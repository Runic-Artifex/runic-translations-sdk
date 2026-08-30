using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Runic.Translations.Authoring;

public static class TranslationWorkspaceTransaction
{
    private const string JournalFileName = ".runic-translations.transaction.json";
    private const int JournalVersion = 1;
    private const int MaximumEdits = 512;
    private const int MaximumJournalBytes = 96 * 1024 * 1024;

    public static void Commit(TranslationWorkspaceTransactionPlan plan) => CommitCore(plan, null);

    public static TranslationPendingTransaction? GetPending(string root)
    {
        string fullRoot = RequireRoot(root);
        string journalPath = Path.Combine(fullRoot, JournalFileName);
        if (!File.Exists(journalPath)) return null;
        TransactionJournal journal = ReadJournal(fullRoot, journalPath);
        return new TranslationPendingTransaction(
            fullRoot,
            journal.CatalogId,
            journal.Entries.Select(static entry => entry.Path).ToArray());
    }

    public static void Recover(string root, TranslationWorkspaceRecoveryMode mode)
    {
        string fullRoot = RequireRoot(root);
        string journalPath = Path.Combine(fullRoot, JournalFileName);
        if (!File.Exists(journalPath))
            throw new TranslationAuthoringException($"Workspace '{fullRoot}' has no pending transaction.");
        TransactionJournal journal = ReadJournal(fullRoot, journalPath);
        try
        {
            if (mode == TranslationWorkspaceRecoveryMode.Complete) Complete(fullRoot, journal);
            else Rollback(fullRoot, journal);
            Cleanup(fullRoot, journal, journalPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            throw new TranslationAuthoringException(
                $"Could not {mode.ToString().ToLowerInvariant()} the pending transaction; its recovery journal was retained.",
                exception);
        }
    }

    internal static void CommitForTesting(TranslationWorkspaceTransactionPlan plan, int failAfterAppliedEdit) =>
        CommitCore(plan, applied =>
        {
            if (applied == failAfterAppliedEdit) throw new SimulatedInterruptionException();
        });

    private static void CommitCore(TranslationWorkspaceTransactionPlan plan, Action<int>? afterApply)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.Compilation.Success)
            throw new TranslationAuthoringException("A workspace transaction cannot commit a compiler-invalid plan.");
        if (plan.Edits.Count is 0 or > MaximumEdits)
            throw new TranslationAuthoringException($"A workspace transaction must contain between 1 and {MaximumEdits} edits.");

        string root = RequireRoot(plan.Root);
        string journalPath = Path.Combine(root, JournalFileName);
        if (File.Exists(journalPath))
            throw new TranslationAuthoringException("The workspace has a pending transaction that must be recovered first.");

        TranslationWorkspaceEdit[] edits = plan.Edits.OrderBy(static edit => edit.RelativePath, StringComparer.Ordinal).ToArray();
        RejectDuplicatePaths(edits);
        var entries = new List<JournalEntry>(edits.Length);
        for (int index = 0; index < edits.Length; index++)
        {
            TranslationWorkspaceEdit edit = edits[index];
            string target = ResolveContainedPath(root, edit.RelativePath);
            bool exists = File.Exists(target);
            byte[]? original = exists ? File.ReadAllBytes(target) : null;
            ValidateExpectedRevision(edit, original);
            ValidateEditKind(edit, exists);
            string? temporaryPath = edit.Kind == TranslationWorkspaceEditKind.Delete
                ? null
                : $".{Path.GetFileName(target)}.runic-{Guid.NewGuid():N}.tmp";
            entries.Add(new JournalEntry(
                NormalizePath(edit.RelativePath),
                temporaryPath,
                original is null ? null : Convert.ToBase64String(original),
                edit.Kind == TranslationWorkspaceEditKind.Delete,
                edit.Bytes is null ? null : Revision(edit.Bytes)));
        }

