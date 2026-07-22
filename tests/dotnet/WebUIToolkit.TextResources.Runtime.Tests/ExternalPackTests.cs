using System;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.TextResources.Runtime.Tests;

internal static class ExternalPackTests
{
    private const string Fingerprint = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    public static void Register(TestRunner runner)
    {
        runner.Add("external pack verifies valid contract", VerifiesValid);
        runner.Add("external pack accepts subset and sorts messages", AcceptsSubsetAndSorts);
        runner.Add("external pack accepts arbitrary member order", AcceptsMemberOrder);
        runner.Add("external pack rejects fingerprint tamper", () => RejectMutation(Fingerprint, "sha256:1111111111111111111111111111111111111111111111111111111111111111", "fingerprint"));
        runner.Add("external pack rejects catalog mismatch", () => RejectMutation("\"catalog\":\"app\"", "\"catalog\":\"other\"", "catalog"));
        runner.Add("external pack rejects locale mismatch", () => RejectMutation("\"locale\":\"en-US\"", "\"locale\":\"de-DE\"", "locale"));
        runner.Add("external pack rejects artifact version", () => RejectMutation("\"artifactVersion\":1", "\"artifactVersion\":2", "artifact version"));
        runner.Add("external pack rejects grammar version", () => RejectMutation("\"messageGrammarVersion\":1", "\"messageGrammarVersion\":2", "grammar version"));
        runner.Add("external pack rejects unknown root member", UnknownRootMember);
        runner.Add("external pack rejects duplicate root member", DuplicateRootMember);
        runner.Add("external pack rejects unknown message", UnknownMessage);
        runner.Add("external pack rejects duplicate message", DuplicateMessage);
        runner.Add("external pack rejects descriptor mismatch", DescriptorMismatch);
        runner.Add("external pack rejects placeholder mismatch", PlaceholderMismatch);
        runner.Add("external pack rejects malformed pattern", MalformedPattern);
        runner.Add("external pack rejects malformed JSON", MalformedJson);
        runner.Add("external pack rejects invalid UTF-8", InvalidUtf8);
        runner.Add("external pack rejects trailing content", TrailingContent);
        runner.Add("external pack enforces document limit", DocumentLimit);
        runner.Add("external pack enforces depth limit", DepthLimit);
        runner.Add("external pack enforces message limit", MessageLimit);
        runner.Add("external pack enforces pattern byte limit", PatternLimit);
        runner.Add("external pack enforces argument limit", ArgumentLimit);
        runner.Add("external pack validates limit tightening", LimitValidation);
        runner.Add("external pack invokes integrity verifier before parse", IntegrityVerifier);
        runner.Add("external pack rejects integrity policy", IntegrityRejected);
        runner.Add("external pack sanitizes integrity exception", IntegrityException);
        runner.Add("external pack honors cancellation", Cancellation);
        runner.Add("external contract defensively copies lists", ContractImmutability);
        runner.Add("external contract validates ordering and identity", ContractValidation);
        runner.Add("external source receives contract identity and verifies", SourceLoads);
        runner.Add("external source may return no pack", SourceReturnsNull);
        runner.Add("external source failure is sanitized", SourceFailure);
        runner.Add("external source honors cancellation", SourceCancellation);
        runner.Add("external pack owns one immutable verification image", IntegrityToctouIsolation);
        runner.Add("external pack exposes stable failure reasons", FailureReasons);
    }

    private static async Task VerifiesValid()
    {
        TextResourcePackContract contract = CreateContract();
        VerifiedExternalTextResourcePack verified = await Verify(ValidJson(), contract);
        Assert.Equal("app", verified.Catalog);
        Assert.Equal("en-US", verified.Locale);
        Assert.Equal(Fingerprint, verified.ContractFingerprint);
        Assert.Equal(2, verified.Messages.Count);
        Assert.True(verified.TryGetPattern(new TextResourceKey("app", 0, "alpha.greeting"), out string greeting), "Known key was absent.");
        Assert.Equal("Hello {name}", greeting);
        Assert.False(verified.TryGetPattern(new TextResourceKey("app", 9, "missing.key"), out _), "Unknown key was accepted.");
    }

