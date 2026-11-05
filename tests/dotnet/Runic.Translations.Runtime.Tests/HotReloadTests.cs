using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
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

    private static string Data(string text, string locale = "en") =>
        "[[0,{\"contentLocale\":\"" + locale + "\",\"ast\":{\"astVersion\":5,\"profile\":\"rmf2-execution-v2\",\"inputs\":[],\"declarations\":[],\"selectors\":[],\"variants\":[{\"keys\":[],\"nodes\":[{\"kind\":\"text\",\"value\":\"" + text + "\"}]}]}}]]";

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

    [Test, DisplayName("a metadata update reloads every cached locale, not only the current one")]
    public async Task UpdateReloadsOtherCachedLocales()
    {
        TranslationHotReload.SupportedOverride = true;
        try
        {
            string en = Data("English one"), de = Data("Deutsch eins", "de");
            var catalog = new CompiledTranslationCatalog("hot", "en",
                [CompiledTranslationDefinition.FromRmf2Inputs("greeting", [])],
                [
                    CompiledTranslationLocale.CreateDeferred("de", "en", () => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(de))),
                    CompiledTranslationLocale.CreateDeferred("en", null, () => CompiledTranslationLocale.ReadRmf2Values(Encoding.UTF8.GetBytes(en))),
                ]);
            var provider = new CompiledTranslationProvider(catalog);
            var manager = new TranslationManager(provider, await provider.GetSnapshotAsync("de"));
            TranslationHotReload.SupportedOverride = null;
            Assert.Equal("Deutsch eins", manager.Current.Format(Greeting, []));
            await manager.SetLocaleAsync("en");
            Assert.Equal("English one", manager.Current.Format(Greeting, []));

            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.SnapshotPublished += (_, args) => { if (args.Reason == TranslationSnapshotPublishReason.Refresh) published.TrySetResult(); };
            de = Data("Deutsch zwei", "de");
            TranslationHotReload.ClearCache(null);
            TranslationHotReload.UpdateApplication(null);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await manager.SetLocaleAsync("de");
            Assert.Equal("Deutsch zwei", manager.Current.Format(Greeting, []), "The cached de snapshot kept the old text.");
            GC.KeepAlive(manager);
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }
    }

    [Test, DisplayName("snapshots created while Hot Reload resets the data never mix two generations or see reset values")]
    public async Task ConcurrentResetKeepsSnapshotsConsistent()
    {
        // Hot Reload stays supported in the tasks below, which inherit this flow: the factory is kept for the next read.
        TranslationHotReload.SupportedOverride = true;
        int version = 0;
        // The pattern and the message carry the same text, so a snapshot that combines arrays of two generations shows.
        var catalog = new CompiledTranslationCatalog("hot", "en",
            [CompiledTranslationDefinition.FromRmf2Inputs("greeting", [])],
            [CompiledTranslationLocale.CreateDeferred("en", null, () =>
            {
                string text = "Version " + Interlocked.Increment(ref version);
                return [new CompiledTranslationValue(0, text)];
            })]);

        // The loops spin on dedicated threads until a Stopwatch deadline. On thread-pool threads they would take every
        // worker of a small CI runner, and a timer-based stop needs a pool thread to fire, so the stop and the other
        // cases' continuations waited for thread injection for seconds (the manager timing cases then timed out).
        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 4;
        bool Running() => Stopwatch.GetTimestamp() < deadline;
        static Task Spin(Action loop) =>
            Task.Factory.StartNew(loop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Task reset = Spin(() =>
        {
            while (Running()) catalog.ResetDeferredData();
        });
        Task[] readers = new Task[Math.Max(2, Environment.ProcessorCount - 1)];
        int snapshots = 0;
        for (int i = 0; i < readers.Length; i++)
        {
            readers[i] = Spin(() =>
            {
                while (Running())
                {
                    var snapshot = new CompiledTranslationSnapshot(catalog, "en");
                    Assert.True(snapshot.TryGet(Greeting, out string pattern), "A snapshot has no pattern.");
                    Assert.Equal(pattern, snapshot.Format(Greeting, []), "A snapshot combines the pattern and the message of two generations.");
                    Interlocked.Increment(ref snapshots);
                }
            });
        }

        try
        {
            await Task.WhenAll([reset, .. readers]);
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }

        Assert.True(snapshots > 0, "No snapshot was created.");
        Assert.True(version > 1, "The data was never read again.");
    }

    [Test, DisplayName("a markup contract linked from its generated source and a registration follow a Hot Reload update")]
    public void ContractFollowsUpdate()
    {
        const string Empty = "{\"version\":2,\"contracts\":{},\"messages\":{}}";
        const string Badge = "{\"version\":2,\"contracts\":{\"shop:badge\":{\"kind\":\"standalone\",\"placement\":\"inline\",\"children\":\"none\"," +
            "\"interactive\":false,\"plainText\":\"omit\",\"options\":{}}},\"messages\":{}}";
        string json = Empty;
        Rmf2MarkupContract linked, fixedContract;
        TranslationCatalogRegistration registration;
        TranslationHotReload.SupportedOverride = true;
        try
        {
            registration = new TranslationCatalogRegistration("hot", "en", "sha256:" + new string('0', 64), () => json,
                (valueFormatter, snapshotFactory, options) => throw new InvalidOperationException());
            linked = registration.LinkMarkupContract();
            fixedContract = Rmf2MarkupContract.Link(json);
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }

        Assert.False(linked.DeclaresMarkup("shop:badge"), "The startup contract declares the badge.");
        json = Badge;
        TranslationHotReload.ClearCache([typeof(string)]);
        TranslationHotReload.UpdateApplication([typeof(string)]);
        Assert.Equal(Badge, registration.Rmf2MarkupContract);
        Assert.True(linked.DeclaresMarkup("shop:badge"), "The linked contract kept the startup markup.");
        Assert.Equal(1, linked.LinkVersion);
        Assert.False(fixedContract.DeclaresMarkup("shop:badge"), "A contract linked from a string changed.");

        json = "{";
        TranslationHotReload.ClearCache(null);
        TranslationHotReload.UpdateApplication(null);
        Assert.True(linked.DeclaresMarkup("shop:badge"), "A contract that fails to link replaced the previous one.");
        GC.KeepAlive(linked);
    }

    [Test, DisplayName("a metadata update reloads only catalogs whose generated types changed")]
    public async Task UpdateSkipsUnchangedCatalogs()
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
            int publishes = 0;
            var published = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.SnapshotPublished += (_, _) => { Interlocked.Increment(ref publishes); published.TrySetResult(); };
            data = Data("Version two");

            // An update of unrelated code: nothing is read again or republished.
            TranslationHotReload.ClearCache([typeof(string), typeof(Assert)]);
            TranslationHotReload.UpdateApplication([typeof(string), typeof(Assert)]);
            await Task.Delay(100);
            Assert.Equal(0, publishes, "A catalog whose generated types did not change was reloaded.");
            Assert.Equal("Version one", (await provider.GetSnapshotAsync("en")).Format(Greeting, []));
            Assert.Equal(1, reads);

            // The type that creates the data (here the test class, which declares the factory lambda) changed.
            TranslationHotReload.ClearCache([typeof(HotReloadTests)]);
            TranslationHotReload.UpdateApplication([typeof(HotReloadTests)]);
            await published.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Version two", manager.Current.Format(Greeting, []));
            Assert.Equal(2, reads);
            GC.KeepAlive(manager);
        }
        finally
        {
            TranslationHotReload.SupportedOverride = null;
        }
    }
}
