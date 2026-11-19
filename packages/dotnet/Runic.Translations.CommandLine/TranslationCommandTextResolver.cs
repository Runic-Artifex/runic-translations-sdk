using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Runic.CommandLine;
using Runic.Translations.CommandLine.Translations;

namespace Runic.Translations.CommandLine;

/// <summary>Translates Runic.CommandLine framework and application text with Runic Translations.</summary>
/// <remarks>
/// <para>
/// Each text key is looked up by name: <see cref="GetMessageName"/> removes the hyphens of the key and capitalizes
/// the following letter, so <c>help.show-help</c> is the message <c>help.showHelp</c>. A message receives the
/// framework's ordered arguments through its declared inputs, named after the arguments in the framework's text key
/// table (<c>{$option}</c>, <c>{$parameter}</c>) or after their position (<c>{$arg0}</c>, <c>{$arg1}</c>).
/// </para>
/// <para>
/// Framework text comes from the <c>runic-command-line</c> catalog (<see cref="CatalogRegistration"/>), which ships English
/// and German, with the application's build-time overrides (<c>translations/overrides/runic-command-line/</c>) and other
/// overlays when a context registers them; other locales come from such overlays too. The application's catalog
/// translates only the application's own keys, such as its description keys. It is never asked for a framework key: a
/// message in it named after one has no effect, and the build reports it as <c>RTR0107</c>. A key without text returns
/// <see langword="null"/>, and Runic.CommandLine writes its English text. A message whose inputs do not fit the supplied
/// arguments, or that fails to format, is skipped the same way.
/// </para>
/// <para>
/// The locale is the invocation culture for the parameterless constructor and for a
/// <see cref="TranslationContextBuilder"/>. A snapshot, manager or context brings its own locale; when the invocation
/// culture names another language, the resolver keeps the translation locale, so text and formatting stay consistent,
/// and reports it once: a warning line on standard error, so it reaches the user of a shipped console app, and a trace
/// warning. Create the manager or context from the invocation culture, or pass a <see cref="TranslationContextBuilder"/>,
/// to avoid it.
/// </para>
/// </remarks>
public sealed class TranslationCommandTextResolver : ICommandTextResolver
{
    /// <summary>The ID of the built-in framework text catalog, which overlays and contexts name.</summary>
    public const string CatalogId = CommandLineTextCatalog.CatalogId;

    private static readonly ConcurrentDictionary<string, ITranslationSnapshot> BuiltInSnapshots = new(StringComparer.Ordinal);
    private static ITranslationProvider? s_builtInProvider;

    private readonly ITranslationManager? _manager;
    private readonly ITranslationSnapshot? _snapshot;
    private readonly ITranslationContext? _context;
    private readonly TranslationContextBuilder? _catalogs;
    private readonly string? _applicationCatalogId;
    private readonly ConcurrentDictionary<string, TranslationContext>? _contexts;
    private int _reportedCulture;
    private int _reportedLocale;

    /// <summary>Where the culture-mismatch warning goes besides the trace; <see langword="null"/> for standard error. Tests set it.</summary>
    internal TextWriter? WarningOutput { get; init; }

    /// <summary>Creates a resolver with only the built-in framework text, in the invocation culture.</summary>
    public TranslationCommandTextResolver()
    {
    }

