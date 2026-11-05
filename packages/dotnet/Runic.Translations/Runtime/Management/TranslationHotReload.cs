using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

[assembly: MetadataUpdateHandler(typeof(Runic.Translations.TranslationHotReload))]

namespace Runic.Translations;

/// <summary>
/// .NET Hot Reload of catalog text. A text-only edit of a <c>.rmf2</c> or <c>.mf2</c> file under <c>dotnet watch</c>
/// or a debugger regenerates the catalog's embedded locale data, which Hot Reload applies as new method bodies. This
/// handler then drops the values the catalogs built from the old data, links changed markup contracts again, and refreshes
/// the live <see cref="TranslationManager"/> and <see cref="TranslationContext"/> objects of those catalogs, whose managers
/// republish their snapshot (<see cref="ITranslationManager.SnapshotPublished"/> with
/// <see cref="TranslationSnapshotPublishReason.Refresh"/>), so bound views update. The other locales that providers cached
/// are built again when they are next requested.
/// </summary>
/// <remarks>
/// Nothing is tracked unless the runtime supports metadata updates (<see cref="MetadataUpdater.IsSupported"/>), which is
/// false in release and trimmed or native AOT builds. An edit that changes the caller contract (a new key or input) is
/// not a text-only edit; Hot Reload asks for a restart then. Only catalogs whose generated data type is among the updated
/// types are reloaded; when the runtime does not name the updated types, every catalog is.
/// </remarks>
internal static class TranslationHotReload
{
    private static readonly ConditionalWeakTable<CompiledTranslationCatalog, object?> Catalogs = new();
    private static readonly ConditionalWeakTable<TranslationManager, object?> Managers = new();
    private static readonly ConditionalWeakTable<TranslationContext, object?> Contexts = new();
    private static readonly ConditionalWeakTable<Rmf2MarkupContract, object?> Contracts = new();
    private static readonly object Gate = new();
    // What ClearCache changed, for the UpdateApplication call that follows it: the reset catalog IDs, and whether a markup
    // contract was linked again.
    private static HashSet<string>? _resetCatalogs;
    private static bool _contractsChanged;

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

    internal static void Track(Rmf2MarkupContract contract)
    {
        if (IsSupported) Contracts.AddOrUpdate(contract, null);
    }

    /// <summary>
    /// Called by the runtime after an update, before <see cref="UpdateApplication"/>: resets the catalogs whose generated
    /// types changed (all of them when <paramref name="updatedTypes"/> is null) and links changed markup contracts again.
    /// </summary>
    internal static void ClearCache(Type[]? updatedTypes)
    {
        lock (Gate)
        {
            var reset = new HashSet<string>(StringComparer.Ordinal);
            foreach ((CompiledTranslationCatalog catalog, _) in Catalogs)
            {
                if (!catalog.IsAffectedBy(updatedTypes)) continue;
                catalog.ResetDeferredData();
                reset.Add(catalog.Catalog);
            }

            bool contractsChanged = false;
            foreach ((Rmf2MarkupContract contract, _) in Contracts)
            {
                int before = contract.LinkVersion;
                try
                {
                    contract.Relink();
                }
                catch (ArgumentException exception)
                {
                    Console.Error.WriteLine($"Runic.Translations: the markup contract could not be linked again: {exception.Message}");
                }

                contractsChanged |= contract.LinkVersion != before;
            }

            if (_resetCatalogs is null) _resetCatalogs = reset;
            else _resetCatalogs.UnionWith(reset);
            _contractsChanged |= contractsChanged;
        }
    }

    /// <summary>
    /// Called by the runtime after an update: refreshes the live managers and contexts of the catalogs that
    /// <see cref="ClearCache"/> reset, and every context when a markup contract changed (its overlays are checked again).
    /// </summary>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The MetadataUpdateHandler signature.")]
    internal static void UpdateApplication(Type[]? updatedTypes)
    {
        HashSet<string> reset;
        bool contractsChanged;
        lock (Gate)
        {
            reset = _resetCatalogs ?? [];
            contractsChanged = _contractsChanged;
            _resetCatalogs = null;
            _contractsChanged = false;
        }

        foreach ((TranslationManager manager, _) in Managers)
        {
            if (reset.Contains(manager.CatalogId)) _ = RefreshAsync(manager.RefreshAsync);
        }

        foreach ((TranslationContext context, _) in Contexts)
        {
            if (contractsChanged || context.ContainsAnyCatalog(reset)) _ = RefreshAsync(context.RefreshAsync);
        }
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
