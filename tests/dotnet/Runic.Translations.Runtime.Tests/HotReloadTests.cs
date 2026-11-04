using System;
using System.Text;
using System.Threading.Tasks;
using TUnit.Core;

namespace Runic.Translations.Runtime.Tests;

// TranslationHotReload: after Hot Reload applies new generated locale data, live managers republish their snapshot.
// The runtime calls ClearCache and UpdateApplication; the tests call them as it does. Hot Reload support is forced in
// the test's own async flow (SupportedOverride is async-local), so the objects of tests running in parallel stay
// untracked; the cases of this class run one at a time because they share the tracked set.
[NotInParallel(nameof(HotReloadTests))]
internal sealed class HotReloadTests
{
    private static readonly TranslationKey Greeting = new("hot", 0, "greeting");

    private static string Data(string text) =>
        "[[0,{\"contentLocale\":\"en\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"" + text + "\"}]}]}}]]";

    [Test, DisplayName("a metadata update republishes live managers with the new generated text")]
    public async Task UpdateRepublishesSnapshot()
    {
        TranslationHotReload.SupportedOverride = true;
        try
        {
            string data = Data("Version one");
            int reads = 0;
            var catalog = new CompiledTranslationCatalog("hot", "en",
                [CompiledTranslationDefinition.FromRmf2Inputs("greeting", [])],
                [CompiledTranslationLocale.CreateDeferred("en", null, () =>
                {
                    reads++;
                    return CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(data));
                })]);
            var provider = new CompiledTranslationProvider(catalog);
            var manager = new TranslationManager(provider, await provider.GetSnapshotAsync("en"));
            TranslationHotReload.SupportedOverride = null;
            ITranslationSnapshot before = manager.Current;
            Assert.Equal("Version one", before.Format(Greeting, []));

            var published = new TaskCompletionSource<TranslationSnapshotPublishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.SnapshotPublished += (_, args) => published.TrySetResult(args);
            data = Data("Version two");
            TranslationHotReload.ClearCache(null);
            TranslationHotReload.UpdateApplication(null);
            TranslationSnapshotPublishedEventArgs args = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(TranslationSnapshotPublishReason.Refresh, args.Reason);
            Assert.Equal("Version two", manager.Current.Format(Greeting, []));
            Assert.Equal("Version one", before.Format(Greeting, []), "An earlier snapshot changed its text.");
            Assert.Equal(2, reads, "The generated data was not read again exactly once.");
            GC.KeepAlive(manager);
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }
    }

    [Test, DisplayName("a metadata update refreshes a translation context's managers with the new generated text")]
    public async Task UpdateRefreshesContext()
    {
        string data = Data("Context one");
        TranslationHotReload.SupportedOverride = true;
        TranslationContext context;
        try
        {
            var catalog = new CompiledTranslationCatalog("hot", "en",
                [CompiledTranslationDefinition.FromRmf2Inputs("greeting", [])],
                [CompiledTranslationLocale.CreateDeferred("en", null, () => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(data)))]);
            var registration = new TranslationCatalogRegistration("hot", "en", "sha256:" + new string('0', 64), "{\"version\":2,\"contracts\":{},\"messages\":{}}",
                (valueFormatter, snapshotFactory, options) => new CompiledTranslationProvider(catalog, valueFormatter, snapshotFactory));
            context = await TranslationContext.CreateBuilder().Add(registration).BuildAsync("en");
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }
        ITranslationManager manager = context.GetManager("hot");
        Assert.Equal("Context one", manager.Current.Format(Greeting, []));

        var published = new TaskCompletionSource<TranslationSnapshotPublishedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        manager.SnapshotPublished += (_, args) => published.TrySetResult(args);
        data = Data("Context two");
        TranslationHotReload.ClearCache(null);
        TranslationHotReload.UpdateApplication(null);
        TranslationSnapshotPublishedEventArgs args = await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TranslationSnapshotPublishReason.Refresh, args.Reason);
        Assert.Equal("Context two", manager.Current.Format(Greeting, []));
        GC.KeepAlive(context);
    }

    [Test, DisplayName("without Hot Reload a deferred locale reads its data once and is not tracked")]
    public async Task WithoutHotReloadNothingChanges()
    {
        TranslationHotReload.SupportedOverride = false;
        try
        {
            int reads = 0;
            var catalog = new CompiledTranslationCatalog("hot", "en",
                [CompiledTranslationDefinition.FromRmf2Inputs("greeting", [])],
                [CompiledTranslationLocale.CreateDeferred("en", null, () =>
                {
                    reads++;
                    return CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(Data("Only")));
                })]);
            var provider = new CompiledTranslationProvider(catalog);
            var manager = new TranslationManager(provider, await provider.GetSnapshotAsync("en"));
            bool published = false;
            manager.SnapshotPublished += (_, _) => published = true;
            TranslationHotReload.ClearCache(null);
            TranslationHotReload.UpdateApplication(null);
            await Task.Delay(100);
            Assert.False(published, "An untracked manager was refreshed.");
            await manager.RefreshAsync();
            Assert.Equal("Only", manager.Current.Format(Greeting, []));
            Assert.Equal(1, reads, "The factory ran again without Hot Reload.");
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }
    }
}
