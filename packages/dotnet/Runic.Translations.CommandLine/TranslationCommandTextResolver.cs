using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
/// The application's catalog is tried first, so it can override any framework text and translate its own
/// description keys. Framework keys it does not define use the built-in English and German text in the locale of
/// the application's snapshot, or of the invocation culture when there is no application catalog. Anything else
/// returns <see langword="null"/>, and Runic.CommandLine writes its English text. A message whose inputs do not fit
/// the supplied arguments, or that fails to format, is skipped the same way.
/// </para>
/// </remarks>
public sealed class TranslationCommandTextResolver : ICommandTextResolver
{
    private static readonly ConcurrentDictionary<string, ITranslationSnapshot> BuiltInSnapshots = new(StringComparer.Ordinal);
    private static ITranslationProvider? s_builtInProvider;

    private readonly ITranslationManager? _manager;
    private readonly ITranslationSnapshot? _snapshot;

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

    /// <inheritdoc />
    public string? Resolve(string key, CultureInfo culture, IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentNullException.ThrowIfNull(arguments);

        string name = GetMessageName(key);
        string[] names = FrameworkTextArguments.For(key);
        ITranslationSnapshot? application = _manager?.Current ?? _snapshot;
        if (application is not null && TryFormat(application, name, names, arguments, out string? text)) return text;
        if (!FrameworkTextArguments.ByKey.ContainsKey(key)) return null;
        return TryFormat(BuiltIn(application?.Locale ?? culture.Name), name, names, arguments, out text) ? text : null;
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

    private static bool TryFormat(ITranslationSnapshot snapshot, string name, string[] names, IReadOnlyList<string> arguments, out string? text)
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
            return false;
        }
    }

    private static int Position(string input, string[] names, int count)
    {
        for (int index = 0; index < names.Length && index < count; index++)
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
                argument = TextArgument.CreateRmf2(input.Name, new TextArgument("_", value));
                return true;
            case TextArgumentType.Int when long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole):
                argument = TextArgument.CreateRmf2(input.Name, new TextArgument("_", whole, input.Format));
                return true;
            case TextArgumentType.Number when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal number):
                argument = TextArgument.CreateRmf2(input.Name, new TextArgument("_", number, input.Format));
                return true;
            default:
                argument = default;
                return false;
        }
    }

    private static ITranslationSnapshot BuiltIn(string locale)
    {
        if (locale.Length == 0) locale = CommandLineTextCatalog.DefaultLocale;
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