    /// <summary>Creates a resolver over a fixed application snapshot, with the built-in framework text in its locale.</summary>
    public TranslationCommandTextResolver(ITranslationSnapshot snapshot) =>
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    /// <summary>Creates a resolver that follows the manager's current snapshot, with the built-in framework text in its locale.</summary>
    public TranslationCommandTextResolver(ITranslationManager manager) =>
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));

    /// <summary>Creates a resolver that follows a translation context's locale.</summary>
    /// <param name="context">
    /// The context. When it registers <see cref="CatalogRegistration"/>, framework text comes from that catalog with the
    /// context's overlays applied (for example a French overlay); otherwise from the built-in catalog in the context's locale.
    /// </param>
    /// <param name="applicationCatalogId">The catalog of the application's own text, such as its description keys, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentException">The context does not register <paramref name="applicationCatalogId"/>.</exception>
    public TranslationCommandTextResolver(ITranslationContext context, string? applicationCatalogId)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        if (applicationCatalogId is not null && !context.TryGetManager(applicationCatalogId, out _))
            throw new ArgumentException($"Catalog '{applicationCatalogId}' is not registered with the translation context.", nameof(applicationCatalogId));
        _applicationCatalogId = applicationCatalogId;
    }

    /// <summary>Creates a resolver that resolves every key in the invocation culture, with contexts built from <paramref name="catalogs"/>.</summary>
    /// <param name="catalogs">
    /// The catalogs and overlays. Register <see cref="CatalogRegistration"/> to attach overlays to the framework text, such
    /// as <c>TranslationOverlay.FromPacks(TranslationCommandTextResolver.CatalogId, packs, [new("fr")])</c>; without it,
    /// framework text comes from the built-in catalog. The resolver builds one context per invocation culture; do not change
    /// the builder afterwards.
    /// </param>
    /// <param name="applicationCatalogId">The catalog of the application's own text, such as its description keys, or <see langword="null"/> for none.</param>
    public TranslationCommandTextResolver(TranslationContextBuilder catalogs, string? applicationCatalogId)
    {
        _catalogs = catalogs ?? throw new ArgumentNullException(nameof(catalogs));
        _applicationCatalogId = applicationCatalogId;
        _contexts = new ConcurrentDictionary<string, TranslationContext>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The registration of the built-in framework text catalog (<see cref="CatalogId"/>), for a
    /// <see cref="TranslationContextBuilder"/> that attaches overlays to it.
    /// </summary>
    public static TranslationCatalogRegistration CatalogRegistration => CommandLineTextCatalog.Registration;

    /// <inheritdoc />
    public string? Resolve(string key, CultureInfo culture, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(arguments);

        string name = GetMessageName(key);
        // The argument names come from the Runic.CommandLine release this adapter is built and packed against (lockstep),
        // so a framework key and its inputs cannot drift between the two packages.
        IReadOnlyList<string>? frameworkArguments = CommandTextKeys.Find(key)?.Arguments;
        (ITranslationSnapshot? application, ITranslationSnapshot? framework, string requested) = Layers(culture);
        if (frameworkArguments is null)
            return application is not null && TryFormat(application, name, [], arguments, out string? applicationText) ? applicationText : null;

        // A framework key comes only from the framework catalog with its overlays, never from the application's catalog
        // (the second override path, removed in 0.15; the build reports such a message as RTR0107).
        ReportMissingFrameworkLocale(application, framework, requested);
        return framework is not null && TryFormat(framework, name, frameworkArguments, arguments, out string? frameworkText) ? frameworkText : null;
    }

    /// <summary>Gets the message name for a text key: each hyphen is removed and the letter after it capitalized.</summary>
    /// <example><c>diagnostics.unknown-option</c> becomes <c>diagnostics.unknownOption</c>; <c>faults.RCLI4000</c> is unchanged.</example>
    public static string GetMessageName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!key.Contains('-', StringComparison.Ordinal)) return key;
        var builder = new StringBuilder(key.Length);
        bool upper = false;
        foreach (char character in key)
        {
            if (character == '-')
            {
                upper = true;
                continue;
            }

            builder.Append(upper && character != '.' ? char.ToUpperInvariant(character) : character);
            upper = false;
        }

        return builder.ToString();
    }

    // The application layer, the framework layer, and the requested locale both resolve.
    private (ITranslationSnapshot? Application, ITranslationSnapshot? Framework, string Requested) Layers(CultureInfo culture)
    {
        if (_catalogs is not null) return FromContext(ContextFor(culture.Name), _applicationCatalogId);
        if (_context is not null)
        {
            ReportCulture(culture, _context.RequestedLocale);
            return FromContext(_context, _applicationCatalogId);
        }

        ITranslationSnapshot? application = _manager?.Current ?? _snapshot;
        if (application is null) return (null, BuiltIn(culture.Name), culture.Name);
        string requested = application.RequestedLocale;
        ReportCulture(culture, requested);
        return (application, BuiltIn(requested), requested);
    }

    private static (ITranslationSnapshot?, ITranslationSnapshot?, string) FromContext(ITranslationContext context, string? applicationCatalogId)
    {
        string requested = context.RequestedLocale;
        ITranslationSnapshot? application = applicationCatalogId is not null && context.TryGetManager(applicationCatalogId, out ITranslationManager? manager)
            ? manager.Current
            : null;
        ITranslationSnapshot framework = context.TryGetManager(CatalogId, out ITranslationManager? frameworkManager)
            ? frameworkManager.Current
            : BuiltIn(requested);
        return (application, framework, requested);
    }

    private TranslationContext ContextFor(string locale)
    {
        if (_contexts!.TryGetValue(locale, out TranslationContext? context)) return context;
        try
        {
            context = _catalogs!.Build(locale);
        }
        catch (Exception exception) when (exception is ArgumentException or TranslationNotFoundException)
        {
            // A culture that is not a valid tag, or that a catalog with the exact-locale policy does not declare, uses the
            // default locale, as the built-in catalog does.
            Trace.TraceWarning($"Runic.Translations.CommandLine: cannot resolve text in culture '{locale}' ({exception.Message}); using the default locale.");
            context = _catalogs!.Build();
        }

        return _contexts.GetOrAdd(locale, context);
    }

    // Reports once when the invocation culture names another language than the translation locale, as when the manager
    // was created with the default locale and CommandApp.Culture is German (values would format in German, text in English).
    private void ReportCulture(CultureInfo culture, string requested)
    {
        if (culture.Name.Length == 0 || requested.Length == 0 || SameLanguage(culture.Name, requested)) return;
        if (Interlocked.Exchange(ref _reportedCulture, 1) != 0) return;
        string message = $"Runic.Translations.CommandLine: the invocation culture '{culture.Name}' differs from the translation locale '{requested}'; " +
            "command-line text uses the translation locale. Create the manager or context from the invocation culture, or pass a TranslationContextBuilder to resolve in the culture.";
        Trace.TraceWarning(message);
        try
        {
            // The default trace listener writes only to an attached debugger, so a shipped console app also gets the line on
            // standard error, which machine-readable standard output does not include.
            (WarningOutput ?? Console.Error).WriteLine("warning: " + message);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A closed standard error must not fail text resolution.
        }
    }

    // Reports once when the application has text in the requested language but runic-command-line has none, so framework
    // lines would fall back to another language, as event 1261 (LocalesWithoutRunicViewsText) does for runic-views. An
    // override file (translations/overrides/runic-command-line/<locale>.rmf2) or an overlay adds the locale.
    private void ReportMissingFrameworkLocale(ITranslationSnapshot? application, ITranslationSnapshot? framework, string requested)
    {
        if (application is null || framework is null || requested.Length == 0 || Volatile.Read(ref _reportedLocale) != 0) return;
        if (!SameLanguage(application.Locale, requested)) return;
        // The content locale of one framework message, which also counts an overlay that adds the locale.
        string frameworkLocale = framework.TryGetKey(GetMessageName(CommandTextKeys.HelpUsage), out TranslationKey probe) &&
            framework.TryGetContentLocale(probe, out string content) ? content : framework.Locale;
        if (SameLanguage(frameworkLocale, requested)) return;
        if (Interlocked.Exchange(ref _reportedLocale, 1) != 0) return;
        string message = $"Runic.Translations.CommandLine: the catalog '{CatalogId}' has no text for the locale '{requested}', which the application's text has; " +
            $"command-line framework text uses '{frameworkLocale}'. Add translations/overrides/{CatalogId}/{Language(requested).ToString()}.rmf2 to the application, " +
            "or an overlay of the catalog (docs/guides/translations/framework-text.md).";
        Trace.TraceWarning(message);
        try
        {
            (WarningOutput ?? Console.Error).WriteLine("warning: " + message);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // A closed standard error must not fail text resolution.
        }
    }

    private static bool SameLanguage(string left, string right) =>
        Language(left).Equals(Language(right), StringComparison.OrdinalIgnoreCase);

    private static ReadOnlySpan<char> Language(string tag)
    {
        int end = tag.AsSpan().IndexOfAny('-', '_');
        return end < 0 ? tag : tag.AsSpan(0, end);
    }

    private static bool TryFormat(ITranslationSnapshot snapshot, string name, IReadOnlyList<string> names, IReadOnlyList<string> arguments, out string? text)
    {
        text = null;
        if (!snapshot.TryGetKey(name, out TranslationKey key) ||
            !snapshot.TryGetPlaceholders(key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders))
            return false;

        try
        {
            ReadOnlySpan<TranslationPlaceholderDescriptor> declared = placeholders.Span;
            var values = new TextArgument[declared.Length];
            for (int index = 0; index < declared.Length; index++)
            {
                int position = Position(declared[index].Name, names, arguments.Count);
                if (position < 0 || !TryCreate(declared[index], arguments[position], out values[index])) return false;
            }

            text = snapshot.Format(key, values);
            return true;
        }
        catch (Exception exception) when (exception is TranslationFormatException or TranslationNotFoundException or ArgumentException)
        {
            // A message that does not fit the framework's arguments falls back to the next source of text.
            text = null;
            return false;
        }
    }

    private static int Position(string input, IReadOnlyList<string> names, int count)
    {
        for (int index = 0; index < names.Count && index < count; index++)
            if (string.Equals(names[index], input, StringComparison.Ordinal)) return index;
        if (input.StartsWith("arg", StringComparison.Ordinal) && input.Length > 3 && input[3] is >= '0' and <= '9' &&
            (input.Length == 4 || input[3] != '0') &&
            int.TryParse(input.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out int position) && position < count)
            return position;
        return -1;
    }

    private static bool TryCreate(TranslationPlaceholderDescriptor input, string value, out TextArgument argument)
    {
        switch (input.Type)
        {
            case TextArgumentType.String:
                argument = Runic.Translations.Generated.GeneratedCatalogSupport.CreateArgument(input.Name, new TextArgument("_", value));
                return true;
            case TextArgumentType.Int when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole):
                argument = Runic.Translations.Generated.GeneratedCatalogSupport.CreateArgument(input.Name, new TextArgument("_", whole, input.Format));
                return true;
            case TextArgumentType.Number when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number):
                argument = Runic.Translations.Generated.GeneratedCatalogSupport.CreateArgument(input.Name, new TextArgument("_", number, input.Format));
                return true;
            default:
                argument = default;
                return false;
        }
    }

    private static ITranslationSnapshot BuiltIn(string locale)
    {
        if (BuiltInSnapshots.TryGetValue(locale, out ITranslationSnapshot? snapshot)) return snapshot;
        ITranslationProvider provider = s_builtInProvider ??= CommandLineTextCatalog.CreateProvider();
        try
        {
            snapshot = Wait(provider.GetSnapshotAsync(locale));
        }
        catch (ArgumentException)
        {
            // A culture name that is not a valid locale tag uses the default locale.
            snapshot = Wait(provider.GetSnapshotAsync(CommandLineTextCatalog.DefaultLocale));
        }

        return BuiltInSnapshots.GetOrAdd(locale, snapshot);
    }

    // Compiled snapshots are created synchronously, so the task has already completed.
    private static ITranslationSnapshot Wait(ValueTask<ITranslationSnapshot> pending) =>
        pending.IsCompletedSuccessfully ? pending.Result : pending.AsTask().GetAwaiter().GetResult();
}
