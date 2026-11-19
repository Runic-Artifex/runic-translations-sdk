using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Runic.Translations.Authoring;
using Runic.Translations.Compiler;
using Runic.Translations.Tooling;

namespace Runic.Translations.Tool;

// Background validation (#271 B). The request worker owns every piece of server state; a validation of a project's
// catalog runs on one background thread instead, so requests are answered while it runs.
//
// - The worker schedules a project with an immutable snapshot of its buffers (WorkspaceSnapshot) and a generation that
//   it raises with every schedule. A newer schedule of the project replaces a waiting one and cancels a running one.
// - The validator builds the workspace and the review view from the snapshot alone (BuildWorkspace, BuildReviewView)
//   and queues the outcome for the worker, as a message in the worker's own queue.
// - The worker applies an outcome only while it is the project's latest generation and no newer change of the project
//   is pending: then the snapshot is the project's current state, and its diagnostics, review view and layout are
//   current. Anything older is dropped. So diagnostics are published for the latest state only, a cancelled or
//   superseded validation leaves no trace in any cache, and requests never see a half-applied validation, because the
//   worker applies an outcome as one message.
internal sealed partial class Rmf2LanguageServer
{
    // The worker's queue message that applies finished validations; it is not a client method.
    internal const string ValidatedMethod = "$/runic/validated";
    // The name the test hook (beforeRequest) sees on the validator thread as a validation starts.
    internal const string ValidationHook = "$/runic/validating";

    private sealed record ValidationJob(string Project, long Generation, string[] Group, string CacheKey, WorkspaceSnapshot Snapshot);
    private sealed record ValidationOutcome(ValidationJob Job, ReviewView? View, bool IsDirect, Exception? Error);

    // Worker state: the latest generation scheduled per project, and what hover shows from the latest applied validation
    // per project, which it reads while a newer validation runs.
    private readonly Dictionary<string, long> _validationGenerations = new(PathComparer);
    private readonly Dictionary<string, HoverFacts> _lastHoverFacts = new(PathComparer);

    // Shared with the validator thread, under _validatorGate.
    private readonly object _validatorGate = new();
    private readonly List<ValidationJob> _scheduledValidations = new();
    private ValidationJob? _runningJob;
    private CancellationTokenSource? _runningCancellation;
    private Thread? _validator;
    private bool _validatorStopped;
    private readonly ConcurrentQueue<ValidationOutcome> _validationOutcomes = new();
    // Queues ValidatedMethod for the worker; set by Run.
    private Action? _enqueueValidated;

    // The compiler parallelizes a catalog's reading and lowering over the thread pool (CompilerParallel), with the
    // current task scheduler. A validation runs its compile on this scheduler, which runs at most one task fewer than
    // the machine has processors, so the request worker and the reader always find a free core while a validation
    // runs: on all of them, a hover after typing waited up to 200 ms for one (#271).
    private static readonly TaskScheduler ValidationScheduler =
        new ConcurrentExclusiveSchedulerPair(TaskScheduler.Default, Math.Max(1, Environment.ProcessorCount - 1)).ConcurrentScheduler;

    // Validations whose outcome is not applied yet, for tests that wait until diagnostics are settled.
    private int _outstandingValidations;
    internal bool ValidationsSettled => Volatile.Read(ref _outstandingValidations) == 0;

    // Worker: validates the project with its open buffers in the background.
    private void ScheduleValidation(string project, string[] group)
    {
        long generation = _validationGenerations[project] = _validationGenerations.GetValueOrDefault(project) + 1;
        var job = new ValidationJob(project, generation, group, ReviewCacheKey(project, group), SnapshotOf(project, group));
        lock (_validatorGate)
        {
            if (_validatorStopped) return;
            int replaced = _scheduledValidations.RemoveAll(item => PathComparer.Equals(item.Project, job.Project));
            _scheduledValidations.Add(job);
            Interlocked.Add(ref _outstandingValidations, 1 - replaced);
            if (_runningJob is { } running && PathComparer.Equals(running.Project, project)) _runningCancellation!.Cancel();
            if (_validator is null)
            {
                _validator = new Thread(ValidateInBackground) { IsBackground = true, Name = "Runic RMF2 validation" };
                _validator.Start();
            }
            Monitor.PulseAll(_validatorGate);
        }
    }

    // Worker: a change of the project (of every project, for null) makes its waiting or running validation useless; the
    // change schedules a new one once FlushDiagnostics runs.
    private void CancelValidation(string? project)
    {
        lock (_validatorGate)
        {
            int removed = _scheduledValidations.RemoveAll(item => project is null || PathComparer.Equals(item.Project, project));
            Interlocked.Add(ref _outstandingValidations, -removed);
            if (_runningJob is { } running && (project is null || PathComparer.Equals(running.Project, project))) _runningCancellation!.Cancel();
        }
    }

    private void StopValidator()
    {
        lock (_validatorGate)
        {
            _validatorStopped = true;
            _scheduledValidations.Clear();
            _runningCancellation?.Cancel();
            Monitor.PulseAll(_validatorGate);
        }
    }

