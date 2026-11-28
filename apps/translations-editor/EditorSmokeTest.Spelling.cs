using Runic.Spelling;

namespace Runic.Translations.Editor;

internal static partial class EditorSmokeTest
{
    /// <summary>A spell checker of its own folders: the bundled English dictionaries, no system ones, a download
    /// folder and word list in <paramref name="container"/>, and the system language <c>de-CH</c>. Its downloads copy
    /// the bundled <c>en_US</c> under the catalog entry's name into an application folder instead of fetching it; the
    /// catalog's hashes belong to the package's own download tests. <c>it</c>'s download fails.</summary>
    private static EditorSpelling SmokeSpelling(string container, List<string> downloads)
    {
        string installed = Path.Combine(container, "installed");
        Directory.CreateDirectory(installed);
        return new EditorSpelling(new EditorSpellingOptions
        {
            Dictionaries = new SpellingDictionariesOptions
            {
                // Naming folders replaces the default one beside the program, which holds the bundled English.
                ApplicationDirectories = [Path.Combine(AppContext.BaseDirectory, "dictionaries"), installed],
                DownloadDirectory = Path.Combine(container, "downloads"),
                UseSystemDictionaries = false,
                UserLanguages = ["de-CH"],
            },
            SystemLanguages = static () => ["de-CH", "en-US"],
            UserWordsPath = Path.Combine(container, "user-words.txt"),
            Download = async (dictionaries, entry, progress, cancellationToken) =>
            {
                lock (downloads) downloads.Add(entry.Name);
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                if (entry.LanguageTag.StartsWith("it", StringComparison.Ordinal)) throw new HttpRequestException("offline");
                SpellingDictionary english = dictionaries.Find("en-US") ?? throw new InvalidOperationException("No bundled en_US.");
                progress.Report(new SpellingDownloadProgress(entry.Size / 2, entry.Size));
                File.Copy(english.AffixPath, Path.Combine(installed, entry.Name + ".aff"), overwrite: true);
                File.Copy(english.DictionaryPath, Path.Combine(installed, entry.Name + ".dic"), overwrite: true);
            },
        });
    }

