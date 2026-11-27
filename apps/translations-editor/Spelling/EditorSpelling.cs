using Runic.Platform;
using Runic.Spelling;

namespace Runic.Translations.Editor;

/// <summary>What <see cref="EditorSpelling"/> reads: the dictionaries, the user's languages and word list, and how a
/// dictionary is downloaded. The defaults are the editor's; the smoke test replaces them.</summary>
internal sealed record EditorSpellingOptions
{
    /// <summary>Where dictionaries are found and downloaded to; by default the bundled English ones, the shared
    /// download folder of Runic applications and the system's Hunspell dictionaries.</summary>
    public SpellingDictionariesOptions? Dictionaries { get; init; }

    /// <summary>The user's languages, most preferred first; the first is the system language (D086.2).</summary>
    public Func<IReadOnlyList<string>>? SystemLanguages { get; init; }

    /// <summary>The user's word list; by default the one all Runic applications share.</summary>
    public string? UserWordsPath { get; init; }

    /// <summary>Downloads a catalog dictionary; by default <see cref="SpellingDictionaries.DownloadAsync"/>, which
    /// checks each file's size and SHA-256 against the package's pinned catalog before it installs it.</summary>
    public Func<SpellingDictionaries, SpellingCatalogEntry, IProgress<SpellingDownloadProgress>, CancellationToken, Task>? Download { get; init; }
}

/// <summary>
/// The editor's own spell checker (D074.10): one Hunspell checker per edited language, through <c>Runic.Spelling</c>,
/// which skips placeholders and markup (<see cref="EditorSpellingRanges"/>). The page turns the webview's checker off
/// and draws the marks itself.
/// </summary>
/// <remarks>
/// Dictionaries (D086.2): the system language's dictionary is downloaded on its own when a project has that language,
/// none is installed and the page's setting allows it, once per run; any other language is downloaded only when the
/// user asks (<see cref="Download"/>). Downloads come only from the package's pinned catalog and run in the background,
/// so the page's serialized operations never wait for one; the page asks <see cref="Status"/> for its progress.
/// </remarks>
internal sealed class EditorSpelling : IDisposable
{
    internal const string Ready = "ready";
    internal const string Downloadable = "downloadable";
    internal const string Downloading = "downloading";
    internal const string Failed = "failed";
    internal const string Unavailable = "unavailable";

    private readonly SpellingDictionaries _dictionaries;
    private readonly Func<IReadOnlyList<string>> _systemLanguages;
    private readonly Func<SpellingDictionaries, SpellingCatalogEntry, IProgress<SpellingDownloadProgress>, CancellationToken, Task> _download;
    private readonly Lazy<Task<SpellingUserWords>> _userWords;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Task<SpellChecker>> _checkers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DownloadState> _downloads = new(StringComparer.Ordinal);
    private readonly HashSet<string> _automaticStarted = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();

    internal EditorSpelling(EditorSpellingOptions? options = null)
    {
        options ??= new EditorSpellingOptions();
        _systemLanguages = options.SystemLanguages ?? (static () => DesktopLanguages.Preferred);
        _dictionaries = new SpellingDictionaries(options.Dictionaries ?? new SpellingDictionariesOptions
        {
            UserLanguages = _systemLanguages(),
        });
        _download = options.Download ?? (static (dictionaries, entry, progress, cancellationToken) =>
            dictionaries.DownloadAsync(entry, progress, cancellationToken));
        string? userWordsPath = options.UserWordsPath;
        _userWords = new Lazy<Task<SpellingUserWords>>(() => SpellingUserWords.OpenAsync(userWordsPath, _lifetime.Token));
    }

    /// <summary>The system language, the first of the user's languages; <see langword="null"/> when the system names
    /// none.</summary>
    internal string? SystemLanguage => _systemLanguages() is [var first, ..] ? first : null;