    private void ValidateInBackground()
    {
        while (true)
        {
            ValidationJob job;
            CancellationTokenSource cancellation = new();
            lock (_validatorGate)
            {
                while (_scheduledValidations.Count == 0 && !_validatorStopped) Monitor.Wait(_validatorGate);
                if (_validatorStopped) { cancellation.Dispose(); return; }
                job = _scheduledValidations[0];
                _scheduledValidations.RemoveAt(0);
                _runningJob = job;
                _runningCancellation = cancellation;
            }
            ValidationOutcome? outcome = null;
            try
            {
                Interlocked.Increment(ref _validations);
                _beforeRequest?.Invoke(ValidationHook);
                // On the validation scheduler, so the compiler's parallel loops leave a core to the request worker.
                outcome = Task.Factory.StartNew(() => {
                    // The buffers' own syntax (Buffer.Syntax, parsed once on first use): the diagnostics publication, the
                    // outline and semantic tokens need it; parsed here, off the request worker, which then finds it ready.
                    foreach ((_, Buffer buffer) in job.Snapshot.Buffers) _ = buffer.Syntax;
                    (Rmf2Workspace workspace, _) = BuildWorkspace(job.Snapshot, cancellation.Token);
                    ReviewView view = BuildReviewView(job.Project, workspace, cancellation.Token);
                    // Off the worker: what hover will keep of this validation once a newer one runs.
                    _ = view.Hover;
                    return new ValidationOutcome(job, view, workspace.IsDirect, null);
                }, cancellation.Token, TaskCreationOptions.None, ValidationScheduler).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            // Any other failure goes to the worker, which reports it as validation failures always were, or rethrows it.
            catch (Exception error) { outcome = new ValidationOutcome(job, null, false, error); }
            finally
            {
                lock (_validatorGate)
                {
                    // Queued before the job stops running, so a settling worker (SettleValidations) finds the outcome.
                    if (outcome is not null) _validationOutcomes.Enqueue(outcome);
                    else Interlocked.Decrement(ref _outstandingValidations);
                    _runningJob = null;
                    _runningCancellation = null;
                    Monitor.PulseAll(_validatorGate);
                }
                cancellation.Dispose();
            }
            if (outcome is null) continue;
            // The connection may close meanwhile; the outcome no longer matters then.
            try { _enqueueValidated?.Invoke(); }
            catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { return; }
        }
    }

    // Worker, with settleDiagnostics: waits for the scheduled and running validations and applies them, so a request is
    // answered after the diagnostics of every change before it, as when validation ran on the worker. Tests use it to
    // read diagnostics after a response; editors get diagnostics as validations finish.
    private void SettleValidations()
    {
        lock (_validatorGate)
            while (!_validatorStopped && (_scheduledValidations.Count > 0 || _runningJob is not null)) Monitor.Wait(_validatorGate);
        ApplyValidations();
    }

    // Worker: applies the finished validations that are still current, and drops the others.
    private void ApplyValidations()
    {
        while (_validationOutcomes.TryDequeue(out ValidationOutcome? outcome))
        {
            try
            {
                ValidationJob job = outcome.Job;
                if (_validationGenerations.GetValueOrDefault(job.Project) != job.Generation || _pendingValidations.Contains(job.Project) ||
                    _globalDiagnosticsRefreshPending) continue;
                PublishValidation(outcome);
            }
            finally { Interlocked.Decrement(ref _outstandingValidations); }
        }
    }

    private static bool IsValidationFailure(Exception error) => error is ToolUsageException or ToolDiagnosticException or UnauthorizedAccessException or
        InvalidOperationException or IOException or FormatException or OverflowException or TranslationFormatException or TranslationPackException or
        TranslationContractException or System.Text.Json.JsonException or TranslationAuthoringException;

    // Publishes a current validation for every project file: the open buffers and, from disk, the closed files that carry
    // diagnostics. A failure that is not a validation failure is rethrown on the worker, as before validation moved off it.
    private void PublishValidation(ValidationOutcome outcome)
    {
        (ValidationJob job, ReviewView? view, bool isDirect, Exception? error) = outcome;
        IReadOnlyList<TranslationDiagnostic> diagnostics;
        if (view is not null)
        {
            _projectLayouts[job.Project] = isDirect;
            _reviewViews[job.CacheKey] = view;
            _lastHoverFacts[job.Project] = view.Hover;
            diagnostics = [.. WithReviewDiagnostics(view), .. LibraryDiagnostics(job.Project, job.Group)];
            // A later failure after recovery is worth reporting again.
            foreach (string uri in job.Group) _reportedBufferFailures.Remove(uri);
        }
        else if (IsValidationFailure(error!))
        {
            foreach (string uri in job.Group) ReportBufferFailure(uri, error!);
            diagnostics = Array.Empty<TranslationDiagnostic>();
            if (TryGetBuffer(Path.Combine(job.Project, "runic.json"), out string? configUri, out Buffer? config))
                diagnostics = ConfigDiagnostics(new TranslationSource(LocalPath(configUri), Utf8.GetBytes(config.Text)));
            // The overrides of library catalogs are checked against the libraries, not the application's catalog.
            diagnostics = [.. diagnostics, .. LibraryDiagnostics(job.Project, job.Group)];
        }
        else
        {
            ExceptionDispatchInfo.Capture(error!).Throw();
            return;
        }
        PublishDiagnostics(diagnostics, job.Group, job.Project);
    }
}
