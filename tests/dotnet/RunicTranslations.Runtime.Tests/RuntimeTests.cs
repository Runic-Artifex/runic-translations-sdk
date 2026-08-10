using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RunicTranslations.Runtime.Tests;

internal static class RuntimeTests
{
    public static void Register(TestRunner runner)
    {
        runner.Add("manager exposes initial immutable reference", InitialState);
        runner.Add("manager validates initial snapshot", InitialValidation);
        runner.Add("manager same locale is no-op", SameLocaleNoOp);
        runner.Add("manager swaps atomically and raises exactly once", SuccessfulSwap);
        runner.Add("manager provider failure preserves old snapshot", FailurePreservesCurrent);
        runner.Add("manager rejects null replacement", NullReplacement);
        runner.Add("manager rejects cross-catalog replacement", CrossCatalogReplacement);
        runner.Add("manager rejects blank replacement locale", BlankLocaleReplacement);
        runner.Add("manager rejects noncanonical replacement locale", NoncanonicalLocaleReplacement);
        runner.Add("manager coalesces identical in-flight requests", CoalescesRequests);
        runner.Add("manager isolates one caller cancellation", IsolatesCallerCancellation);
        runner.Add("manager cancels provider after all waiters cancel", CancelsAfterAllWaiters);
        runner.Add("manager serializes different locale loads", SerializesDifferentLocales);
        runner.Add("manager concurrent transitions have exactly-once event chain", ConcurrentTransitions);
        runner.Add("manager canceled transition preserves current", CancellationPreservesCurrent);
        runner.Add("manager resolved active locale is no-op", ResolvedActiveLocaleNoOp);
        runner.Add("manager notification permits synchronous reentrant switch", ReentrantNotification);
        runner.Add("manager isolates throwing notification handlers", ThrowingNotificationHandler);
        runner.Add("manager cancellation wins before commit", CancellationWinsBeforeCommit);
        runner.Add("manager commit wins before caller cancellation", CommitWinsBeforeCancellation);
        runner.Add("manager synchronous wait does not capture caller context", SynchronousWaitDoesNotDeadlock);
        runner.Add("compiled catalog defensively copies inputs", CatalogImmutability);
        runner.Add("compiled catalog validates sorted canonical data", CatalogValidation);
        runner.Add("compiled catalog validates fallback graph", FallbackValidation);
        runner.Add("snapshot resolves fallback values", SnapshotFallback);
        runner.Add("snapshot lookup validates complete O(1) key identity", SnapshotKeyIdentity);
        runner.Add("snapshot formats only exact compiled descriptors", SnapshotFormattingContract);
        runner.Add("snapshot applies throw missing policy", SnapshotMissingThrow);
        runner.Add("snapshot applies return-key missing policy", SnapshotMissingKey);
        runner.Add("snapshot applies marker missing policy", SnapshotMissingMarker);
        runner.Add("snapshot layers validated replacement values", SnapshotReplacement);
        runner.Add("provider resolves parents then default", ProviderParentResolution);
        runner.Add("provider applies exact unsupported policy", ProviderExactResolution);
        runner.Add("provider applies default unsupported policy", ProviderDefaultResolution);
        runner.Add("provider caches and coalesces canonical locale", ProviderCoalescing);
        runner.Add("provider isolates a canceled coalesced caller", ProviderCancellationIsolation);
        runner.Add("provider retries after factory failure", ProviderFailureRetry);
        runner.Add("provider rejects invalid factory snapshot", ProviderFactoryValidation);
        runner.Add("snapshot resolves allowed extras only through dynamic keys", AllowedExtraDynamicLookup);
        runner.Add("compiled public memory cannot mutate snapshot state", PublicMemoryIsolation);
        runner.Add("provider abandons canceled blocked factory and retries independently", ProviderAbandonsCanceledFactory);
        runner.Add("external snapshot factory null source uses compiled fallback", ExternalFactoryNullFallback);
        runner.Add("external snapshot factory subset overlays per-key fallback", ExternalFactorySubsetOverlay);
        runner.Add("external snapshot factory remaps name order to IDs and dynamic extra", ExternalFactoryExtraOrdering);
        runner.Add("external snapshot factory rejects incompatible contracts before source", ExternalFactoryRejectsContractsBeforeSource);
        runner.Add("external snapshot manager publishes verified data and preserves on tamper", ExternalFactoryManagerSafety);
        runner.Add("external snapshot manager cancellation preserves current", ExternalFactoryManagerCancellation);
        runner.Add("compiled catalog WithOptions captures immutable policies", CatalogWithOptions);
    }

    private static void InitialState()
    {
        FakeSnapshot initial = new("app", "en-US");
        TextResourceManager manager = new(new ImmediateProvider(locale => new FakeSnapshot("app", locale)), initial);
        Assert.Same(initial, manager.Current);
        Assert.Equal("en-US", manager.CurrentLocale);
    }

