using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: MetadataUpdateHandler(typeof(Runic.Translations.TranslationHotReload))]

namespace Runic.Translations;

/// <summary>
/// .NET Hot Reload of catalog text. A text-only edit of a <c>.rmf2</c> or <c>.mf2</c> file under <c>dotnet watch</c>
/// or a debugger regenerates the catalog's embedded locale data, which Hot Reload applies as new method bodies. This
/// handler then drops the values the catalogs built from the old data and refreshes every live
/// <see cref="TranslationManager"/> and <see cref="TranslationContext"/>, whose managers republish their snapshot
/// (<see cref="ITranslationManager.SnapshotPublished"/> with <see cref="TranslationSnapshotPublishReason.Refresh"/>), so
/// bound views update.
/// </summary>
/// <remarks>
/// Nothing is tracked unless the runtime supports metadata updates (<see cref="MetadataUpdater.IsSupported"/>), which is
/// false in release and trimmed or native AOT builds. An edit that changes the caller contract (a new key or input) is
/// not a text-only edit; Hot Reload asks for a restart then.
/// </remarks>
internal static class TranslationHotReload
{
    private static readonly ConditionalWeakTable<CompiledTranslationCatalog, object?> Catalogs = new();
    private static readonly ConditionalWeakTable<TranslationManager, object?> Managers = new();
    private static readonly ConditionalWeakTable<TranslationContext, object?> Contexts = new();

    /// <summary>Whether Hot Reload can update this process, so generated data may change after start-up.</summary>
    internal static bool IsSupported => SupportedOverrideValue.Value ?? MetadataUpdater.IsSupported;

    private static readonly System.Threading.AsyncLocal<bool?> SupportedOverrideValue = new();

    /// <summary>
    /// Tests set this to exercise the handler in a process that Hot Reload does not update. It applies to the setting
    /// code's async flow only, so objects that tests running in parallel create stay untracked.
    /// </summary>
    internal static bool? SupportedOverride
    {
        get => SupportedOverrideValue.Value;
        set => SupportedOverrideValue.Value = value;
    }

    internal static void Track(CompiledTranslationCatalog catalog)
    {
        if (IsSupported) Catalogs.AddOrUpdate(catalog, null);
    }

    internal static void Track(TranslationManager manager)
    {
        if (IsSupported) Managers.AddOrUpdate(manager, null);
    }

    internal static void Track(TranslationContext context)
    {
        if (IsSupported) Contexts.AddOrUpdate(context, null);
    }

    /// <summary>Called by the runtime after an update, before <see cref="UpdateApplication"/>.</summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The MetadataUpdateHandler signature.")]
    internal static void ClearCache(Type[]? updatedTypes)
    {
        foreach ((CompiledTranslationCatalog catalog, _) in Catalogs) catalog.ResetDeferredData();
    }

    /// <summary>Called by the runtime after an update: refreshes every live manager and context.</summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The MetadataUpdateHandler signature.")]
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        foreach ((TranslationManager manager, _) in Managers) _ = RefreshAsync(manager.RefreshAsync);
        foreach ((TranslationContext context, _) in Contexts) _ = RefreshAsync(context.RefreshAsync);
    }

    private static async Task RefreshAsync(Func<System.Threading.CancellationToken, ValueTask> refresh)
    {
        try
        {
            await refresh(default).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The manager keeps its snapshot; a development session shows why the new text was not applied.
            Console.Error.WriteLine($"Runic.Translations: the catalog text could not be reloaded: {exception.Message}");
        }
    }
}
