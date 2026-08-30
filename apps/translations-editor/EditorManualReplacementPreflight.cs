using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Runic.Translations.Editor;

internal static class EditorManualReplacementPreflight
{
    private static readonly string[] RequiredStaging = ["SHA256SUMS", "dependencies.json", "package-manifest.json", "provenance.json", "release-manifest.json", "sbom.spdx.json", "upstream-receipt.template.json"];

    internal static string Run(string[] arguments)
    {
        try
        {
            Dictionary<string, string> values = Parse(arguments);
            Candidate current = Read(values["--current-archive"], values["--current-staging"]);
            Candidate replacement = Read(values["--replacement-archive"], values["--replacement-staging"]);
            VerifyAuthority(values["--authority"], values["--candidate-receipt"], current, replacement);
            if (current.RuntimeIdentifier != replacement.RuntimeIdentifier) return Ineligible("RID-MISMATCH");
            if (current.Channel != replacement.Channel) return Ineligible("CHANNEL-MISMATCH");
            if (current.SourceRevision != replacement.SourceRevision || current.SourceTree != replacement.SourceTree) return Ineligible("SOURCE-MISMATCH");
            return JsonSerializer.Serialize(new { schema = "runic.manual-replacement-preflight/1", result = "manual-replacement-eligible", runtimeIdentifier = current.RuntimeIdentifier, guidance = "Verify the displayed archives and perform the replacement yourself; this command does not replace, install, delete, download, or roll back anything." });
        }
        catch (PreflightException exception) { return Ineligible(exception.Code); }
        catch (Exception) { return Ineligible("MALFORMED-INPUT"); }
    }