    /// <summary>What checks each of <paramref name="request"/>'s languages, and the download of the system language's
    /// dictionary when it is missing and the request allows it.</summary>
    internal EditorSpellingStatus Status(EditorSpellingStatusRequest request)
    {
        string? system = SystemLanguage;
        var languages = new List<EditorSpellingLanguage>(request.Locales.Count);
        foreach (string locale in request.Locales.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(locale)) continue;
            bool isSystem = system is not null && SameLanguage(locale, system);
            SpellingAvailability availability = Availability(locale);
            if (request.AutomaticDownload && isSystem && availability.Installed is null && availability.Download is { } entry)
            {
                bool start;
                lock (_gate) start = _automaticStarted.Add(entry.Name);
                if (start) StartDownload(entry, automatic: true);
            }
            languages.Add(Describe(locale, availability, isSystem));
        }
        return new EditorSpellingStatus(system, languages);
    }

    /// <summary>Starts the download of the dictionary for <paramref name="locale"/> that the user asked for, unless it
    /// is running; returns the language's state at once.</summary>
    internal EditorSpellingLanguage Download(string locale)
    {
        SpellingAvailability availability = Availability(locale);
        if (availability.Installed is null && availability.Download is { } entry) StartDownload(entry, automatic: false);
        string? system = SystemLanguage;
        return Describe(locale, Availability(locale), system is not null && SameLanguage(locale, system));
    }

    /// <summary>The misspelled words of each text in <paramref name="request"/>'s language, without its placeholders
    /// and markup. Without a dictionary every text has none, and the state says why.</summary>
    internal async Task<EditorSpellingCheckResult> CheckAsync(EditorSpellingCheckRequest request)
    {
        SpellChecker? checker = await CheckerAsync(request.Locale).ConfigureAwait(false);
        if (checker is null)
        {
            string state = Describe(request.Locale, Availability(request.Locale), isSystem: false).State;
            return new EditorSpellingCheckResult(request.Locale, state,
                request.Texts.Select(static text => new EditorSpellingTextResult(text.Id, [])).ToArray());
        }
        var results = new List<EditorSpellingTextResult>(request.Texts.Count);
        foreach (EditorSpellingText text in request.Texts)
        {
            EditorSpellingTextKind kind = string.Equals(text.Kind, "message", StringComparison.Ordinal)
                ? EditorSpellingTextKind.Message : EditorSpellingTextKind.Pattern;
            SpellingRange[] skip = EditorSpellingRanges.Skip(text.Text, kind)
                .Select(static range => new SpellingRange(range.Start, range.Length)).ToArray();
            results.Add(new EditorSpellingTextResult(text.Id, checker.Check(text.Text, skip)
                .Select(static word => new EditorMisspelling(word.Start, word.Length, word.Word)).ToArray()));
        }
        return new EditorSpellingCheckResult(request.Locale, Ready, results);
    }

    /// <summary>Up to eight corrections of <paramref name="request"/>'s word in its language.</summary>
    internal async Task<EditorSpellingSuggestions> SuggestAsync(EditorSpellingSuggestRequest request)
    {
        SpellChecker? checker = await CheckerAsync(request.Locale).ConfigureAwait(false);
        return new EditorSpellingSuggestions(request.Locale, request.Word, checker?.Suggest(request.Word) ?? []);
    }

    /// <summary>Adds a word to the user's own list, which every Runic application of the user shares; every checker
    /// knows it at once.</summary>
    internal async Task<EditorSpellingWordResult> AddWordAsync(EditorSpellingWordRequest request)
    {
        string word = request.Word.Trim();
        if (word.Length == 0) throw new ArgumentException("The word is empty.");
        SpellingUserWords userWords = await _userWords.Value.ConfigureAwait(false);
        return new EditorSpellingWordResult(word, await userWords.AddAsync(word, _lifetime.Token).ConfigureAwait(false));
    }

    private SpellingAvailability Availability(string locale) => _dictionaries.GetAvailability(locale);

    private async Task<SpellChecker?> CheckerAsync(string locale)
    {
        if (_dictionaries.Find(locale) is not { } dictionary) return null;
        string key = $"{dictionary.Source}\n{dictionary.DictionaryPath}";
        Task<SpellChecker> load;
        lock (_gate)
        {
            if (!_checkers.TryGetValue(key, out load!) || load.IsFaulted || load.IsCanceled)
                _checkers[key] = load = LoadAsync(dictionary);
        }
        try
        {
            return await load.ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            // A downloaded dictionary changed on disk since it was found: it is offered for download again.
            return null;
        }
    }

    private async Task<SpellChecker> LoadAsync(SpellingDictionary dictionary)
    {
        SpellingUserWords userWords = await _userWords.Value.ConfigureAwait(false);
        return await SpellChecker.LoadAsync(dictionary, new SpellCheckerOptions { UserWords = userWords }, _lifetime.Token)
            .ConfigureAwait(false);
    }

    private void StartDownload(SpellingCatalogEntry entry, bool automatic)
    {
        DownloadState state;
        lock (_gate)
        {
            if (_downloads.TryGetValue(entry.Name, out DownloadState? running) && !running.Failed) return;
            _downloads[entry.Name] = state = new DownloadState(automatic);
        }
        CancellationToken cancellationToken = _lifetime.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await _download(_dictionaries, entry, state, cancellationToken).ConfigureAwait(false);
                lock (_gate) _downloads.Remove(entry.Name);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The editor is closing.
            }
            catch (Exception)
            {
                // Nothing was installed (the package installs a dictionary only after every file's size and hash
                // matched); the page offers the download again.
                state.Failed = true;
            }
        }, CancellationToken.None);
    }

    private EditorSpellingLanguage Describe(string locale, SpellingAvailability availability, bool isSystem)
    {
        if (availability.Installed is { } installed)
            return new EditorSpellingLanguage(locale, Ready, isSystem, installed.Name, SourceName(installed.Source));
        if (availability.Download is not { } entry)
            return new EditorSpellingLanguage(locale, Unavailable, isSystem);
        DownloadState? download;
        lock (_gate) _downloads.TryGetValue(entry.Name, out download);
        string state = download is null ? Downloadable : download.Failed ? Failed : Downloading;
        return new EditorSpellingLanguage(locale, state, isSystem, DownloadName: entry.Name, DownloadSize: entry.Size,
            BytesReceived: download is { Failed: false } ? download.BytesReceived : null,
            Automatic: download?.Automatic ?? false, License: entry.License);
    }

    private static string SourceName(SpellingDictionarySource source) => source switch
    {
        SpellingDictionarySource.Bundled => "bundled",
        SpellingDictionarySource.Downloaded => "downloaded",
        _ => "system",
    };

    /// <summary>Whether two BCP 47 or POSIX tags name the same language (<c>de-CH</c> and <c>de_DE</c> do).</summary>
    internal static bool SameLanguage(string left, string right) =>
        string.Equals(Language(left), Language(right), StringComparison.OrdinalIgnoreCase);

    private static string Language(string tag)
    {
        int end = tag.IndexOfAny(['-', '_', '.', '@']);
        return end < 0 ? tag : tag[..end];
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    private sealed class DownloadState(bool automatic) : IProgress<SpellingDownloadProgress>
    {
        private long _bytesReceived;
        private volatile bool _failed;

        internal bool Automatic { get; } = automatic;
        internal bool Failed { get => _failed; set => _failed = value; }
        internal long BytesReceived => Interlocked.Read(ref _bytesReceived);

        public void Report(SpellingDownloadProgress value) => Interlocked.Exchange(ref _bytesReceived, value.BytesReceived);
    }
}