    private static void InitialValidation()
    {
        ImmediateProvider provider = new(locale => new FakeSnapshot("app", locale));
        Assert.Throws<ArgumentNullException>(() => _ = new TextResourceManager(null!, new FakeSnapshot("app", "en-US")));
        Assert.Throws<ArgumentNullException>(() => _ = new TextResourceManager(provider, null!));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceManager(provider, new FakeSnapshot("", "en-US")));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceManager(provider, new FakeSnapshot("app", "")));
        Assert.Throws<ArgumentException>(() => _ = new TextResourceManager(provider, new FakeSnapshot("app", "EN-us")));
    }

    private static async Task SameLocaleNoOp()
    {
        ImmediateProvider provider = new(locale => new FakeSnapshot("app", locale));
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        await manager.SetLocaleAsync("EN-us");
        Assert.Equal(0, provider.CallCount);
    }

    private static async Task SuccessfulSwap()
    {
        FakeSnapshot initial = new("app", "en-US");
        FakeSnapshot replacement = new("app", "de-DE");
        TextResourceManager manager = new(new ImmediateProvider(_ => replacement), initial);
        int events = 0;
        manager.LocaleChanged += (sender, args) =>
        {
            events++;
            Assert.Same(manager, sender!);
            Assert.Same(initial, args.OldSnapshot);
            Assert.Same(replacement, args.NewSnapshot);
            Assert.Same(replacement, manager.Current, "Swap must happen before notification.");
        };
        await manager.SetLocaleAsync("de-DE");
        Assert.Same(replacement, manager.Current);
        Assert.Equal(1, events);
    }

    private static async Task FailurePreservesCurrent()
    {
        FakeSnapshot initial = new("app", "en-US");
        TextResourceManager manager = new(new ThrowingProvider(new InvalidOperationException("load failed")), initial);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetLocaleAsync("de-DE").AsTask(), "load failed");
        Assert.Same(initial, manager.Current);
    }

    private static async Task NullReplacement()
    {
        FakeSnapshot initial = new("app", "en-US");
        TextResourceManager manager = new(new NullProvider(), initial);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetLocaleAsync("de-DE").AsTask(), "null snapshot");
        Assert.Same(initial, manager.Current);
    }

    private static Task CrossCatalogReplacement() => InvalidReplacement(new FakeSnapshot("other", "de-DE"), "different catalog");

    private static Task BlankLocaleReplacement() => InvalidReplacement(new FakeSnapshot("app", ""), "canonical locale");

    private static Task NoncanonicalLocaleReplacement() => InvalidReplacement(new FakeSnapshot("app", "EN-us"), "canonical locale");

    private static async Task InvalidReplacement(FakeSnapshot replacement, string message)
    {
        FakeSnapshot initial = new("app", "en-US");
        TextResourceManager manager = new(new ImmediateProvider(_ => replacement), initial);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SetLocaleAsync("de-DE").AsTask(), message);
        Assert.Same(initial, manager.Current);
    }

    private static async Task CoalescesRequests()
    {
        BlockingProvider provider = new();
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        Task first = manager.SetLocaleAsync("de-DE").AsTask();
        Task second = manager.SetLocaleAsync("DE-de").AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.CallCount);
        provider.Complete(new FakeSnapshot("app", "de-DE"));
        await Task.WhenAll(first, second);
        Assert.Equal(1, provider.CallCount);
    }

    private static async Task IsolatesCallerCancellation()
    {
        BlockingProvider provider = new();
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        using CancellationTokenSource canceled = new();
        Task first = manager.SetLocaleAsync("de-DE", canceled.Token).AsTask();
        Task second = manager.SetLocaleAsync("de-DE").AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        Assert.False(provider.ProviderToken.IsCancellationRequested, "Shared provider load was canceled with an interested waiter.");
        provider.Complete(new FakeSnapshot("app", "de-DE"));
        await second;
        Assert.Equal("de-DE", manager.CurrentLocale);
    }

    private static async Task CancelsAfterAllWaiters()
    {
        BlockingProvider provider = new();
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        using CancellationTokenSource a = new();
        using CancellationTokenSource b = new();
        Task first = manager.SetLocaleAsync("de-DE", a.Token).AsTask();
        Task second = manager.SetLocaleAsync("de-DE", b.Token).AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        a.Cancel();
        b.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAsync<OperationCanceledException>(() => second);
        await provider.ProviderCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("en-US", manager.CurrentLocale);
    }

    private static async Task SerializesDifferentLocales()
    {
        SerialProbeProvider provider = new();
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        Task[] changes = [
            manager.SetLocaleAsync("de-DE").AsTask(),
            manager.SetLocaleAsync("fr-FR").AsTask(),
            manager.SetLocaleAsync("it-IT").AsTask(),
        ];
        await Task.WhenAll(changes).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, provider.MaximumConcurrentCalls);
        Assert.Equal(3, provider.CallCount);
    }

    private static async Task ConcurrentTransitions()
    {
        ImmediateProvider provider = new(locale => new FakeSnapshot("app", locale));
        TextResourceManager manager = new(provider, new FakeSnapshot("app", "en-US"));
        List<TextResourceLocaleChangedEventArgs> events = new();
        object gate = new();
        manager.LocaleChanged += (_, args) => { lock (gate) events.Add(args); };
        string[] locales = Enumerable.Range(0, 20).Select(i => "aa-A" + (char)('A' + i)).ToArray();
        await Task.WhenAll(locales.Select(locale => manager.SetLocaleAsync(locale).AsTask())).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(locales.Length, provider.CallCount);
        Assert.Equal(locales.Length, events.Count);
        for (int index = 1; index < events.Count; index++)
            Assert.Same(events[index - 1].NewSnapshot, events[index].OldSnapshot, "Events do not describe one atomic chain.");
        Assert.Same(events[^1].NewSnapshot, manager.Current);
    }

    private static async Task CancellationPreservesCurrent()
    {
        FakeSnapshot initial = new("app", "en-US");
        BlockingProvider provider = new();
        TextResourceManager manager = new(provider, initial);
        using CancellationTokenSource source = new();
        Task change = manager.SetLocaleAsync("de-DE", source.Token).AsTask();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => change);
        Assert.Same(initial, manager.Current);
    }

    private static async Task ResolvedActiveLocaleNoOp()
    {
        FakeSnapshot initial = new("app", "en-US");
        TextResourceManager manager = new(new ImmediateProvider(_ => initial), initial);
        int events = 0;
        manager.LocaleChanged += (_, _) => events++;
        await manager.SetLocaleAsync("unsupported");
        Assert.Same(initial, manager.Current);
        Assert.Equal(0, events);
    }

    private static async Task ReentrantNotification()
    {
        TextResourceManager manager = new(
            new ImmediateProvider(locale => new FakeSnapshot("app", locale)),
            new FakeSnapshot("app", "en-US"));
        int events = 0;
        manager.LocaleChanged += (_, args) =>
        {
            events++;
            if (args.NewLocale == "de-DE")
                manager.SetLocaleAsync("fr-FR").AsTask().GetAwaiter().GetResult();
        };

        await manager.SetLocaleAsync("de-DE");
        Assert.Equal("fr-FR", manager.CurrentLocale);
        Assert.Equal(2, events);
    }

    private static async Task ThrowingNotificationHandler()
    {
        TextResourceManager manager = new(
            new ImmediateProvider(locale => new FakeSnapshot("app", locale)),
            new FakeSnapshot("app", "en-US"));
        int laterHandlers = 0;
        manager.LocaleChanged += (_, _) => throw new InvalidOperationException("subscriber failed");
        manager.LocaleChanged += (_, _) => laterHandlers++;

        await manager.SetLocaleAsync("de-DE");
        Assert.Equal("de-DE", manager.CurrentLocale);
        Assert.Equal(1, laterHandlers);
    }

    private static async Task CancellationWinsBeforeCommit()
    {
        FakeSnapshot initial = new("app", "en-US");
        CommitBarrierSnapshot replacement = new("app", "de-DE");
        TextResourceManager manager = new(new YieldingProvider(replacement), initial);
        using CancellationTokenSource cancellation = new();
        Task transition = manager.SetLocaleAsync("de-DE", cancellation.Token).AsTask();

        await replacement.ValidationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        replacement.AllowValidation.TrySetResult(true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => transition);
        Assert.Same(initial, manager.Current);
    }

    private static async Task CommitWinsBeforeCancellation()
    {
        using CancellationTokenSource cancellation = new();
        TextResourceManager manager = new(
            new ImmediateProvider(locale => new FakeSnapshot("app", locale)),
            new FakeSnapshot("app", "en-US"));
        manager.LocaleChanged += (_, _) => cancellation.Cancel();

        await manager.SetLocaleAsync("de-DE", cancellation.Token);
        Assert.Equal("de-DE", manager.CurrentLocale);
    }

    private static void SynchronousWaitDoesNotDeadlock()
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            try
            {
                TextResourceManager manager = new(
                    new OffContextProvider(),
                    new FakeSnapshot("app", "en-US"));
                manager.SetLocaleAsync("de-DE").AsTask().GetAwaiter().GetResult();
                Assert.Equal("de-DE", manager.CurrentLocale);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(null);
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(5)),
            "SetLocaleAsync deadlocked after capturing a non-pumping synchronization context.");
        if (failure is not null)
            throw new InvalidOperationException("The synchronous locale switch failed.", failure);
    }

    private static void CatalogImmutability()
    {
        TextResourcePlaceholderDescriptor[] descriptors =
            [new("count", TextArgumentType.Int, TextArgumentFormat.Grouped)];
        CompiledTextResourceDefinition count = new("beta.count", descriptors);
        descriptors[0] = new("other", TextArgumentType.String, TextArgumentFormat.None);
        Assert.Equal("count", count.Placeholders.Span[0].Name);

        CompiledTextResourceDefinition[] definitions = [new("alpha.greeting", []), count];
        CompiledTextResourceValue[] values = [new(0, "Hello"), new(1, "Count {count}")];
        CompiledTextResourceLocale en = new("en", null, values);
        values[0] = new(0, "MUTATED");
        CompiledTextResourceLocale[] locales = [en];
        CompiledTextResourceCatalog catalog = new("app", "en", definitions, locales);
        definitions[0] = new("changed.key", []);
        locales[0] = new("fr", null, [new(0, "x"), new(1, "{count}")]);
        CompiledTextResourceSnapshot snapshot = new(catalog, "en");
        Assert.Equal("Hello", snapshot.Get(new TextResourceKey("app", 0, "alpha.greeting")));
        Assert.Equal("alpha.greeting", catalog.Definitions.Span[0].Name);
        Assert.Equal("en", catalog.Locales.Span[0].Locale);
    }

    private static void CatalogValidation()
    {
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceDefinition("bad-name", []));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceDefinition("alpha.greeting",
            [new("z", TextArgumentType.String, TextArgumentFormat.None), new("a", TextArgumentType.String, TextArgumentFormat.None)]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceDefinition("alpha.greeting",
            [new("x", TextArgumentType.Int, TextArgumentFormat.Fixed1)]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceLocale("EN-us", null, []));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceLocale("en", null, [new(1, "b"), new(0, "a")]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("App", "en", [new("alpha.greeting", [])],
            [new CompiledTextResourceLocale("en", null, [new(0, "Hello")])]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en",
            [new CompiledTextResourceDefinition("beta.count", []), new CompiledTextResourceDefinition("alpha.greeting", [])],
            [new CompiledTextResourceLocale("en", null, [new(0, "a"), new(1, "b")])]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", [new("alpha.greeting", [])],
            [new CompiledTextResourceLocale("en", null, [new(1, "unknown")])]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", [new("alpha.greeting", [])],
            [new CompiledTextResourceLocale("en", null, [])]), "does not define");
    }

    private static void FallbackValidation()
    {
        CompiledTextResourceDefinition[] definitions = [new("alpha.greeting", [])];
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", definitions,
            [new CompiledTextResourceLocale("de", null, []), new CompiledTextResourceLocale("en", null, [new(0, "Hello")])]), "must declare");
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", definitions,
            [new CompiledTextResourceLocale("de", "fr", []), new CompiledTextResourceLocale("en", null, [new(0, "Hello")])]), "declared locale");
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", definitions,
            [new CompiledTextResourceLocale("de", "fr", []), new CompiledTextResourceLocale("en", null, [new(0, "Hello")]), new CompiledTextResourceLocale("fr", "de", [])]), "cycle");
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", definitions,
            [new CompiledTextResourceLocale("en", "de", [new(0, "Hello")])]), "default locale cannot");
    }

    private static void SnapshotFallback()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();
        CompiledTextResourceSnapshot de = new(catalog, "de-DE");
        Assert.Equal("Hallo", de.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Count {count}", de.Get(Key(1, "beta.count")));
        CompiledTextResourceSnapshot us = new(catalog, "en-US");
        Assert.Equal("Hello", us.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Total {count}", us.Get(Key(1, "beta.count")));
    }

    private static void SnapshotKeyIdentity()
    {
        CompiledTextResourceSnapshot snapshot = new(CreateCatalog(), "en");
        Assert.True(snapshot.TryGet(Key(0, "alpha.greeting"), out string pattern), "Known key missing.");
        Assert.Equal("Hello", pattern);
        Assert.False(snapshot.TryGet(new TextResourceKey("other", 0, "alpha.greeting"), out _), "Cross-catalog key accepted.");
        Assert.False(snapshot.TryGet(Key(1, "alpha.greeting"), out _), "Mismatched ID/name accepted.");
        Assert.False(snapshot.TryGet(Key(999, "alpha.greeting"), out _), "Out-of-range ID accepted.");
        Assert.False(snapshot.TryGet(Key(-1, "alpha.greeting"), out _), "Negative ID accepted.");
    }

    private static void SnapshotFormattingContract()
    {
        CompiledTextResourceSnapshot snapshot = new(CreateCatalog(), "en-US");
        string expected = "Total " + 1234L.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(expected, snapshot.Format(Key(1, "beta.count"),
            [new TextArgument("count", 1234L, TextArgumentFormat.Grouped)]));
        Assert.Throws<TextResourceFormatException>(() => snapshot.Format(Key(1, "beta.count"), []), "requires");
        Assert.Throws<TextResourceFormatException>(() => snapshot.Format(Key(1, "beta.count"), [new TextArgument("other", 1L)]), "does not declare");
        Assert.Throws<TextResourceFormatException>(() => snapshot.Format(Key(1, "beta.count"), [new TextArgument("count", 1L)]), "does not match");
    }

    private static void SnapshotMissingThrow() => Assert.Throws<TextResourceNotFoundException>(
        () => new CompiledTextResourceSnapshot(CreateCatalog(missingKey: MissingTextResourcePolicy.Throw), "en").Get(Key(9, "missing.key")), "missing.key");

    private static void SnapshotMissingKey() => Assert.Equal("missing.key",
        new CompiledTextResourceSnapshot(CreateCatalog(missingKey: MissingTextResourcePolicy.ReturnKey), "en").Get(Key(9, "missing.key")));

    private static void SnapshotMissingMarker() => Assert.Equal("⟦missing.key⟧",
        new CompiledTextResourceSnapshot(CreateCatalog(missingKey: MissingTextResourcePolicy.ReturnMarker), "en").Format(Key(9, "missing.key"), []));

    private static void SnapshotReplacement()
    {
        CompiledTextResourceValue[] replacements = [new(0, "Howdy")];
        CompiledTextResourceSnapshot snapshot = new(CreateCatalog(), "en-US", replacements);
        replacements[0] = new(0, "MUTATED");
        Assert.Equal("Howdy", snapshot.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Total {count}", snapshot.Get(Key(1, "beta.count")));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceSnapshot(CreateCatalog(), "en", [new(2, "bad")]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceSnapshot(CreateCatalog(), "en", [new(1, "wrong {name}")]));
    }

    private static async Task ProviderParentResolution()
    {
        CompiledTextResourceProvider provider = new(CreateCatalog());
        ITextResourceSnapshot snapshot = await provider.GetSnapshotAsync("en-AU");
        Assert.Equal("en", snapshot.Locale);
        Assert.Same(snapshot, await provider.GetSnapshotAsync("EN-au"));
    }

    private static async Task ProviderExactResolution()
    {
        CompiledTextResourceProvider provider = new(CreateCatalog(UnsupportedLocalePolicy.Exact));
        await Assert.ThrowsAsync<TextResourceNotFoundException>(() => provider.GetSnapshotAsync("en-AU").AsTask(), "not declared");
    }

    private static async Task ProviderDefaultResolution()
    {
        CompiledTextResourceProvider provider = new(CreateCatalog(UnsupportedLocalePolicy.Default));
        Assert.Equal("en", (await provider.GetSnapshotAsync("zh-Hant-TW")).Locale);
    }

    private static async Task ProviderCoalescing()
    {
        SnapshotFactoryProbe factory = new(block: true);
        CompiledTextResourceProvider provider = new(CreateCatalog(), snapshotFactory: factory);
        Task<ITextResourceSnapshot> a = provider.GetSnapshotAsync("en-AU").AsTask();
        Task<ITextResourceSnapshot> b = provider.GetSnapshotAsync("en").AsTask();
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, factory.CallCount);
        factory.Release();
        ITextResourceSnapshot[] snapshots = await Task.WhenAll(a, b);
        Assert.Same(snapshots[0], snapshots[1]);
        Assert.Same(snapshots[0], await provider.GetSnapshotAsync("en"));
        Assert.Equal(1, factory.CallCount);
    }

    private static async Task ProviderCancellationIsolation()
    {
        SnapshotFactoryProbe factory = new(block: true);
        CompiledTextResourceProvider provider = new(CreateCatalog(), snapshotFactory: factory);
        using CancellationTokenSource canceled = new();
        Task<ITextResourceSnapshot> first = provider.GetSnapshotAsync("en", canceled.Token).AsTask();
        Task<ITextResourceSnapshot> second = provider.GetSnapshotAsync("en").AsTask();
        await factory.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);
        factory.Release();
        ITextResourceSnapshot snapshot = await second;
        Assert.Equal("en", snapshot.Locale);
        Assert.Equal(1, factory.CallCount);
    }

    private static async Task ProviderFailureRetry()
    {
        SnapshotFactoryProbe factory = new(failFirst: true);
        CompiledTextResourceProvider provider = new(CreateCatalog(), snapshotFactory: factory);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotAsync("en").AsTask(), "factory failed");
        Assert.Equal("en", (await provider.GetSnapshotAsync("en")).Locale);
        Assert.Equal(2, factory.CallCount);
    }

    private static async Task ProviderFactoryValidation()
    {
        CompiledTextResourceProvider provider = new(CreateCatalog(), snapshotFactory: new InvalidSnapshotFactory());
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotAsync("en").AsTask(), "different catalog or locale");
    }

    private static void AllowedExtraDynamicLookup()
    {
        CompiledTextResourceDefinition canonical = new("alpha.greeting", []);
        CompiledTextResourceDefinition extra = new("gamma.extra", [], isCanonical: false);
        CompiledTextResourceCatalog catalog = new(
            "app", "en", [canonical, extra],
            [
                new CompiledTextResourceLocale("de", "en", [new(1, "Nur Deutsch")]),
                new CompiledTextResourceLocale("de-DE", "de", []),
                new CompiledTextResourceLocale("en", null, [new(0, "Hello")]),
            ], missingKey: MissingTextResourcePolicy.ReturnKey);

        TextResourceKey dynamicExtra = new("app", CompiledTextResourceCatalog.DynamicKeyId, "gamma.extra");
        Assert.Equal("Nur Deutsch", new CompiledTextResourceSnapshot(catalog, "de").Get(dynamicExtra));
        Assert.Equal("Nur Deutsch", new CompiledTextResourceSnapshot(catalog, "de-DE").Get(dynamicExtra));
        Assert.Equal("gamma.extra", new CompiledTextResourceSnapshot(catalog, "en").Get(dynamicExtra));
        Assert.False(new CompiledTextResourceSnapshot(catalog, "de").TryGet(new TextResourceKey("app", 0, "gamma.extra"), out _),
            "A nonnegative canonical ID/name mismatch was accepted.");

        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", [canonical, extra],
            [new CompiledTextResourceLocale("en", null, [new(0, "Hello"), new(1, "Extra")])]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", [canonical, extra],
            [new CompiledTextResourceLocale("en", null, [new(0, "Hello")])]));
        Assert.Throws<ArgumentException>(() => _ = new CompiledTextResourceCatalog("app", "en", [extra, canonical],
            [new CompiledTextResourceLocale("en", null, [new(1, "Hello")])]));
    }

    private static void PublicMemoryIsolation()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();

        ReadOnlyMemory<CompiledTextResourceDefinition> definitions = catalog.Definitions;
        Assert.True(MemoryMarshal.TryGetArray(definitions, out ArraySegment<CompiledTextResourceDefinition> definitionArray),
            "Definitions memory did not expose an array for adversarial mutation.");
        definitionArray.Array![definitionArray.Offset] = new CompiledTextResourceDefinition("changed.key", []);

        ReadOnlyMemory<CompiledTextResourceLocale> locales = catalog.Locales;
        Assert.True(MemoryMarshal.TryGetArray(locales, out ArraySegment<CompiledTextResourceLocale> localeArray),
            "Locales memory did not expose an array for adversarial mutation.");
        localeArray.Array![localeArray.Offset] = new CompiledTextResourceLocale("fr", "en", []);

        CompiledTextResourceDefinition count = catalog.Definitions.Span[1];
        ReadOnlyMemory<TextResourcePlaceholderDescriptor> placeholders = count.Placeholders;
        Assert.True(MemoryMarshal.TryGetArray(placeholders, out ArraySegment<TextResourcePlaceholderDescriptor> placeholderArray),
            "Placeholder memory did not expose an array for adversarial mutation.");
        placeholderArray.Array![placeholderArray.Offset] = new TextResourcePlaceholderDescriptor(
            "other", TextArgumentType.String, TextArgumentFormat.None);

        CompiledTextResourceLocale en = catalog.Locales.Span[1];
        ReadOnlyMemory<CompiledTextResourceValue> values = en.Values;
        Assert.True(MemoryMarshal.TryGetArray(values, out ArraySegment<CompiledTextResourceValue> valueArray),
            "Values memory did not expose an array for adversarial mutation.");
        valueArray.Array![valueArray.Offset] = new CompiledTextResourceValue(0, "MUTATED");

        CompiledTextResourceSnapshot snapshot = new(catalog, "en");
        Assert.Equal("alpha.greeting", catalog.Definitions.Span[0].Name);
        Assert.Equal("de-DE", catalog.Locales.Span[0].Locale);
        Assert.Equal("count", catalog.Definitions.Span[1].Placeholders.Span[0].Name);
        Assert.Equal("Hello", snapshot.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Count {count}", snapshot.Get(Key(1, "beta.count")));
    }

    private static async Task ProviderAbandonsCanceledFactory()
    {
        AbandoningFactory factory = new();
        CompiledTextResourceProvider provider = new(CreateCatalog(), snapshotFactory: factory);
        using CancellationTokenSource cancellation = new();
        Task<ITextResourceSnapshot> abandoned = provider.GetSnapshotAsync("en", cancellation.Token).AsTask();
        await factory.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => abandoned);
        await factory.FirstTokenCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        ITextResourceSnapshot replacement = await provider.GetSnapshotAsync("en").AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("en", replacement.Locale);
        Assert.Equal(2, factory.CallCount);
        factory.ReleaseFirst();
    }

    private static async Task ExternalFactoryNullFallback()
    {
        CountingSource source = new(null);
        CompiledTextResourceCatalog catalog = CreateCatalog();
        ExternalTextResourceSnapshotFactory factory = CreateExternalFactory(source, locale => CreatePackContract(catalog, locale));
        ITextResourceSnapshot snapshot = await factory.CreateSnapshotAsync(catalog, "en-US", DefaultTextValueFormatter.Shared, default);
        Assert.Equal("Hello", snapshot.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Total {count}", snapshot.Get(Key(1, "beta.count")));
        Assert.Equal(1, source.Calls);
    }

    private static async Task ExternalFactorySubsetOverlay()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();
        string json = ExternalPackJson("en-US", "\"alpha.greeting\":{\"pattern\":\"External hello\",\"arguments\":[]}");
        CountingSource source = new(new ExternalTextResourcePack(Encoding.UTF8.GetBytes(json)));
        CompiledTextResourceProvider provider = new(catalog, snapshotFactory: CreateExternalFactory(source, locale => CreatePackContract(catalog, locale)));
        ITextResourceSnapshot snapshot = await provider.GetSnapshotAsync("en-US");
        Assert.Equal("External hello", snapshot.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Total {count}", snapshot.Get(Key(1, "beta.count")));
    }

    private static async Task ExternalFactoryExtraOrdering()
    {
        CompiledTextResourceCatalog catalog = new(
            "app", "en",
            [
                new CompiledTextResourceDefinition("Zulu.Key", []),
                new CompiledTextResourceDefinition("Alpha.Extra", [], isCanonical: false),
            ],
            [
                new CompiledTextResourceLocale("de", "en", [new(1, "compiled extra")]),
                new CompiledTextResourceLocale("en", null, [new(0, "compiled canonical")]),
            ]);
        TextResourcePackContract Contract(string locale) => new("app", locale, ExternalFingerprint,
            [
                new TextResourcePackMessageContract(new TextResourceKey("app", 1, "Alpha.Extra")),
                new TextResourcePackMessageContract(new TextResourceKey("app", 0, "Zulu.Key")),
            ]);
        string messages =
            "\"Zulu.Key\":{\"pattern\":\"external canonical\",\"arguments\":[]}," +
            "\"Alpha.Extra\":{\"pattern\":\"external extra\",\"arguments\":[]}";
        CountingSource source = new(new ExternalTextResourcePack(Encoding.UTF8.GetBytes(ExternalPackJson("de", messages))));
        ITextResourceSnapshot snapshot = await new CompiledTextResourceProvider(
            catalog, snapshotFactory: CreateExternalFactory(source, Contract)).GetSnapshotAsync("de");
        Assert.Equal("external canonical", snapshot.Get(new TextResourceKey("app", 0, "Zulu.Key")));
        Assert.Equal("external extra", snapshot.Get(new TextResourceKey("app", CompiledTextResourceCatalog.DynamicKeyId, "Alpha.Extra")));
    }

    private static async Task ExternalFactoryRejectsContractsBeforeSource()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();
        TextResourcePackMessageContract Alpha(string catalogName = "app", int id = 0, string name = "alpha.greeting") =>
            new(new TextResourceKey(catalogName, id, name));
        TextResourcePackMessageContract Beta(TextArgumentFormat format = TextArgumentFormat.Grouped) =>
            new(new TextResourceKey("app", 1, "beta.count"),
                [new TextResourcePackArgumentContract("count", TextArgumentType.Int, format)]);
        Func<string, TextResourcePackContract>[] invalid =
        [
            locale => new TextResourcePackContract("other", locale, ExternalFingerprint, [Alpha("other")]),
            locale => new TextResourcePackContract("app", "de-DE", ExternalFingerprint, [Alpha(), Beta()]),
            locale => new TextResourcePackContract("app", locale, "sha256:1111111111111111111111111111111111111111111111111111111111111111", [Alpha(), Beta()]),
            locale => new TextResourcePackContract("app", locale, ExternalFingerprint, [Alpha(id: 99)]),
            locale => new TextResourcePackContract("app", locale, ExternalFingerprint, [Alpha(name: "alpha.wrong"), Beta()]),
            locale => new TextResourcePackContract("app", locale, ExternalFingerprint, [Alpha(), Beta(TextArgumentFormat.Plain)]),
            locale => new TextResourcePackContract("app", locale, ExternalFingerprint, [Alpha()]),
        ];

        foreach (Func<string, TextResourcePackContract> contractFactory in invalid)
        {
            CountingSource source = new(null);
            ExternalTextResourceSnapshotFactory factory = CreateExternalFactory(source, contractFactory);
            await Assert.ThrowsAsync<TextResourcePackException>(() => factory.CreateSnapshotAsync(
                catalog, "en-US", DefaultTextValueFormatter.Shared, default).AsTask());
            Assert.Equal(0, source.Calls, "Invalid generated contract reached the external source.");
        }

        CompiledTextResourceCatalog extras = new(
            "app", "en", [new CompiledTextResourceDefinition("Alpha", []), new CompiledTextResourceDefinition("Extra", [], false)],
            [new CompiledTextResourceLocale("de", "en", [new(1, "extra")]), new CompiledTextResourceLocale("en", null, [new(0, "alpha")])]);
        CountingSource extraSource = new(null);
        TextResourcePackContract extraContract = new("app", "en", ExternalFingerprint,
            [new TextResourcePackMessageContract(new TextResourceKey("app", 0, "Alpha")),
             new TextResourcePackMessageContract(new TextResourceKey("app", 1, "Extra"))]);
        await Assert.ThrowsAsync<TextResourcePackException>(() => CreateExternalFactory(extraSource, _ => extraContract)
            .CreateSnapshotAsync(extras, "en", DefaultTextValueFormatter.Shared, default).AsTask());
        Assert.Equal(0, extraSource.Calls);
    }

    private static async Task ExternalFactoryManagerSafety()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();
        MutableSource source = new();
        ExternalTextResourceSnapshotFactory factory = CreateExternalFactory(source, locale => CreatePackContract(catalog, locale));
        CompiledTextResourceProvider provider = new(catalog, snapshotFactory: factory);
        ITextResourceSnapshot initial = await new CompiledTextResourceProvider(catalog).GetSnapshotAsync("en");
        TextResourceManager manager = new(provider, initial);

        source.Pack = new ExternalTextResourcePack(Encoding.UTF8.GetBytes(
            ExternalPackJson("de-DE", "\"alpha.greeting\":{\"pattern\":\"tampered\",\"arguments\":[]}")
                .Replace(ExternalFingerprint, "sha256:1111111111111111111111111111111111111111111111111111111111111111", StringComparison.Ordinal)));
        await Assert.ThrowsAsync<TextResourcePackException>(() => manager.SetLocaleAsync("de-DE").AsTask());
        Assert.Same(initial, manager.Current);

        source.Pack = new ExternalTextResourcePack(Encoding.UTF8.GetBytes(
            ExternalPackJson("de-DE", "\"alpha.greeting\":{\"pattern\":\"verified\",\"arguments\":[]}")));
        await manager.SetLocaleAsync("de-DE");
        Assert.Equal("de-DE", manager.CurrentLocale);
        Assert.Equal("verified", manager.Current.Get(Key(0, "alpha.greeting")));
        Assert.Equal("Count {count}", manager.Current.Get(Key(1, "beta.count")));
    }

    private static async Task ExternalFactoryManagerCancellation()
    {
        CompiledTextResourceCatalog catalog = CreateCatalog();
        CancelingSource source = new();
        ExternalTextResourceSnapshotFactory factory = CreateExternalFactory(source, locale => CreatePackContract(catalog, locale));
        CompiledTextResourceProvider provider = new(catalog, snapshotFactory: factory);
        ITextResourceSnapshot initial = await new CompiledTextResourceProvider(catalog).GetSnapshotAsync("en");
        TextResourceManager manager = new(provider, initial);
        using CancellationTokenSource cancellation = new();
        Task change = manager.SetLocaleAsync("de-DE", cancellation.Token).AsTask();
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => change);
        Assert.Same(initial, manager.Current);
    }

    private static async Task CatalogWithOptions()
    {
        CompiledTextResourceCatalog original = CreateCatalog();
        Assert.Same(original, original.WithOptions(null));
        Assert.Same(original, original.WithOptions(new TextResourceOptions()));

        TextResourceOptions options = new()
        {
            UnsupportedLocale = UnsupportedLocalePolicy.Exact,
            MissingKey = MissingTextResourcePolicy.ReturnMarker,
        };
        CompiledTextResourceCatalog changed = original.WithOptions(options);
        options.UnsupportedLocale = UnsupportedLocalePolicy.Default;
        options.MissingKey = MissingTextResourcePolicy.ReturnKey;
        Assert.Equal(UnsupportedLocalePolicy.Exact, changed.UnsupportedLocale);
        Assert.Equal(MissingTextResourcePolicy.ReturnMarker, changed.MissingKey);
        Assert.Equal(UnsupportedLocalePolicy.ParentsThenDefault, original.UnsupportedLocale);
        Assert.Equal(MissingTextResourcePolicy.Throw, original.MissingKey);
        await Assert.ThrowsAsync<TextResourceNotFoundException>(() => new CompiledTextResourceProvider(changed)
            .GetSnapshotAsync("zh-Hant").AsTask());
        ITextResourceSnapshot snapshot = await new CompiledTextResourceProvider(changed).GetSnapshotAsync("en");
        Assert.Equal("⟦missing.key⟧", snapshot.Get(Key(99, "missing.key")));

        options.UnsupportedLocale = (UnsupportedLocalePolicy)999;
        Assert.Throws<ArgumentException>(() => original.WithOptions(options));
        options.UnsupportedLocale = UnsupportedLocalePolicy.Exact;
        options.MissingKey = (MissingTextResourcePolicy)999;
        Assert.Throws<ArgumentException>(() => original.WithOptions(options));
    }

    private const string ExternalFingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    private static ExternalTextResourceSnapshotFactory CreateExternalFactory(
        IExternalTextResourceSource source, Func<string, TextResourcePackContract> contractFactory) =>
        new(source, "app", ExternalFingerprint, contractFactory);

    private static TextResourcePackContract CreatePackContract(CompiledTextResourceCatalog catalog, string locale) => new(
        "app", locale, ExternalFingerprint,
        [
            new TextResourcePackMessageContract(new TextResourceKey("app", 0, "alpha.greeting")),
            new TextResourcePackMessageContract(new TextResourceKey("app", 1, "beta.count"),
                [new TextResourcePackArgumentContract("count", TextArgumentType.Int, TextArgumentFormat.Grouped)]),
        ]);

    private static string ExternalPackJson(string locale, string messages) =>
        "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"" + locale +
        "\",\"contractFingerprint\":\"" + ExternalFingerprint + "\",\"messages\":{" + messages + "}}";

    private static TextResourceKey Key(int id, string name) => new("app", id, name);

    private static CompiledTextResourceCatalog CreateCatalog(
        UnsupportedLocalePolicy unsupported = UnsupportedLocalePolicy.ParentsThenDefault,
        MissingTextResourcePolicy missingKey = MissingTextResourcePolicy.Throw) => new(
            "app", "en",
            [
                new CompiledTextResourceDefinition("alpha.greeting", []),
                new CompiledTextResourceDefinition("beta.count",
                    [new TextResourcePlaceholderDescriptor("count", TextArgumentType.Int, TextArgumentFormat.Grouped)]),
            ],
            [
                new CompiledTextResourceLocale("de-DE", "en", [new(0, "Hallo")]),
                new CompiledTextResourceLocale("en", null, [new(0, "Hello"), new(1, "Count {count}")]),
                new CompiledTextResourceLocale("en-US", "en", [new(1, "Total {count}")]),
            ], unsupported, missingKey);

    private sealed class FakeSnapshot : ITextResourceSnapshot
    {
        internal FakeSnapshot(string catalog, string locale) { Catalog = catalog; Locale = locale; }
        public string Catalog { get; }
        public string Locale { get; }
        public bool TryGet(TextResourceKey key, out string pattern) { pattern = string.Empty; return false; }
        public string Get(TextResourceKey key) => throw new TextResourceNotFoundException(key.Name);
        public string Format(TextResourceKey key, ReadOnlySpan<TextArgument> arguments) => Get(key);
    }

    private sealed class ImmediateProvider : ITextResourceProvider
    {
        private readonly Func<string, ITextResourceSnapshot> _factory;
        internal ImmediateProvider(Func<string, ITextResourceSnapshot> factory) => _factory = factory;
        internal int CallCount;
        public ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref CallCount);
            return ValueTask.FromResult(_factory(requestedLocale));
        }
    }

    private sealed class YieldingProvider : ITextResourceProvider
    {
        private readonly ITextResourceSnapshot _snapshot;
        internal YieldingProvider(ITextResourceSnapshot snapshot) => _snapshot = snapshot;

        public async ValueTask<ITextResourceSnapshot> GetSnapshotAsync(
            string requestedLocale,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return _snapshot;
        }
    }

    private sealed class OffContextProvider : ITextResourceProvider
    {
        public async ValueTask<ITextResourceSnapshot> GetSnapshotAsync(
            string requestedLocale,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
            return new FakeSnapshot("app", requestedLocale);
        }
    }

    private sealed class NonPumpingSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            // Intentionally do not dispatch: captured continuations would deadlock
            // the thread that synchronously waits in the regression test.
        }
    }

    private sealed class CommitBarrierSnapshot : ITextResourceSnapshot
    {
        private readonly string _catalog;

        internal CommitBarrierSnapshot(string catalog, string locale)
        {
            _catalog = catalog;
            Locale = locale;
        }

        internal TaskCompletionSource<bool> ValidationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> AllowValidation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Catalog
        {
            get
            {
                ValidationStarted.TrySetResult(true);
                AllowValidation.Task.GetAwaiter().GetResult();
                return _catalog;
            }
        }

        public string Locale { get; }
        public bool TryGet(TextResourceKey key, out string pattern) { pattern = string.Empty; return false; }
        public string Get(TextResourceKey key) => throw new TextResourceNotFoundException(key.Name);
        public string Format(TextResourceKey key, ReadOnlySpan<TextArgument> arguments) => Get(key);
    }

    private sealed class ThrowingProvider : ITextResourceProvider
    {
        private readonly Exception _exception;
        internal ThrowingProvider(Exception exception) => _exception = exception;
        public ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<ITextResourceSnapshot>(_exception);
    }

    private sealed class NullProvider : ITextResourceProvider
    {
        public ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ITextResourceSnapshot>(null!);
    }

    private sealed class BlockingProvider : ITextResourceProvider
    {
        private readonly TaskCompletionSource<ITextResourceSnapshot> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> ProviderCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CallCount;
        internal CancellationToken ProviderToken;

        public async ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            ProviderToken = cancellationToken;
            Started.TrySetResult(true);
            try
            {
                return await _completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ProviderCanceled.TrySetResult(true);
                throw;
            }
        }

        internal void Complete(ITextResourceSnapshot snapshot) => _completion.TrySetResult(snapshot);
    }

    private sealed class SerialProbeProvider : ITextResourceProvider
    {
        private int _active;
        internal int CallCount;
        internal int MaximumConcurrentCalls;

        public async ValueTask<ITextResourceSnapshot> GetSnapshotAsync(string requestedLocale, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CallCount);
            int active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            try
            {
                await Task.Delay(15, cancellationToken).ConfigureAwait(false);
                return new FakeSnapshot("app", requestedLocale);
            }
            finally { Interlocked.Decrement(ref _active); }
        }

        private void UpdateMaximum(int value)
        {
            int observed;
            do
            {
                observed = MaximumConcurrentCalls;
                if (observed >= value) return;
            }
            while (Interlocked.CompareExchange(ref MaximumConcurrentCalls, value, observed) != observed);
        }
    }

    private sealed class SnapshotFactoryProbe : ITextResourceSnapshotFactory
    {
        private readonly bool _block;
        private readonly bool _failFirst;
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal SnapshotFactoryProbe(bool block = false, bool failFirst = false) { _block = block; _failFirst = failFirst; }
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CallCount;

        public async ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
            CompiledTextResourceCatalog catalog, string canonicalLocale, ITextValueFormatter valueFormatter, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref CallCount);
            Started.TrySetResult(true);
            if (_block) await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (_failFirst && call == 1) throw new InvalidOperationException("factory failed");
            return new CompiledTextResourceSnapshot(catalog, canonicalLocale, valueFormatter);
        }

        internal void Release() => _release.TrySetResult(true);
    }

    private sealed class InvalidSnapshotFactory : ITextResourceSnapshotFactory
    {
        public ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
            CompiledTextResourceCatalog catalog, string canonicalLocale, ITextValueFormatter valueFormatter, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ITextResourceSnapshot>(new FakeSnapshot("other", canonicalLocale));
    }

    private sealed class AbandoningFactory : ITextResourceSnapshotFactory
    {
        private readonly TaskCompletionSource<bool> _firstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> FirstTokenCanceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int CallCount;

        public async ValueTask<ITextResourceSnapshot> CreateSnapshotAsync(
            CompiledTextResourceCatalog catalog, string canonicalLocale, ITextValueFormatter valueFormatter, CancellationToken cancellationToken)
        {
            int call = Interlocked.Increment(ref CallCount);
            if (call == 1)
            {
                using CancellationTokenRegistration registration = cancellationToken.Register(() => FirstTokenCanceled.TrySetResult(true));
                FirstStarted.TrySetResult(true);
                await _firstRelease.Task.ConfigureAwait(false);
            }

            return new CompiledTextResourceSnapshot(catalog, canonicalLocale, valueFormatter);
        }

        internal void ReleaseFirst() => _firstRelease.TrySetResult(true);
    }

    private sealed class CountingSource : IExternalTextResourceSource
    {
        private readonly ExternalTextResourcePack? _pack;
        internal CountingSource(ExternalTextResourcePack? pack) => _pack = pack;
        internal int Calls;
        public ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref Calls);
            return ValueTask.FromResult(_pack);
        }
    }

    private sealed class MutableSource : IExternalTextResourceSource
    {
        internal ExternalTextResourcePack? Pack;
        public ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Pack);
        }
    }

    private sealed class CancelingSource : IExternalTextResourceSource
    {
        internal TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }
}