    private static async Task AcceptsSubsetAndSorts()
    {
        string json = Root("\"beta.count\":" + CountMessage() + ",\"alpha.greeting\":" + GreetingMessage());
        VerifiedExternalTextResourcePack verified = await Verify(json, CreateContract());
        Assert.Equal("alpha.greeting", verified.Messages[0].Key.Name);
        Assert.Equal("beta.count", verified.Messages[1].Key.Name);

        verified = await Verify(Root("\"beta.count\":" + CountMessage()), CreateContract());
        Assert.Equal(1, verified.Messages.Count);
        Assert.Equal("beta.count", verified.Messages[0].Key.Name);
    }

    private static async Task AcceptsMemberOrder()
    {
        string json = "{\"messages\":{\"alpha.greeting\":{\"arguments\":[{\"format\":\"none\",\"type\":\"string\",\"name\":\"name\"}],\"pattern\":\"Hello {name}\"}}," +
            "\"contractFingerprint\":\"" + Fingerprint + "\",\"locale\":\"en-US\",\"catalog\":\"app\",\"messageGrammarVersion\":1,\"artifactVersion\":1}";
        VerifiedExternalTextResourcePack verified = await Verify(json, CreateContract());
        Assert.Equal(1, verified.Messages.Count);
    }

    private static Task<TextResourcePackException> RejectMutation(string oldValue, string newValue, string expected) =>
        Assert.ThrowsAsync<TextResourcePackException>(() => Verify(ValidJson().Replace(oldValue, newValue, StringComparison.Ordinal), CreateContract()), expected);