/// <param name="Locales">The project's languages.</param>
/// <param name="AutomaticDownload">The page's setting: whether the system language's dictionary may be downloaded
/// without asking (D086.2).</param>
internal sealed record EditorSpellingStatusRequest(IReadOnlyList<string> Locales, bool AutomaticDownload);

/// <param name="SystemLanguage">The first of the user's languages, whose dictionary downloads on its own.</param>
internal sealed record EditorSpellingStatus(string? SystemLanguage, IReadOnlyList<EditorSpellingLanguage> Languages);

/// <summary>What checks one language.</summary>
/// <param name="State"><c>ready</c>, <c>downloadable</c>, <c>downloading</c>, <c>failed</c> (the last download) or
/// <c>unavailable</c> (no dictionary exists).</param>
/// <param name="Dictionary">The installed dictionary's name, such as <c>en_US</c>.</param>
/// <param name="Source"><c>bundled</c>, <c>downloaded</c> or <c>system</c>.</param>
/// <param name="DownloadName">The catalog dictionary that would be downloaded, such as <c>de_CH</c>.</param>
/// <param name="Automatic">Whether the running download started on its own (the system language).</param>
/// <param name="License">The catalog dictionary's license, an SPDX expression.</param>
internal sealed record EditorSpellingLanguage(
    string Locale,
    string State,
    bool SystemLanguage,
    string? Dictionary = null,
    string? Source = null,
    string? DownloadName = null,
    long? DownloadSize = null,
    long? BytesReceived = null,
    bool Automatic = false,
    string? License = null);

internal sealed record EditorSpellingCheckRequest(string Locale, IReadOnlyList<EditorSpellingText> Texts);

/// <param name="Id">The page's name for the text, returned with its result.</param>
/// <param name="Kind"><c>pattern</c> (the default) or <c>message</c>, a whole message's MF2 source.</param>
internal sealed record EditorSpellingText(string Id, string Text, string? Kind = null);

internal sealed record EditorSpellingCheckResult(string Locale, string State, IReadOnlyList<EditorSpellingTextResult> Results);

internal sealed record EditorSpellingTextResult(string Id, IReadOnlyList<EditorMisspelling> Misspellings);

/// <param name="Start">The word's first UTF-16 code unit in the text.</param>
internal sealed record EditorMisspelling(int Start, int Length, string Word);

internal sealed record EditorSpellingSuggestRequest(string Locale, string Word);

internal sealed record EditorSpellingSuggestions(string Locale, string Word, IReadOnlyList<string> Suggestions);

internal sealed record EditorSpellingDownloadRequest(string Locale);

internal sealed record EditorSpellingWordRequest(string Word);

internal sealed record EditorSpellingWordResult(string Word, bool Added);