        var journal = new TransactionJournal(JournalVersion, root, plan.CatalogId, entries.ToArray());
        string journalTemporaryPath = journalPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WriteTemporaryFiles(root, edits, journal.Entries);
            WriteJournal(journalTemporaryPath, journal);
            File.Move(journalTemporaryPath, journalPath);
            for (int index = 0; index < journal.Entries.Length; index++)
            {
                Apply(root, journal.Entries[index]);
                afterApply?.Invoke(index + 1);
            }
            Cleanup(root, journal, journalPath);
        }
        catch (SimulatedInterruptionException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            try
            {
                if (File.Exists(journalPath))
                {
                    Rollback(root, journal);
                    Cleanup(root, journal, journalPath);
                }
                else
                {
                    Cleanup(root, journal, journalTemporaryPath);
                }
            }
            catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                throw new TranslationAuthoringException(
                    "The workspace transaction failed and automatic rollback was incomplete; use the retained recovery journal.",
                    new AggregateException(exception, rollbackException));
            }
            throw new TranslationAuthoringException("The workspace transaction failed and was rolled back.", exception);
        }
        finally
        {
            TryDelete(journalTemporaryPath);
        }
    }

    private static void WriteTemporaryFiles(
        string root,
        TranslationWorkspaceEdit[] edits,
        JournalEntry[] entries)
    {
        for (int index = 0; index < edits.Length; index++)
        {
            if (entries[index].Delete) continue;
            string target = ResolveContainedPath(root, entries[index].Path);
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, entries[index].TemporaryName!);
            WriteFile(temporary, edits[index].Bytes!, FileMode.CreateNew);
        }
    }

    private static void WriteJournal(string path, TransactionJournal journal)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(journal, AuthoringJsonContext.Default.TransactionJournal);
        if (bytes.Length > MaximumJournalBytes)
            throw new InvalidOperationException($"The transaction recovery journal exceeds {MaximumJournalBytes} bytes.");
        WriteFile(path, bytes, FileMode.CreateNew);
    }

    private static TransactionJournal ReadJournal(string root, string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumJournalBytes)
            throw new TranslationAuthoringException($"The transaction recovery journal exceeds {MaximumJournalBytes} bytes.");
        try
        {
            TransactionJournal? journal = JsonSerializer.Deserialize(File.ReadAllBytes(path), AuthoringJsonContext.Default.TransactionJournal);
            if (journal is null || journal.Version != JournalVersion || journal.Entries is null ||
                journal.Entries.Length is 0 or > MaximumEdits ||
                string.IsNullOrWhiteSpace(journal.CatalogId) ||
                !string.Equals(Path.GetFullPath(journal.Root), root, PathComparison))
                throw new InvalidOperationException("The transaction recovery journal is invalid.");
            for (int index = 0; index < journal.Entries.Length; index++)
            {
                JournalEntry entry = journal.Entries[index];
                _ = ResolveContainedPath(root, entry.Path);
                if (entry.OriginalBase64 is not null) _ = Convert.FromBase64String(entry.OriginalBase64);
                if (entry.Delete)
                {
                    if (entry.TemporaryName is not null || entry.NewRevision is not null)
                        throw new InvalidOperationException("A delete journal entry has replacement data.");
                }
                else if (entry.TemporaryName is null || entry.NewRevision is null || entry.NewRevision.Length != 64 ||
                    !string.Equals(entry.TemporaryName, Path.GetFileName(entry.TemporaryName), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("A replacement journal entry is invalid.");
                }
            }
            return journal;
        }
        catch (Exception exception) when (exception is JsonException or IOException or InvalidOperationException or FormatException or ArgumentException or TranslationAuthoringException)
        {
            throw new TranslationAuthoringException("The pending transaction recovery journal is invalid.", exception);
        }
    }

    private static void Complete(string root, TransactionJournal journal)
    {
        for (int index = 0; index < journal.Entries.Length; index++)
        {
            JournalEntry entry = journal.Entries[index];
            string target = ResolveContainedPath(root, entry.Path);
            if (entry.Delete)
            {
                if (!File.Exists(target)) continue;
                RequireRevision(target, OriginalRevision(entry), entry.Path);
                File.Delete(target);
                continue;
            }
            if (File.Exists(target) && string.Equals(Revision(File.ReadAllBytes(target)), entry.NewRevision, StringComparison.Ordinal))
                continue;
            string? originalRevision = OriginalRevision(entry);
            if (originalRevision is null)
            {
                if (File.Exists(target)) throw new InvalidOperationException($"'{entry.Path}' changed after the interruption.");
            }
            else
            {
                RequireRevision(target, originalRevision, entry.Path);
            }
            string temporary = Path.Combine(Path.GetDirectoryName(target)!, entry.TemporaryName!);
            if (!File.Exists(temporary))
                throw new InvalidOperationException($"The staged replacement for '{entry.Path}' is missing.");
            File.Move(temporary, target, true);
        }
    }

    private static void Rollback(string root, TransactionJournal journal)
    {
        for (int index = journal.Entries.Length - 1; index >= 0; index--)
        {
            JournalEntry entry = journal.Entries[index];
            string target = ResolveContainedPath(root, entry.Path);
            if (entry.OriginalBase64 is null)
            {
                if (File.Exists(target))
                {
                    RequireRevision(target, entry.NewRevision, entry.Path);
                    File.Delete(target);
                }
                continue;
            }
            byte[] original = Convert.FromBase64String(entry.OriginalBase64);
            if (File.Exists(target))
            {
                string current = Revision(File.ReadAllBytes(target));
                string originalRevision = Revision(original);
                if (string.Equals(current, originalRevision, StringComparison.Ordinal)) continue;
                if (!entry.Delete && !string.Equals(current, entry.NewRevision, StringComparison.Ordinal))
                    throw new InvalidOperationException($"'{entry.Path}' changed after the interruption.");
            }
            else if (!entry.Delete)
            {
                throw new InvalidOperationException($"'{entry.Path}' changed after the interruption.");
            }
            string rollback = Path.Combine(Path.GetDirectoryName(target)!, $".{Path.GetFileName(target)}.runic-rollback-{Guid.NewGuid():N}.tmp");
            WriteFile(rollback, original, FileMode.CreateNew);
            File.Move(rollback, target, true);
        }
    }

    private static void Apply(string root, JournalEntry entry)
    {
        string target = ResolveContainedPath(root, entry.Path);
        if (entry.Delete)
        {
            File.Delete(target);
            return;
        }
        string temporary = Path.Combine(Path.GetDirectoryName(target)!, entry.TemporaryName!);
        File.Move(temporary, target, true);
    }

    private static void Cleanup(string root, TransactionJournal journal, string journalPath)
    {
        for (int index = 0; index < journal.Entries.Length; index++)
        {
            JournalEntry entry = journal.Entries[index];
            if (entry.TemporaryName is null) continue;
            string target = ResolveContainedPath(root, entry.Path);
            TryDelete(Path.Combine(Path.GetDirectoryName(target)!, entry.TemporaryName));
        }
        TryDelete(journalPath);
    }

    private static void ValidateExpectedRevision(TranslationWorkspaceEdit edit, byte[]? original)
    {
        string? actual = original is null ? null : Revision(original);
        if (!string.Equals(edit.ExpectedRevision, actual, StringComparison.Ordinal))
            throw new TranslationAuthoringException($"'{edit.RelativePath}' changed after the operation was planned.");
    }

    private static void ValidateEditKind(TranslationWorkspaceEdit edit, bool exists)
    {
        bool valid = edit.Kind switch
        {
            TranslationWorkspaceEditKind.Create => !exists && edit.Bytes is not null,
            TranslationWorkspaceEditKind.Replace => exists && edit.Bytes is not null,
            TranslationWorkspaceEditKind.Delete => exists && edit.Bytes is null,
            _ => false,
        };
        if (!valid) throw new TranslationAuthoringException($"Edit '{edit.RelativePath}' is inconsistent with the current workspace.");
    }

    private static void RejectDuplicatePaths(TranslationWorkspaceEdit[] edits)
    {
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        for (int index = 0; index < edits.Length; index++)
        {
            if (!paths.Add(NormalizePath(edits[index].RelativePath)))
                throw new TranslationAuthoringException($"Transaction path '{edits[index].RelativePath}' is declared more than once.");
        }
    }

    private static string RequireRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        string fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot)) throw new TranslationAuthoringException($"Workspace '{fullRoot}' does not exist.");
        if ((File.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0)
            throw new TranslationAuthoringException($"Workspace root '{fullRoot}' is a symbolic link or reparse point.");
        return fullRoot;
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new TranslationAuthoringException("Transaction paths must be non-empty relative paths.");
        string normalized = NormalizePath(relativePath);
        string fullPath = Path.GetFullPath(normalized.Replace('/', Path.DirectorySeparatorChar), root);
        string boundary = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(boundary, PathComparison))
            throw new TranslationAuthoringException($"Transaction path '{relativePath}' escapes the workspace.");

        string? parent = Path.GetDirectoryName(fullPath);
        if (parent is null || !Directory.Exists(parent))
            throw new TranslationAuthoringException($"Parent directory for transaction path '{relativePath}' does not exist.");
        var current = new DirectoryInfo(parent);
        while (!string.Equals(current.FullName, root, PathComparison))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new TranslationAuthoringException($"Transaction path '{relativePath}' crosses a symbolic link or reparse point.");
            current = current.Parent
                ?? throw new TranslationAuthoringException($"Transaction path '{relativePath}' escapes the workspace.");
        }
        if (File.Exists(fullPath) && (File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new TranslationAuthoringException($"Transaction path '{relativePath}' is a symbolic link or reparse point.");
        return fullPath;
    }

    private static void WriteFile(string path, byte[] bytes, FileMode mode)
    {
        using var stream = new FileStream(path, mode, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').TrimStart('/');
    private static string Revision(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string? OriginalRevision(JournalEntry entry) =>
        entry.OriginalBase64 is null ? null : Revision(Convert.FromBase64String(entry.OriginalBase64));
    private static void RequireRevision(string path, string? expected, string displayPath)
    {
        if (expected is null || !File.Exists(path) ||
            !string.Equals(Revision(File.ReadAllBytes(path)), expected, StringComparison.Ordinal))
            throw new InvalidOperationException($"'{displayPath}' changed after the interruption.");
    }
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal sealed record TransactionJournal(int Version, string Root, string CatalogId, JournalEntry[] Entries);
    internal sealed record JournalEntry(string Path, string? TemporaryName, string? OriginalBase64, bool Delete, string? NewRevision);
    private sealed class SimulatedInterruptionException : Exception;
}