    private static Task UnknownRootMember() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson().Replace("{\"artifactVersion\"", "{\"unknown\":1,\"artifactVersion\"", StringComparison.Ordinal), CreateContract()), "unknown property");

    private static Task DuplicateRootMember() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson().Replace("{\"artifactVersion\":1", "{\"artifactVersion\":1,\"artifactVersion\":1", StringComparison.Ordinal), CreateContract()), "duplicate property");

    private static Task UnknownMessage() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(Root("\"unknown.key\":" + GreetingMessage()), CreateContract()), "unknown message key");

    private static Task DuplicateMessage() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(Root("\"alpha.greeting\":" + GreetingMessage() + ",\"alpha.greeting\":" + GreetingMessage()), CreateContract()), "duplicate message key");

    private static Task DescriptorMismatch() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson().Replace("\"type\":\"string\"", "\"type\":\"guid\"", StringComparison.Ordinal), CreateContract()), "argument contract");

    private static Task PlaceholderMismatch() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson().Replace("Hello {name}", "Hello {other}", StringComparison.Ordinal), CreateContract()), "pattern");

    private static Task MalformedPattern() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson().Replace("Hello {name}", "Hello {name", StringComparison.Ordinal), CreateContract()), "pattern");

    private static Task MalformedJson() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify("{", CreateContract()), "incomplete");

    private static async Task InvalidUtf8()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ValidJson());
        int valueIndex = Array.IndexOf(bytes, (byte)'H');
        bytes[valueIndex] = 0xff;
        await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(new ExternalTextResourcePack(bytes), CreateContract()), "invalid UTF-8");
    }

    private static Task TrailingContent() => Assert.ThrowsAsync<TextResourcePackException>(
        () => Verify(ValidJson() + "{}", CreateContract()), "after the root");

    private static async Task DocumentLimit()
    {
        byte[] bytes = Encoding.UTF8.GetBytes(ValidJson());
        TextResourcePackLimits limits = new(bytes.Length - 1, 64, 50_000, 64 * 1024, 32);
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(new ExternalTextResourcePack(bytes), CreateContract(), limits), "document limit");
        Assert.Equal(TextResourcePackFailureReason.LimitExceeded, TextResourcePackFailure.GetReason(exception));
    }

    private static async Task DepthLimit()
    {
        TextResourcePackLimits limits = new(8 * 1024 * 1024, 2, 50_000, 64 * 1024, 32);
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(), limits), "depth limit");
        Assert.Equal(TextResourcePackFailureReason.LimitExceeded, TextResourcePackFailure.GetReason(exception));
    }

    private static async Task MessageLimit()
    {
        TextResourcePackLimits limits = new(8 * 1024 * 1024, 64, 1, 64 * 1024, 32);
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(), limits), "message limit");
        Assert.Equal(TextResourcePackFailureReason.LimitExceeded, TextResourcePackFailure.GetReason(exception));
    }

    private static async Task PatternLimit()
    {
        TextResourcePackLimits limits = new(8 * 1024 * 1024, 64, 50_000, 5, 32);
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(), limits), "pattern limit");
        Assert.Equal(TextResourcePackFailureReason.LimitExceeded, TextResourcePackFailure.GetReason(exception));
    }

    private static async Task ArgumentLimit()
    {
        TextResourcePackContract contract = CreateTwoArgumentContract();
        string message = "{\"pattern\":\"{a} {b}\",\"arguments\":[{\"name\":\"a\",\"type\":\"string\",\"format\":\"none\"},{\"name\":\"b\",\"type\":\"string\",\"format\":\"none\"}]}";
        TextResourcePackLimits limits = new(8 * 1024 * 1024, 64, 50_000, 64 * 1024, 1);
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(Root("\"alpha.greeting\":" + message)), contract, limits), "argument limit");
        Assert.Equal(TextResourcePackFailureReason.LimitExceeded, TextResourcePackFailure.GetReason(exception));
    }

    private static void LimitValidation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextResourcePackLimits(0, 1, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new TextResourcePackLimits(TextResourcePackLimits.DefaultMaximumDocumentBytes + 1, 1, 1, 1, 1));
    }

    private static async Task IntegrityVerifier()
    {
        int calls = 0;
        VerifiedExternalTextResourcePack verified = await TextResourcePackLoader.VerifyAsync(
            Pack(ValidJson()), CreateContract(), integrityVerifier: (content, token) =>
            {
                calls++;
                Assert.True(content.Length > 0, "Verifier received no bytes.");
                Assert.False(token.IsCancellationRequested, "Unexpected cancellation.");
                return ValueTask.FromResult(true);
            });
        Assert.Equal(1, calls);
        Assert.Equal(2, verified.Messages.Count);
    }

    private static async Task IntegrityRejected() => await Assert.ThrowsAsync<TextResourcePackException>(
        async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(),
            integrityVerifier: static (content, token) => ValueTask.FromResult(false)), "integrity policy");

    private static async Task IntegrityException()
    {
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(),
                integrityVerifier: static (content, token) => throw new InvalidOperationException("secret")), "verification failed");
        Assert.False(exception.Message.Contains("secret", StringComparison.Ordinal), "Verifier detail leaked.");
    }

    private static async Task Cancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(), cancellationToken: source.Token));
        Assert.Equal(TextResourcePackFailureReason.Cancelled, TextResourcePackFailure.GetReason((Exception)exception));
    }

    private static void ContractImmutability()
    {
        TextResourcePackArgumentContract[] arguments = [new("name", TextArgumentType.String, TextArgumentFormat.None)];
        TextResourcePackMessageContract message = new(new TextResourceKey("app", 0, "alpha.greeting"), arguments);
        arguments[0] = new("other", TextArgumentType.String, TextArgumentFormat.None);
        Assert.Equal("name", message.Arguments[0].Name);

        TextResourcePackMessageContract[] messages = [message];
        TextResourcePackContract contract = new("app", "en-US", Fingerprint, messages);
        messages[0] = new(new TextResourceKey("app", 1, "beta.count"));
        Assert.Equal("alpha.greeting", contract.Messages[0].Key.Name);
    }

    private static void ContractValidation()
    {
        TextResourcePackMessageContract a = new(new TextResourceKey("app", 0, "alpha.greeting"));
        TextResourcePackMessageContract b = new(new TextResourceKey("app", 1, "beta.count"));
        Assert.Throws<ArgumentException>(() => _ = new TextResourcePackContract("app", "en-US", Fingerprint, [b, a]));
        Assert.Throws<ArgumentException>(() => _ = new TextResourcePackContract("app", "EN-us", Fingerprint, [a]));
        Assert.Throws<ArgumentException>(() => _ = new TextResourcePackContract("app", "en-US", "bad", [a]));
        Assert.Throws<ArgumentException>(() => _ = new TextResourcePackMessageContract(
            new TextResourceKey("app", 0, "alpha.greeting"),
            [new("z", TextArgumentType.String, TextArgumentFormat.None), new("a", TextArgumentType.String, TextArgumentFormat.None)]));
    }

    private static async Task SourceLoads()
    {
        SourceProbe source = new(Pack(ValidJson()));
        VerifiedExternalTextResourcePack? verified = await TextResourcePackLoader.LoadAsync(source, CreateContract());
        Assert.True(verified is not null, "Available pack was not loaded.");
        Assert.Equal("app", source.Catalog);
        Assert.Equal("en-US", source.Locale);
        Assert.Equal(1, source.Calls);
    }

    private static async Task SourceReturnsNull()
    {
        SourceProbe source = new(null);
        VerifiedExternalTextResourcePack? verified = await TextResourcePackLoader.LoadAsync(source, CreateContract());
        Assert.True(verified is null, "Null source result did not remain null.");
    }

    private static async Task SourceFailure()
    {
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(
            async () => await TextResourcePackLoader.LoadAsync(new ThrowingSource(), CreateContract()), "source failed");
        Assert.False(exception.Message.Contains("secret", StringComparison.Ordinal), "Source exception details leaked.");
    }

    private static async Task SourceCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await TextResourcePackLoader.LoadAsync(new SourceProbe(Pack(ValidJson())), CreateContract(), cancellationToken: source.Token));
    }

    private static async Task IntegrityToctouIsolation()
    {
        byte[] callerBytes = Encoding.UTF8.GetBytes(ValidJson());
        int mutationIndex = Array.IndexOf(callerBytes, (byte)'H');
        VerifiedExternalTextResourcePack verified = await TextResourcePackLoader.VerifyAsync(
            new ExternalTextResourcePack(callerBytes), CreateContract(), integrityVerifier: (content, token) =>
            {
                Assert.True(MemoryMarshal.TryGetArray(content, out ArraySegment<byte> loaderBytes),
                    "Loader verification image was not array-backed.");
                Assert.False(ReferenceEquals(callerBytes, loaderBytes.Array),
                    "Integrity verifier received caller-mutable backing bytes.");
                callerBytes[mutationIndex] = (byte)'J';
                return ValueTask.FromResult(true);
            });
        Assert.True(verified.TryGetPattern(new TextResourceKey("app", 0, "alpha.greeting"), out string pattern),
            "Verified greeting was missing.");
        Assert.Equal("Hello {name}", pattern);
    }

    private static async Task FailureReasons()
    {
        await AssertReason(
            () => Verify(ValidJson().Replace("\"artifactVersion\":1", "\"artifactVersion\":2", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.ArtifactVersionMismatch);
        await AssertReason(
            () => Verify(ValidJson().Replace("\"messageGrammarVersion\":1", "\"messageGrammarVersion\":2", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.MessageGrammarVersionMismatch);
        await AssertReason(
            () => Verify(ValidJson().Replace("\"catalog\":\"app\"", "\"catalog\":\"other\"", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.CatalogMismatch);
        await AssertReason(
            () => Verify(ValidJson().Replace("\"locale\":\"en-US\"", "\"locale\":\"de-DE\"", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.LocaleMismatch);
        await AssertReason(
            () => Verify(ValidJson().Replace(Fingerprint, "sha256:1111111111111111111111111111111111111111111111111111111111111111", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.ContractFingerprintMismatch);

        byte[] bytes = Encoding.UTF8.GetBytes(ValidJson());
        TextResourcePackLimits limits = new(bytes.Length - 1, 64, 50_000, 64 * 1024, 32);
        await AssertReason(
            async () => await TextResourcePackLoader.VerifyAsync(new ExternalTextResourcePack(bytes), CreateContract(), limits),
            TextResourcePackFailureReason.LimitExceeded);
        await AssertReason(() => Verify("{", CreateContract()), TextResourcePackFailureReason.Malformed);
        await AssertReason(
            () => Verify(Root("\"unknown.key\":" + GreetingMessage()), CreateContract()),
            TextResourcePackFailureReason.UnknownKey);
        await AssertReason(
            () => Verify(ValidJson().Replace("\"type\":\"string\"", "\"type\":\"guid\"", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.ArgumentContractMismatch);
        await AssertReason(
            () => Verify(ValidJson().Replace("Hello {name}", "Hello {other}", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.MalformedPattern);
        await AssertReason(
            () => Verify(ValidJson().Replace("{\"artifactVersion\"", "{\"sourceUri\":\"redacted\",\"artifactVersion\"", StringComparison.Ordinal), CreateContract()),
            TextResourcePackFailureReason.UnknownMember);
        await AssertReason(
            async () => await TextResourcePackLoader.VerifyAsync(Pack(ValidJson()), CreateContract(),
                integrityVerifier: static (content, token) => ValueTask.FromResult(false)),
            TextResourcePackFailureReason.IntegrityRejected);
        await AssertReason(
            async () => await TextResourcePackLoader.LoadAsync(new ThrowingSource(), CreateContract()),
            TextResourcePackFailureReason.SourceFailure);

        Assert.Equal(TextResourcePackFailureReason.Unknown,
            TextResourcePackFailure.GetReason(new TextResourcePackException("legacy")));
        Assert.Equal(TextResourcePackFailureReason.Cancelled,
            TextResourcePackFailure.GetReason((Exception)new OperationCanceledException()));
        Assert.Equal(TextResourcePackFailureReason.Unknown,
            TextResourcePackFailure.GetReason((Exception)new InvalidOperationException()));
        Assert.Throws<ArgumentNullException>(() => TextResourcePackFailure.GetReason(null!));
        Assert.Throws<ArgumentNullException>(() => TextResourcePackFailure.GetDiagnosticId(null!));
    }

    private static async Task AssertReason(Func<Task> action, TextResourcePackFailureReason expected)
    {
        TextResourcePackException exception = await Assert.ThrowsAsync<TextResourcePackException>(action);
        Assert.Equal(expected, TextResourcePackFailure.GetReason(exception));
        Assert.Equal("WUTTEXT0023", TextResourcePackFailure.DiagnosticId);
        Assert.Equal(TextResourcePackFailure.DiagnosticId, TextResourcePackFailure.GetDiagnosticId(exception));
    }

    private static TextResourcePackContract CreateContract() => new(
        "app", "en-US", Fingerprint,
        [
            new TextResourcePackMessageContract(new TextResourceKey("app", 0, "alpha.greeting"),
                [new TextResourcePackArgumentContract("name", TextArgumentType.String, TextArgumentFormat.None)]),
            new TextResourcePackMessageContract(new TextResourceKey("app", 1, "beta.count"),
                [new TextResourcePackArgumentContract("count", TextArgumentType.Int, TextArgumentFormat.Grouped)]),
        ]);

    private static TextResourcePackContract CreateTwoArgumentContract() => new(
        "app", "en-US", Fingerprint,
        [new TextResourcePackMessageContract(new TextResourceKey("app", 0, "alpha.greeting"),
            [
                new TextResourcePackArgumentContract("a", TextArgumentType.String, TextArgumentFormat.None),
                new TextResourcePackArgumentContract("b", TextArgumentType.String, TextArgumentFormat.None),
            ])]);

    private static Task<VerifiedExternalTextResourcePack> Verify(string json, TextResourcePackContract contract) =>
        TextResourcePackLoader.VerifyAsync(Pack(json), contract).AsTask();

    private static ExternalTextResourcePack Pack(string json) => new(Encoding.UTF8.GetBytes(json));

    private static string ValidJson() => Root("\"alpha.greeting\":" + GreetingMessage() + ",\"beta.count\":" + CountMessage());

    private static string Root(string messages) => "{\"artifactVersion\":1,\"messageGrammarVersion\":1,\"catalog\":\"app\",\"locale\":\"en-US\",\"contractFingerprint\":\"" + Fingerprint + "\",\"messages\":{" + messages + "}}";

    private static string GreetingMessage() => "{\"pattern\":\"Hello {name}\",\"arguments\":[{\"name\":\"name\",\"type\":\"string\",\"format\":\"none\"}]}";

    private static string CountMessage() => "{\"pattern\":\"Count: {count}\",\"arguments\":[{\"name\":\"count\",\"type\":\"int\",\"format\":\"grouped\"}]}";

    private sealed class SourceProbe : IExternalTextResourceSource
    {
        private readonly ExternalTextResourcePack? _pack;
        internal SourceProbe(ExternalTextResourcePack? pack) => _pack = pack;
        internal string? Catalog;
        internal string? Locale;
        internal int Calls;
        public ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Catalog = catalog;
            Locale = locale;
            return ValueTask.FromResult(_pack);
        }
    }

    private sealed class ThrowingSource : IExternalTextResourceSource
    {
        public ValueTask<ExternalTextResourcePack?> LoadAsync(string catalog, string locale, CancellationToken cancellationToken) =>
            ValueTask.FromException<ExternalTextResourcePack?>(new InvalidOperationException("secret"));
    }
}
