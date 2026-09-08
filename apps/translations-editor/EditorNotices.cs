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
