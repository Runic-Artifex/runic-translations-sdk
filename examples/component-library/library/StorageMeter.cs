using Runic.Translations;

namespace Acme.Widgets;

/// <summary>A storage meter's text, in the locale of the application's translation context.</summary>
/// <remarks>
/// The library creates no translation manager of its own: the application registers
/// <see cref="WidgetsTextCatalog.Registration"/> in its <see cref="TranslationContext"/>, so its locale switches
/// apply here too, and its build-time overrides replace single messages.
/// </remarks>
public sealed class StorageMeter(ITranslationContext translations)
{
    private readonly WidgetsText _text = new(translations);

    /// <summary>The meter's heading.</summary>
    public string Title => _text.Messages.storage_title;

    /// <summary>The number of files and the space they use, or a hint while there are none.</summary>
    public string Describe(long files, decimal usedGigabytes, decimal totalGigabytes) => files == 0
        ? _text.Messages.storage_empty
        : _text.Messages.storage_files(count: files) + " · " + _text.Messages.storage_usage(used: usedGigabytes, total: totalGigabytes);
}
