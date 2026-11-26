using System.Globalization;
using Runic.Translations.Editor.Generated;

namespace Runic.Translations.Editor;

internal sealed record EditorNoticeArgument(string Name, string? Value = null, double? Number = null);

internal sealed record EditorNotice(string Code, IReadOnlyList<EditorNoticeArgument> Args, string? Detail = null)
{
    internal static EditorNotice Create(string code, params (string Name, string Value)[] args) =>
        new(code, args.Select(static arg => new EditorNoticeArgument(arg.Name, arg.Value)).ToArray());
    internal static EditorNotice External(string detail) => new("ui_backend_external_error", [], detail);
    internal static EditorNotice FromException(Exception exception) => exception is EditorUserException owned
        ? owned.Notice : External(exception.Message);
    public override string ToString() => Code + (Detail is null ? string.Empty : ": " + Detail);
}

internal sealed class EditorUserException(EditorNotice notice) : ArgumentException(notice.Code)
{
    internal EditorNotice Notice { get; } = notice;
}

/// <summary>
/// A notice as text for the command line (#280): the editor's catalog renders it, as the page does
/// (<c>displayNotice</c> in <c>Frontend/src/lib/ui-text.ts</c>), in the base language, which the rest of the command
/// line's output speaks. A code the catalog does not have stays as it is.
/// </summary>
internal static class EditorNoticeText
{
    private static readonly Lazy<ITranslationSnapshot> Snapshot = new(static () => EditorTextCatalog.CreateManager().Current);

    internal static string Render(EditorNotice notice)
    {
        ITranslationSnapshot snapshot = Snapshot.Value;
        if (!snapshot.TryGetKey(notice.Code, out TranslationKey key)) return notice.ToString();
        bool external = notice.Code == "ui_backend_external_error";
        TextArgument[] arguments = [];
        if (snapshot.TryGetPlaceholders(key, out ReadOnlyMemory<TranslationPlaceholderDescriptor> placeholders))
        {
            arguments = new TextArgument[placeholders.Length];
            for (int index = 0; index < placeholders.Length; index++)
            {
                TranslationPlaceholderDescriptor placeholder = placeholders.Span[index];
                EditorNoticeArgument? argument = notice.Args.FirstOrDefault(item => item.Name == placeholder.Name);
                string? value = external && placeholder.Name == "detail" ? notice.Detail : argument?.Value;
                double? number = argument?.Number ?? (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null);
                arguments[index] = placeholder.Type switch
                {
                    TextArgumentType.Int => new TextArgument(placeholder.Name, (long)(number ?? 0)),
                    TextArgumentType.Number => new TextArgument(placeholder.Name, (decimal)(number ?? 0)),
                    _ => new TextArgument(placeholder.Name, value ?? (number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)),
                };
            }
        }
        string text = snapshot.Format(key, arguments);
        return notice.Detail is null || external ? text : text + " " + notice.Detail;
    }
}