    // D074.10, D086.2: each language has its own checker, placeholders and markup are skipped, the system language's
    // dictionary downloads on its own (unless the setting is off), any other language only when the user asks.
    private static async Task SpellingJourneyAsync(string container)
    {
        Directory.CreateDirectory(container);
        var downloads = new List<string>();
        using var spelling = SmokeSpelling(container, downloads);

        EditorSpellingStatus status = spelling.Status(new EditorSpellingStatusRequest(["en", "de-CH", "fr"], AutomaticDownload: false));
        Require(status.SystemLanguage == "de-CH", "The system language is not the first of the user's languages.");
        Require(Language(status, "en") is { State: EditorSpelling.Ready, Source: "bundled", SystemLanguage: false },
            "English is not checked with the bundled dictionary: " + Language(status, "en"));
        Require(Language(status, "de-CH") is { State: EditorSpelling.Downloadable, SystemLanguage: true, DownloadName: "de_CH", DownloadSize: > 0 },
            "The system language's dictionary is not offered: " + Language(status, "de-CH"));
        Require(Language(status, "fr") is { State: EditorSpelling.Downloadable, SystemLanguage: false },
            "French is not offered for download: " + Language(status, "fr"));
        Require(Language(status, "fr")!.License is { Length: > 0 }, "A downloadable dictionary does not name its license.");
        await Task.Delay(100).ConfigureAwait(false);
        Require(downloads.Count == 0, "A dictionary downloaded with the setting off: " + string.Join(",", downloads));

        const string pattern = "Hello {$nmae}, teh {#link href=|https://exmaple.test|}documentaton{/link} is \\{here\\}.";
        const string message = ".input {$cuont :number}\n.match $cuont\none {{One fiel for {$cuont}}}\n* {{{$cuont} fiels}}";
        EditorSpellingCheckResult checkedEnglish = await spelling.CheckAsync(new EditorSpellingCheckRequest("en",
            [new EditorSpellingText("pattern", pattern), new EditorSpellingText("message", message, "message")])).ConfigureAwait(false);
        Require(checkedEnglish.State == EditorSpelling.Ready, "English was not checked: " + checkedEnglish.State);
        string[] patternWords = checkedEnglish.Results.Single(result => result.Id == "pattern").Misspellings.Select(word => word.Word).ToArray();
        Require(patternWords.SequenceEqual(["teh", "documentaton"]),
            "The pattern's misspellings are not its words alone (placeholders, markup and escapes skipped): " + string.Join(",", patternWords));
        EditorMisspelling teh = checkedEnglish.Results.Single(result => result.Id == "pattern").Misspellings[0];
        Require(pattern.Substring(teh.Start, teh.Length) == "teh", "A misspelling's offsets do not point into the text.");
        string[] messageWords = checkedEnglish.Results.Single(result => result.Id == "message").Misspellings.Select(word => word.Word).ToArray();
        Require(messageWords.SequenceEqual(["fiel", "fiels"]),
            "A complex message's misspellings are not the words of its patterns: " + string.Join(",", messageWords));

        EditorSpellingSuggestions suggestions = await spelling.SuggestAsync(new EditorSpellingSuggestRequest("en", "teh")).ConfigureAwait(false);
        Require(suggestions.Suggestions.Contains("the"), "No suggestion corrects 'teh': " + string.Join(",", suggestions.Suggestions));
        Require((await spelling.AddWordAsync(new EditorSpellingWordRequest("documentaton")).ConfigureAwait(false)).Added,
            "The word was not added to the user's word list.");
        EditorSpellingCheckResult afterAdd = await spelling.CheckAsync(new EditorSpellingCheckRequest("en", [new EditorSpellingText("pattern", pattern)])).ConfigureAwait(false);
        Require(afterAdd.Results[0].Misspellings.Select(word => word.Word).SequenceEqual(["teh"]), "An added word is still marked.");
        Require(File.ReadAllText(Path.Combine(container, "user-words.txt")).Contains("documentaton", StringComparison.Ordinal),
            "The user's word list was not written.");

        EditorSpellingCheckResult missing = await spelling.CheckAsync(new EditorSpellingCheckRequest("fr", [new EditorSpellingText("a", "Bonjour")])).ConfigureAwait(false);
        Require(missing is { State: EditorSpelling.Downloadable } && missing.Results[0].Misspellings.Count == 0,
            "A language without a dictionary is not reported as downloadable: " + missing.State);

        status = spelling.Status(new EditorSpellingStatusRequest(["en", "de-CH", "fr"], AutomaticDownload: true));
        Require(Language(status, "de-CH") is { Automatic: true, State: EditorSpelling.Downloading or EditorSpelling.Ready },
            "The system language's dictionary did not download on its own: " + Language(status, "de-CH"));
        await WaitForSpellingAsync(spelling, "de-CH", EditorSpelling.Ready).ConfigureAwait(false);
        Require(downloads.SequenceEqual(["de_CH"]), "Another language than the system's downloaded without asking: " + string.Join(",", downloads));
        spelling.Status(new EditorSpellingStatusRequest(["de-CH"], AutomaticDownload: true));
        Require(downloads.Count == 1, "The system language's dictionary downloaded twice.");

        Require(spelling.Download("fr") is { State: EditorSpelling.Downloading or EditorSpelling.Ready, Automatic: false },
            "The asked-for download did not start.");
        await WaitForSpellingAsync(spelling, "fr", EditorSpelling.Ready).ConfigureAwait(false);
        Require((await spelling.CheckAsync(new EditorSpellingCheckRequest("fr", [new EditorSpellingText("a", "teh")])).ConfigureAwait(false)).State == EditorSpelling.Ready,
            "A downloaded dictionary does not check.");

        spelling.Download("it");
        await WaitForSpellingAsync(spelling, "it", EditorSpelling.Failed).ConfigureAwait(false);
        Require(spelling.Download("it").State == EditorSpelling.Downloading, "A failed download cannot be tried again.");
        await WaitForSpellingAsync(spelling, "it", EditorSpelling.Failed).ConfigureAwait(false);
        Require(spelling.Status(new EditorSpellingStatusRequest(["qaa"], false)).Languages.Single().State == EditorSpelling.Unavailable,
            "A language without any dictionary is not unavailable.");
        Console.WriteLine("PASS: spelling checks each language with its own dictionary, skips placeholders and markup, and downloads the system language's dictionary on its own and others when asked.");
    }

    private static EditorSpellingLanguage? Language(EditorSpellingStatus status, string locale) =>
        status.Languages.SingleOrDefault(language => language.Locale == locale);

    private static async Task WaitForSpellingAsync(EditorSpelling spelling, string locale, string state)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (spelling.Status(new EditorSpellingStatusRequest([locale], false)).Languages.Single().State == state) return;
            await Task.Delay(25).ConfigureAwait(false);
        }
        throw new InvalidOperationException($"The spelling state of {locale} did not become {state}.");
    }
}