    private static Candidate Read(string archivePath, string stagingPath)
    {
        if (!File.Exists(archivePath) || new FileInfo(archivePath).LinkTarget is not null) throw new PreflightException("ARCHIVE-INVALID");
        if (!Directory.Exists(stagingPath) || new DirectoryInfo(stagingPath).LinkTarget is not null) throw new PreflightException("STAGING-INVALID");
        string[] actual = Directory.GetFiles(stagingPath, "*", SearchOption.TopDirectoryOnly).Select(Path.GetFileName).OrderBy(static value => value, StringComparer.Ordinal).ToArray()!;
        if (!actual.SequenceEqual(RequiredStaging.OrderBy(static value => value, StringComparer.Ordinal), StringComparer.Ordinal) || Directory.GetDirectories(stagingPath).Length != 0) throw new PreflightException("STAGING-NOT-CLOSED");
        foreach (string name in RequiredStaging) if (new FileInfo(Path.Combine(stagingPath, name)).LinkTarget is not null) throw new PreflightException("STAGING-LINK");
        using JsonDocument release = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagingPath, "release-manifest.json")));
        using JsonDocument package = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagingPath, "package-manifest.json")));
        using JsonDocument sbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagingPath, "sbom.spdx.json")));
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagingPath, "provenance.json")));
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagingPath, "upstream-receipt.template.json")));
        JsonElement root = release.RootElement, artifact = root.GetProperty("artifacts").EnumerateArray().Single();
        string archiveName = Path.GetFileName(archivePath), archiveHash = Hash(archivePath), rid = root.GetProperty("runtimeIdentifier").GetString()!;
        if (root.GetProperty("schema").GetString() != "runic.translations.editor-release/1" || artifact.GetProperty("path").GetString() != archiveName || artifact.GetProperty("sha256").GetString() != archiveHash || artifact.GetProperty("identity").GetString() != "Runic.Translations.Editor" || artifact.GetProperty("product").GetString() != "editor" || artifact.GetProperty("type").GetString() != "distribution") throw new PreflightException("ARCHIVE-RECEIPT-MISMATCH");
        if (File.ReadAllText(archivePath + ".sha256").Trim() != $"{archiveHash}  {archiveName}") throw new PreflightException("ARCHIVE-CHECKSUM-MISMATCH");
        VerifyChecksums(stagingPath, archiveName, archiveHash);
        string revision = root.GetProperty("repositoryCommit").GetString()!, tree = root.GetProperty("repositoryTree").GetString()!;
        if (!IsGit(revision) || !IsGit(tree) || package.RootElement.GetProperty("schema").GetString() != "runic.translations.editor-package/1" || package.RootElement.GetProperty("runtimeIdentifier").GetString() != rid || package.RootElement.GetProperty("repositoryCommit").GetString() != revision || package.RootElement.GetProperty("repositoryTree").GetString() != tree || provenance.RootElement.GetProperty("schema").GetString() != "runic.translations.editor-provenance/1" || provenance.RootElement.GetProperty("source").GetProperty("revision").GetString() != revision || provenance.RootElement.GetProperty("source").GetProperty("tree").GetString() != tree || sbom.RootElement.GetProperty("spdxVersion").GetString() != "SPDX-2.3" || !receipt.RootElement.TryGetProperty("attestationBundle", out JsonElement attestation) || attestation.GetProperty("path").GetString() != "REPLACE_WITH_GITHUB_ATTESTATION_BUNDLE") throw new PreflightException("STAGING-PROVENANCE-MISMATCH");
        return new Candidate(rid, root.GetProperty("channel").GetString()!, revision, tree, provenance.RootElement.GetProperty("source").GetProperty("repository").GetString()!, archiveName, archiveHash, Hash(Path.Combine(stagingPath, "SHA256SUMS")), RequiredStaging.Where(static name => name != "SHA256SUMS").ToDictionary(static name => name, name => Hash(Path.Combine(stagingPath, name)), StringComparer.Ordinal));
    }

    private static void VerifyChecksums(string stagingPath, string archiveName, string archiveHash)
    {
        string[] lines = File.ReadAllLines(Path.Combine(stagingPath, "SHA256SUMS")).Where(static line => line.Length > 0).ToArray();
        if (lines.Length != 7) throw new PreflightException("CHECKSUMS-MALFORMED");
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in lines) { string[] parts = line.Split("  "); if (parts.Length != 2 || parts[0].Length != 64 || !entries.TryAdd(parts[1], parts[0])) throw new PreflightException("CHECKSUMS-MALFORMED"); }
        foreach (string name in RequiredStaging.Where(static value => value != "SHA256SUMS")) if (!entries.TryGetValue(name, out string? value) || value != Hash(Path.Combine(stagingPath, name))) throw new PreflightException("CHECKSUMS-MISMATCH");
        if (!entries.TryGetValue(archiveName, out string? digest) || digest != archiveHash) throw new PreflightException("CHECKSUMS-MISMATCH");
    }

    private static void VerifyAuthority(string path, string candidateReceipt, Candidate current, Candidate replacement)
    {
        using JsonDocument authority = JsonDocument.Parse(File.ReadAllText(path));
        using JsonDocument receipt = JsonDocument.Parse(File.ReadAllText(candidateReceipt));
        JsonElement journeys = receipt.RootElement.GetProperty("journeys");
        if (receipt.RootElement.GetProperty("schema").GetString() != "runic.unsigned-candidate-set-consumer-repeat/1" || journeys.GetArrayLength() != 2 || journeys[0].GetRawText() != journeys[1].GetRawText()) throw new PreflightException("CANDIDATE-MODEL-STALE");
        JsonElement journey = journeys[0], candidateSet = journey.GetProperty("candidateSet");
        JsonElement releaseAuthority = candidateSet.GetProperty("releaseAuthority"), distributionFacts = releaseAuthority.GetProperty("distribution"), platforms = candidateSet.GetProperty("platforms");
        if (journey.GetProperty("schema").GetString() != "runic.unsigned-candidate-set-consumer/1" || journey.GetProperty("isolation").GetProperty("workingDirectory").GetString() != "temporary-empty" || journey.GetProperty("noProductProjectReference").ValueKind != JsonValueKind.True || candidateSet.GetProperty("schema").GetString() != "runic.unsigned-candidate-set/1" || candidateSet.GetProperty("publication").GetString() != "forbidden" || candidateSet.GetProperty("productEvidence").GetArrayLength() != 0 || releaseAuthority.GetProperty("path").GetString() != "runic.release.json" || !IsGit(releaseAuthority.GetProperty("revision").GetString()!) || !IsGit(releaseAuthority.GetProperty("tree").GetString()!) || releaseAuthority.GetProperty("sha256").GetString() != Hash(path) || distributionFacts.GetProperty("id").GetString() != "translations-editor-archive" || distributionFacts.GetProperty("product").GetString() != "editor" || distributionFacts.GetProperty("kind").GetString() != "application-archive" || distributionFacts.GetProperty("identity").GetString() != "Runic.Translations.Editor" || distributionFacts.GetProperty("version").GetProperty("state").GetString() != "unassigned" || distributionFacts.GetProperty("version").GetProperty("value").ValueKind != JsonValueKind.Null || platforms.GetArrayLength() != 3) throw new PreflightException("CANDIDATE-MODEL-STALE");
        JsonElement candidateSource = candidateSet.GetProperty("source");
        string[] expectedRids = ["linux-x64", "osx-arm64", "win-x64"];
        if (!MatchesSource(candidateSource, current) || !MatchesSource(candidateSource, replacement) || !platforms.EnumerateArray().All(platform => MatchesSource(platform.GetProperty("source"), current)) || !platforms.EnumerateArray().Select(platform => platform.GetProperty("runtimeIdentifier").GetString()).OrderBy(static rid => rid, StringComparer.Ordinal).SequenceEqual(expectedRids, StringComparer.Ordinal) || !MatchesPlatform(platforms, current) || !MatchesPlatform(platforms, replacement)) throw new PreflightException("CANDIDATE-PLATFORM-MISMATCH");
        JsonElement root = authority.RootElement;
        JsonElement distribution = root.GetProperty("distributions").EnumerateArray().Single(value => value.GetProperty("id").GetString() == "translations-editor-archive");
        if (distribution.GetProperty("product").GetString() != "editor" || distribution.GetProperty("version").GetProperty("state").GetString() != "unassigned" || distribution.GetProperty("version").GetProperty("value").ValueKind != JsonValueKind.Null) throw new PreflightException("AUTHORITY-DISTRIBUTION-INVALID");
        bool lane = root.GetProperty("compatibilityTrains").EnumerateArray().SelectMany(train => train.GetProperty("lanes").EnumerateArray()).Any(lane => lane.GetProperty("name").GetString() == "current" && lane.GetProperty("products").EnumerateArray().Any(product => product.GetString() == "editor"));
        if (!lane) throw new PreflightException("AUTHORITY-LANE-UNSUPPORTED");
    }

    private static bool MatchesSource(JsonElement source, Candidate candidate) => source.GetProperty("repository").GetString() == candidate.SourceRepository && source.GetProperty("revision").GetString() == candidate.SourceRevision && source.GetProperty("tree").GetString() == candidate.SourceTree;

    private static bool MatchesPlatform(JsonElement platforms, Candidate candidate)
    {
        JsonElement[] matches = platforms.EnumerateArray().Where(platform => platform.GetProperty("runtimeIdentifier").GetString() == candidate.RuntimeIdentifier).ToArray();
        if (matches.Length != 1) return false;
        JsonElement platform = matches[0], archive = platform.GetProperty("archive"), staging = platform.GetProperty("staging");
        if (archive.GetProperty("path").GetString() != candidate.ArchiveName || archive.GetProperty("sha256").GetString() != candidate.ArchiveHash || !MatchesSource(platform.GetProperty("source"), candidate) || staging.GetProperty("sha256sums").GetProperty("sha256").GetString() != candidate.StagingChecksumsHash) return false;
        JsonElement files = staging.GetProperty("files");
        return candidate.StagingFiles.Count == files.EnumerateObject().Count() && candidate.StagingFiles.All(pair => files.TryGetProperty(pair.Key, out JsonElement value) && value.GetString() == pair.Value);
    }

    private static Dictionary<string, string> Parse(string[] arguments)
    {
        string[] names = ["--current-archive", "--current-staging", "--replacement-archive", "--replacement-staging", "--authority", "--candidate-receipt"];
        if (arguments.Length != names.Length * 2) throw new PreflightException("USAGE");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2) if (!names.Contains(arguments[index], StringComparer.Ordinal) || string.IsNullOrWhiteSpace(arguments[index + 1]) || !values.TryAdd(arguments[index], arguments[index + 1])) throw new PreflightException("USAGE");
        return values;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static bool IsGit(string value) => value.Length == 40 && value.All(static character => character is >= 'a' and <= 'f' or >= '0' and <= '9');
    private static string Ineligible(string code) => JsonSerializer.Serialize(new { schema = "runic.manual-replacement-preflight/1", result = "manual-replacement-ineligible", diagnostics = new[] { code } });
    private sealed record Candidate(string RuntimeIdentifier, string Channel, string SourceRevision, string SourceTree, string SourceRepository, string ArchiveName, string ArchiveHash, string StagingChecksumsHash, Dictionary<string, string> StagingFiles);
    private sealed class PreflightException(string code) : Exception { internal string Code { get; } = code; }
}
