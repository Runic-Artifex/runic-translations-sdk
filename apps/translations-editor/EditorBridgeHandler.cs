using Runic.Application.Bridge;
using Contract = Runic.Translations.Editor.Contract;

namespace Runic.Translations.Editor;

/// <summary>
/// Routes typed Application Bridge commands to <see cref="EditorSession"/>,
/// preserving the operations, results, events, and payload semantics of the
/// former string-named window bindings byte-for-byte.
/// </summary>
internal sealed class EditorBridgeHandler(EditorSession session) : Contract.IEditorBridgeHandler
{
    public async ValueTask<Contract.WorkspaceSnapshot> GetSnapshotAsync(
        BridgeSnapshotContext context,
        CancellationToken cancellationToken) => WorkspaceSnapshotValue(
            await session.LoadAsync(cancellationToken).ConfigureAwait(false));

    public async ValueTask<Contract.WorkspaceLoaded> LoadWorkspaceAsync(
        Contract.LoadWorkspace command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "WorkspaceLoaded",
        Snapshot = WorkspaceSnapshotValue(
            await session.LoadAsync(cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.ExternalChangesChecked> CheckExternalChangesAsync(
        Contract.CheckExternalChanges command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorExternalChanges value = await session.CheckExternalChangesAsync(cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "ExternalChangesChecked",
            Changes = new Contract.ExternalChangesCheckedChanges
            {
                Overflowed = value.Overflowed,
                Paths = value.Paths.ToArray(),
                Changes = value.Changes.Select(static value => new Contract.ExternalChangesCheckedChangesChangesItem
                {
                    Path = value.Path,
                    Exists = value.Exists,
                    Content = value.Content,
                    Revision = value.Revision,
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.WorkspacePicked> PickWorkspaceAsync(
        Contract.PickWorkspace command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorWorkspacePickerResult value = await EditorWorkspacePicker.PickAsync(cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "WorkspacePicked",
            Result = new Contract.WorkspacePickedResult
            {
                Ok = value.Ok,
                Cancelled = value.Cancelled,
                Directory = value.Directory,
                Message = value.Message is null ? null : WorkspacePickedResultMessageValue(value.Message),
            },
        };
    }

    public ValueTask<Contract.MutationPreviewed> PreviewMutationAsync(
        Contract.PreviewMutation command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorMutationPreview value = session.PreviewMutation(MutationRequest(command.Request));
        return ValueTask.FromResult(new Contract.MutationPreviewed
        {
            Tag = "MutationPreviewed",
            Preview = new Contract.MutationPreviewedPreview
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : MutationPreviewedPreviewMessageValue(value.Message),
                Files = value.Files.Select(static value => new Contract.MutationPreviewedPreviewFilesItem
                {
                    Path = value.Path,
                    Kind = value.Kind,
                    BeforeBytes = value.BeforeBytes,
                    AfterBytes = value.AfterBytes,
                }).ToArray(),
                RequiresIrreversibleConfirmation = value.RequiresIrreversibleConfirmation,
                ConfirmationToken = value.ConfirmationToken,
            },
        });
    }

    public async ValueTask<Contract.MutationApplied> ApplyMutationAsync(
        Contract.ApplyMutation command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "MutationApplied",
        Result = MutationAppliedResultValue(await session.ApplyMutationAsync(
            MutationRequest(command.Request),
            cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.TransactionRecovered> RecoverTransactionAsync(
        Contract.RecoverTransaction command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "TransactionRecovered",
        Result = TransactionRecoveredResultValue(await session.RecoverTransactionAsync(
            new EditorRecoveryRequest(command.Mode),
            cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.UndoApplied> UndoAsync(
        Contract.Undo command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "UndoApplied",
        Result = UndoAppliedResultValue(
            await session.UndoAsync(cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.RedoApplied> RedoAsync(
        Contract.Redo command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "RedoApplied",
        Result = RedoAppliedResultValue(
            await session.RedoAsync(cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.DocumentTransformed> TransformDocumentAsync(
        Contract.TransformDocument command, BridgeCommandContext context, CancellationToken cancellationToken)
    {
        EditorDocumentDraft value = await session.TransformDocumentAsync(command.Path, command.Content,
            command.Key, command.Value, cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "DocumentTransformed",
            Result = new Contract.DocumentTransformedResult
            {
                Success = value.Success,
                Content = value.Content,
                Entries = value.Entries.Select(static entry => new Contract.DocumentTransformedResultEntriesItem
                {
                    Key = entry.Key, Content = entry.Content,
                    ValueStartByte = entry.ValueStartByte, ValueLengthBytes = entry.ValueLengthBytes,
                }).ToArray(),
                Diagnostics = value.Diagnostics.Select(static diagnostic => new Contract.DocumentTransformedResultDiagnosticsItem
                {
                    Id = diagnostic.Id, Severity = diagnostic.Severity, Message = diagnostic.Message,
                    Path = diagnostic.Path, Line = diagnostic.Line, Column = diagnostic.Column,
                    EndLine = diagnostic.EndLine, EndColumn = diagnostic.EndColumn,
                    Notice = diagnostic.Notice is null ? null : DocumentTransformedResultDiagnosticsItemNoticeValue(diagnostic.Notice),
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.DocumentValidated> ValidateDocumentAsync(
        Contract.ValidateDocument command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        ValidationResult value = await session.ValidateAsync(
            command.Path,
            command.Content,
            cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "DocumentValidated",
            Result = new Contract.DocumentValidatedResult
            {
                Success = value.Success,
                Diagnostics = value.Diagnostics.Select(static value => new Contract.DocumentValidatedResultDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : DocumentValidatedResultDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.MessagePreviewed> PreviewMessageAsync(
        Contract.PreviewMessage command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorMessagePreview value = await session.PreviewMessageAsync(
            command.Path,
            command.Content,
            command.Locale,
            command.Key,
            cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "MessagePreviewed",
            Preview = new Contract.MessagePreviewedPreview
            {
                Success = value.Success,
                Locale = value.Locale,
                AstJson = value.AstJson,
                Diagnostics = value.Diagnostics.Select(static value => new Contract.MessagePreviewedPreviewDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : MessagePreviewedPreviewDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.DocumentSaved> SaveDocumentAsync(
        Contract.SaveDocument command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "DocumentSaved",
        Result = DocumentSavedResultValue(await session.SaveAsync(
            command.Path,
            command.Content,
            command.Revision,
            cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.ReviewSaved> SaveReviewAsync(
        Contract.SaveReview command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        foreach (Contract.SaveReviewRequestEntriesItem entry in command.Request.Entries)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Contract.SaveReviewRequestEntriesItemSamplesItem sample in entry.Samples)
            {
                if (seen.Add(sample.Key)) continue;
                return new()
                {
                    Tag = "ReviewSaved",
                    Result = new Contract.ReviewSavedResult
                    {
                        Ok = false,
                        Message =
                            ReviewSavedResultMessageValue(EditorNotice.Create("ui_backend_duplicate_sample", ("key", entry.Key), ("locale", entry.Locale), ("sample", sample.Key))),
                    },
                };
            }
        }
        EditorReviewOperationResult value = await session.SaveReviewAsync(
            ReviewSaveRequest(command.Request),
            cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "ReviewSaved",
            Result = new Contract.ReviewSavedResult
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : ReviewSavedResultMessageValue(value.Message),
                Review = value.Review is null ? null : ReviewSavedResultReviewValue(value.Review),
                History = value.History is null ? null : new Contract.ReviewSavedResultHistory
                {
                    CanUndo = value.History.CanUndo,
                    CanRedo = value.History.CanRedo,
                    UndoLabel = value.History.UndoLabel is null ? null : ReviewSavedResultHistoryUndoLabelValue(value.History.UndoLabel),
                    RedoLabel = value.History.RedoLabel is null ? null : ReviewSavedResultHistoryRedoLabelValue(value.History.RedoLabel),
                },
            },
        };
    }

    public ValueTask<Contract.AboutLoaded> AboutAsync(
        Contract.About command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorAbout value = EditorDiagnostics.About();
        return ValueTask.FromResult(new Contract.AboutLoaded
        {
            Tag = "AboutLoaded",
            About = new Contract.AboutLoadedAbout
            {
                Product = value.Product,
                Version = value.Version,
                UpdateChannel = value.UpdateChannel,
                Commit = value.Commit,
                Runtime = value.Runtime,
                RuntimeIdentifier = value.RuntimeIdentifier,
                OperatingSystem = value.OperatingSystem,
                Architecture = value.Architecture,
            },
        });
    }

    public async ValueTask<Contract.DiagnosticBundleCreated> CreateDiagnosticBundleAsync(
        Contract.CreateDiagnosticBundle command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorDiagnosticBundleResult value = await session.CreateDiagnosticBundleAsync(cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "DiagnosticBundleCreated",
            Result = new Contract.DiagnosticBundleCreatedResult
            {
                Ok = value.Ok,
                Path = value.Path,
                Message = value.Message is null ? null : DiagnosticBundleCreatedResultMessageValue(value.Message),
            },
        };
    }

    public ValueTask<Contract.DiagnosticBundleRevealed> RevealDiagnosticBundleAsync(
        Contract.RevealDiagnosticBundle command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new Contract.DiagnosticBundleRevealed
    {
        Tag = "DiagnosticBundleRevealed",
        Result = DiagnosticBundleActionValue(session.RevealDiagnosticBundle(command.Path)),
    });

    public ValueTask<Contract.DiagnosticBundleDeleted> DeleteDiagnosticBundleAsync(
        Contract.DeleteDiagnosticBundle command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new Contract.DiagnosticBundleDeleted
    {
        Tag = "DiagnosticBundleDeleted",
        Result = DiagnosticBundleDeletedResultValue(session.DeleteDiagnosticBundle(command.Path)),
    });

    public ValueTask<Contract.LocalStateLoaded> LoadLocalStateAsync(
        Contract.LoadLocalState command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new Contract.LocalStateLoaded
    {
        Tag = "LocalStateLoaded",
        State = LocalStateValue(session.LoadLocalState()),
    });

    public ValueTask<Contract.LocalStateSaved> SaveLocalStateAsync(
        Contract.SaveLocalState command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => ValueTask.FromResult(new Contract.LocalStateSaved
    {
        Tag = "LocalStateSaved",
        State = LocalStateSavedValue(session.SaveLocalState(command.Entries
            .Select(static entry => new EditorLocalStateEntry(entry.Key, entry.Value)).ToArray())),
    });

    public ValueTask<Contract.LocalStateCleared> ClearLocalStateAsync(
        Contract.ClearLocalState command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorLocalStateClearResult value = session.ClearLocalState();
        return ValueTask.FromResult(new Contract.LocalStateCleared
        {
            Tag = "LocalStateCleared",
            Result = new Contract.LocalStateClearedResult
            {
                RemovedEntries = value.RemovedEntries,
                Recovered = value.Recovered,
            },
        });
    }

    public ValueTask<Contract.ProjectPreviewed> PreviewProjectAsync(
        Contract.PreviewProject command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorProjectPlan value = EditorSession.PreviewProject(ProjectRequest(command.Request));
        return ValueTask.FromResult(new Contract.ProjectPreviewed
        {
            Tag = "ProjectPreviewed",
            Plan = new Contract.ProjectPreviewedPlan
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : ProjectPreviewedPlanMessageValue(value.Message),
                Directory = value.Directory,
                CatalogId = value.CatalogId,
                Locales = value.Locales.Select(static value => new Contract.ProjectPreviewedPlanLocalesItem
                {
                    Tag = value.Tag,
                    Fallback = value.Fallback,
                }).ToArray(),
                Files = value.Files.ToArray(),
            },
        });
    }

    public async ValueTask<Contract.ProjectCreated> CreateProjectAsync(
        Contract.CreateProject command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "ProjectCreated",
        Result = ProjectCreatedResultValue(await session.CreateProjectAsync(
            ProjectRequest(command.Request),
            cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.WorkspaceOpened> OpenWorkspaceAsync(
        Contract.OpenWorkspace command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "WorkspaceOpened",
        Result = WorkspaceOpenedResultValue(await session.OpenWorkspaceAsync(
            new EditorOpenWorkspaceRequest(command.Request.Directory, command.Request.CatalogId),
            cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.XliffExported> ExportXliffAsync(
        Contract.ExportXliff command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorXliffExportResult value = await session.ExportXliffAsync(command.Directory, cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "XliffExported",
            Result = new Contract.XliffExportedResult
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : XliffExportedResultMessageValue(value.Message),
                CatalogId = value.CatalogId,
                Documents = value.Documents.Select(static value => new Contract.XliffExportedResultDocumentsItem
                {
                    Path = value.Path,
                    Locale = value.Locale,
                    ByteCount = value.ByteCount,
                }).ToArray(),
                Losses = value.Losses.Select(static value => new Contract.XliffExportedResultLossesItem
                {
                    Code = value.Code,
                    Location = value.Location,
                    Message = value.Message,
                    SemanticLoss = value.SemanticLoss,
                }).ToArray(),
                Lossless = value.Ok && value.Losses.All(static loss => !loss.SemanticLoss),
            },
        };
    }

    public async ValueTask<Contract.XliffImportPreviewed> PreviewXliffImportAsync(
        Contract.PreviewXliffImport command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorXliffImportPlan value = await session.PreviewXliffImportAsync(command.Path, cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "XliffImportPreviewed",
            Preview = new Contract.XliffImportPreviewedPreview
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : XliffImportPreviewedPreviewMessageValue(value.Message),
                RequiresIrreversibleConfirmation = value.Ok,
                ConfirmationToken = value.ConfirmationToken,
                CatalogId = value.CatalogId,
                SourceLocale = value.SourceLocale,
                TargetLocale = value.TargetLocale,
                Layer = value.Layer,
                Changes = value.Changes.Select(static value => new Contract.XliffImportPreviewedPreviewChangesItem
                {
                    Key = value.Key,
                    Kind = value.Kind,
                    Before = value.Before,
                    After = value.After,
                    StateBefore = value.StateBefore,
                    StateAfter = value.StateAfter,
                }).ToArray(),
                AddedCount = value.AddedCount,
                ChangedCount = value.ChangedCount,
                RemovedCount = value.RemovedCount,
                UnchangedCount = value.UnchangedCount,
                ReviewUpdateCount = value.ReviewUpdateCount,
                ChangesOverflowed = value.ChangesOverflowed,
                Refusals = value.Refusals.Select(static value => new Contract.XliffImportPreviewedPreviewRefusalsItem
                {
                    Code = value.Code,
                    Message = XliffImportPreviewedPreviewRefusalsItemMessageValue(value.Message),
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.XliffImportApplied> ApplyXliffImportAsync(
        Contract.ApplyXliffImport command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "XliffImportApplied",
        Result = XliffImportAppliedResultValue(await session.ApplyXliffImportAsync(
            command.ConfirmationToken, cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.ReviewJsonExported> ExportReviewJsonAsync(
        Contract.ExportReviewJson command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorReviewFileResult value = await session.ExportReviewJsonAsync(command.Path, cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "ReviewJsonExported",
            Result = new Contract.ReviewJsonExportedResult
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : ReviewJsonExportedResultMessageValue(value.Message),
                Path = value.Path,
                EntryCount = value.EntryCount,
            },
        };
    }

    public async ValueTask<Contract.ReviewJsonImportPreviewed> PreviewReviewJsonImportAsync(
        Contract.PreviewReviewJsonImport command,
        BridgeCommandContext context,
        CancellationToken cancellationToken)
    {
        EditorReviewImportPlan value = await session.PreviewReviewJsonImportAsync(command.Path, cancellationToken).ConfigureAwait(false);
        return new()
        {
            Tag = "ReviewJsonImportPreviewed",
            Preview = new Contract.ReviewJsonImportPreviewedPreview
            {
                Ok = value.Ok,
                Message = value.Message is null ? null : ReviewJsonImportPreviewedPreviewMessageValue(value.Message),
                RequiresIrreversibleConfirmation = value.Ok,
                ConfirmationToken = value.ConfirmationToken,
                CatalogId = value.CatalogId,
                Changes = value.Changes.Select(static value => new Contract.ReviewJsonImportPreviewedPreviewChangesItem
                {
                    Key = value.Key,
                    Locale = value.Locale,
                    Kind = value.Kind,
                    StateBefore = value.StateBefore,
                    StateAfter = value.StateAfter,
                }).ToArray(),
                AddedCount = value.AddedCount,
                ChangedCount = value.ChangedCount,
                RemovedCount = value.RemovedCount,
                ChangesOverflowed = value.ChangesOverflowed,
                Refusals = value.Refusals.Select(static value => new Contract.ReviewJsonImportPreviewedPreviewRefusalsItem
                {
                    Code = value.Code,
                    Message = ReviewJsonImportPreviewedPreviewRefusalsItemMessageValue(value.Message),
                }).ToArray(),
            },
        };
    }

    public async ValueTask<Contract.ReviewJsonImportApplied> ApplyReviewJsonImportAsync(
        Contract.ApplyReviewJsonImport command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "ReviewJsonImportApplied",
        Result = ReviewJsonImportAppliedResultValue(await session.ApplyReviewJsonImportAsync(
            command.ConfirmationToken, cancellationToken).ConfigureAwait(false)),
    };

    private static EditorMutationRequest MutationRequest(Contract.PreviewMutationRequest value) => new(
        value.Kind,
        value.Locale,
        value.Fallback,
        value.ReplacementFallback,
        value.CopyFromLocale,
        value.SourceKey,
        value.TargetKey,
        value.InitialValue,
        value.ConfirmationToken);

    private static EditorMutationRequest MutationRequest(Contract.ApplyMutationRequest value) => new(
        value.Kind,
        value.Locale,
        value.Fallback,
        value.ReplacementFallback,
        value.CopyFromLocale,
        value.SourceKey,
        value.TargetKey,
        value.InitialValue,
        value.ConfirmationToken);

    private static EditorReviewSaveRequest ReviewSaveRequest(Contract.SaveReviewRequest value) => new(
        value.ExpectedRevision,
        value.Entries.Select(static entry => new EditorReviewEntry(
            entry.Key,
            entry.Locale,
            entry.State,
            entry.Note,
            entry.SourceFingerprint,
            SampleMap(entry.Samples))).ToArray(),
        value.Terminology.Select(TerminologyEntry).ToArray());

    private static Dictionary<string, string> SampleMap(
        IReadOnlyList<Contract.SaveReviewRequestEntriesItemSamplesItem> samples)
    {
        Dictionary<string, string> result = new(samples.Count, StringComparer.Ordinal);
        foreach (Contract.SaveReviewRequestEntriesItemSamplesItem sample in samples)
            result[sample.Key] = sample.Value;
        return result;
    }

    private static EditorTerminologyEntry TerminologyEntry(Contract.SaveReviewRequestTerminologyItem value) =>
        new(value.Source, value.Preferred, value.Locale, value.Note);

    private static EditorProjectCreationRequest ProjectRequest(Contract.PreviewProjectRequest value) => new(
        value.Directory,
        value.CatalogId,
        value.DefaultLocale,
        value.AdditionalLocales.Select(static locale => new EditorProjectLocaleRequest(locale.Tag, locale.Fallback)).ToArray(),
        value.CodeNamespace,
        value.ClassName,
        value.IncludeStarterMessage);

    private static EditorProjectCreationRequest ProjectRequest(Contract.CreateProjectRequest value) => new(
        value.Directory,
        value.CatalogId,
        value.DefaultLocale,
        value.AdditionalLocales.Select(static locale => new EditorProjectLocaleRequest(locale.Tag, locale.Fallback)).ToArray(),
        value.CodeNamespace,
        value.ClassName,
        value.IncludeStarterMessage);
private static Contract.WorkspaceSnapshotReview WorkspaceSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.WorkspaceSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.WorkspaceSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.WorkspaceSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };

    private static Contract.WorkspaceSnapshot WorkspaceSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : WorkspaceSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.WorkspaceSnapshotCatalogsItem
        {
            Id = value.Id,
            ManifestPaths = value.ManifestPaths.ToArray(),
            DocumentCount = value.DocumentCount,
            LocaleCount = value.LocaleCount,
            MessageCount = value.MessageCount,
            ErrorCount = value.ErrorCount,
            WarningCount = value.WarningCount,
            Success = value.Success,
        }).ToArray(),
        Documents = value.Documents.Select(static value => new Contract.WorkspaceSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Entries = value.Entries?.Select(static entry => new Contract.WorkspaceSnapshotDocumentsItemEntriesItem
            {
                Key = entry.Key, Content = entry.Content,
                ValueStartByte = entry.ValueStartByte, ValueLengthBytes = entry.ValueLengthBytes,
            }).ToArray(),
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.WorkspaceSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : WorkspaceSnapshotDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.WorkspaceSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : WorkspaceSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.WorkspaceSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : WorkspaceSnapshotHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : WorkspaceSnapshotHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };

    private static Contract.WorkspaceSnapshotCatalog WorkspaceSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.WorkspaceSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.WorkspaceSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };




private static Contract.ReviewSavedResultReview ReviewSavedResultReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.ReviewSavedResultReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.ReviewSavedResultReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.ReviewSavedResultReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.MutationAppliedResult MutationAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : MutationAppliedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.MutationAppliedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.MutationAppliedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : MutationAppliedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.MutationAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : MutationAppliedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : MutationAppliedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.TransactionRecoveredResult TransactionRecoveredResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : TransactionRecoveredResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.TransactionRecoveredResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.TransactionRecoveredResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : TransactionRecoveredResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.TransactionRecoveredResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : TransactionRecoveredResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : TransactionRecoveredResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.UndoAppliedResult UndoAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : UndoAppliedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.UndoAppliedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.UndoAppliedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : UndoAppliedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.UndoAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : UndoAppliedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : UndoAppliedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.RedoAppliedResult RedoAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : RedoAppliedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.RedoAppliedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.RedoAppliedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : RedoAppliedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.RedoAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : RedoAppliedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : RedoAppliedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.DocumentSavedResult DocumentSavedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : DocumentSavedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.DocumentSavedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.DocumentSavedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : DocumentSavedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.DocumentSavedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : DocumentSavedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : DocumentSavedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.ProjectCreatedResult ProjectCreatedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : ProjectCreatedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.ProjectCreatedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.ProjectCreatedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : ProjectCreatedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.ProjectCreatedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : ProjectCreatedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : ProjectCreatedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.WorkspaceOpenedResult WorkspaceOpenedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : WorkspaceOpenedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.WorkspaceOpenedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.WorkspaceOpenedResultValidationDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : WorkspaceOpenedResultValidationDiagnosticsItemNoticeValue(value.Notice),
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.WorkspaceOpenedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : WorkspaceOpenedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : WorkspaceOpenedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.XliffImportAppliedResult XliffImportAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message is null ? null : XliffImportAppliedResultMessageValue(value.Message),
        Snapshot = value.Snapshot is null ? null : WorkspaceSnapshotValue(value.Snapshot),
        Validation = value.Validation is null ? null : new Contract.XliffImportAppliedResultValidation
        {
            Success = value.Validation.Success,
            Diagnostics = value.Validation.Diagnostics.Select(static value => new Contract.XliffImportAppliedResultValidationDiagnosticsItem
            {
                Id = value.Id,
                Severity = value.Severity,
                Message = value.Message,
                Path = value.Path,
                Line = value.Line,
                Column = value.Column,
                EndLine = value.EndLine,
                EndColumn = value.EndColumn,
                    Notice = value.Notice is null ? null : XliffImportAppliedResultValidationDiagnosticsItemNoticeValue(value.Notice),
            }).ToArray(),
        },
        History = value.History is null ? null : new Contract.XliffImportAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : XliffImportAppliedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : XliffImportAppliedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };




    private static Contract.ReviewJsonImportAppliedResult ReviewJsonImportAppliedResultValue(EditorReviewOperationResult value) => new()
    {
        Ok = value.Ok,
        Message = value.Message is null ? null : ReviewJsonImportAppliedResultMessageValue(value.Message),
        Review = value.Review is null ? null : ReviewJsonImportAppliedResultReviewValue(value.Review),
        History = value.History is null ? null : new Contract.ReviewJsonImportAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel is null ? null : ReviewJsonImportAppliedResultHistoryUndoLabelValue(value.History.UndoLabel),
            RedoLabel = value.History.RedoLabel is null ? null : ReviewJsonImportAppliedResultHistoryRedoLabelValue(value.History.RedoLabel),
        },
    };

private static Contract.ReviewJsonImportAppliedResultReview ReviewJsonImportAppliedResultReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.ReviewJsonImportAppliedResultReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.ReviewJsonImportAppliedResultReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.ReviewJsonImportAppliedResultReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };

    private static Contract.DiagnosticBundleRevealedResult DiagnosticBundleActionValue(EditorDiagnosticBundleActionResult value) => new()
    {
        Ok = value.Ok,
        Message = value.Message is null ? null : DiagnosticBundleRevealedResultMessageValue(value.Message),
    };

    private static Contract.DiagnosticBundleDeletedResult DiagnosticBundleDeletedResultValue(EditorDiagnosticBundleActionResult value) => new()
    {
        Ok = value.Ok,
        Message = value.Message is null ? null : DiagnosticBundleDeletedResultMessageValue(value.Message),
    };

    private static Contract.LocalStateLoadedState LocalStateValue(EditorLocalStateSnapshot value) => new()
    {
        Entries = value.Entries.Select(static entry => new Contract.LocalStateLoadedStateEntriesItem
        {
            Key = entry.Key,
            Value = entry.Value,
        }).ToArray(),
        Recovered = value.Recovered,
    };

    private static Contract.LocalStateSavedState LocalStateSavedValue(EditorLocalStateSnapshot value) => new()
    {
        Entries = value.Entries.Select(static entry => new Contract.LocalStateSavedStateEntriesItem
        {
            Key = entry.Key,
            Value = entry.Value,
        }).ToArray(),
        Recovered = value.Recovered,
    };


    private static Contract.DiagnosticBundleCreatedResultMessage DiagnosticBundleCreatedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DiagnosticBundleCreatedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DiagnosticBundleDeletedResultMessage DiagnosticBundleDeletedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DiagnosticBundleDeletedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DiagnosticBundleRevealedResultMessage DiagnosticBundleRevealedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DiagnosticBundleRevealedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentSavedResultHistoryRedoLabel DocumentSavedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentSavedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentSavedResultHistoryUndoLabel DocumentSavedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentSavedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentSavedResultMessage DocumentSavedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentSavedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentSavedResultValidationDiagnosticsItemNotice DocumentSavedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentSavedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentTransformedResultDiagnosticsItemNotice DocumentTransformedResultDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentTransformedResultDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.DocumentValidatedResultDiagnosticsItemNotice DocumentValidatedResultDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.DocumentValidatedResultDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MessagePreviewedPreviewDiagnosticsItemNotice MessagePreviewedPreviewDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MessagePreviewedPreviewDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MutationAppliedResultHistoryRedoLabel MutationAppliedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MutationAppliedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MutationAppliedResultHistoryUndoLabel MutationAppliedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MutationAppliedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MutationAppliedResultMessage MutationAppliedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MutationAppliedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MutationAppliedResultValidationDiagnosticsItemNotice MutationAppliedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MutationAppliedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.MutationPreviewedPreviewMessage MutationPreviewedPreviewMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.MutationPreviewedPreviewMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ProjectCreatedResultHistoryRedoLabel ProjectCreatedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ProjectCreatedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ProjectCreatedResultHistoryUndoLabel ProjectCreatedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ProjectCreatedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ProjectCreatedResultMessage ProjectCreatedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ProjectCreatedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ProjectCreatedResultValidationDiagnosticsItemNotice ProjectCreatedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ProjectCreatedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ProjectPreviewedPlanMessage ProjectPreviewedPlanMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ProjectPreviewedPlanMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.RedoAppliedResultHistoryRedoLabel RedoAppliedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.RedoAppliedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.RedoAppliedResultHistoryUndoLabel RedoAppliedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.RedoAppliedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.RedoAppliedResultMessage RedoAppliedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.RedoAppliedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.RedoAppliedResultValidationDiagnosticsItemNotice RedoAppliedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.RedoAppliedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonExportedResultMessage ReviewJsonExportedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonExportedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportAppliedResultHistoryRedoLabel ReviewJsonImportAppliedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonImportAppliedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportAppliedResultHistoryUndoLabel ReviewJsonImportAppliedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonImportAppliedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportAppliedResultMessage ReviewJsonImportAppliedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonImportAppliedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportPreviewedPreviewMessage ReviewJsonImportPreviewedPreviewMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonImportPreviewedPreviewMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportPreviewedPreviewRefusalsItemMessage ReviewJsonImportPreviewedPreviewRefusalsItemMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewJsonImportPreviewedPreviewRefusalsItemMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewSavedResultHistoryRedoLabel ReviewSavedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewSavedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewSavedResultHistoryUndoLabel ReviewSavedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewSavedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.ReviewSavedResultMessage ReviewSavedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.ReviewSavedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.TransactionRecoveredResultHistoryRedoLabel TransactionRecoveredResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.TransactionRecoveredResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.TransactionRecoveredResultHistoryUndoLabel TransactionRecoveredResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.TransactionRecoveredResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.TransactionRecoveredResultMessage TransactionRecoveredResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.TransactionRecoveredResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.TransactionRecoveredResultValidationDiagnosticsItemNotice TransactionRecoveredResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.TransactionRecoveredResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.UndoAppliedResultHistoryRedoLabel UndoAppliedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.UndoAppliedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.UndoAppliedResultHistoryUndoLabel UndoAppliedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.UndoAppliedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.UndoAppliedResultMessage UndoAppliedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.UndoAppliedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.UndoAppliedResultValidationDiagnosticsItemNotice UndoAppliedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.UndoAppliedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceOpenedResultHistoryRedoLabel WorkspaceOpenedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceOpenedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceOpenedResultHistoryUndoLabel WorkspaceOpenedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceOpenedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceOpenedResultMessage WorkspaceOpenedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceOpenedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceOpenedResultValidationDiagnosticsItemNotice WorkspaceOpenedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceOpenedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspacePickedResultMessage WorkspacePickedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspacePickedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceSnapshotDiagnosticsItemNotice WorkspaceSnapshotDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceSnapshotDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceSnapshotHistoryRedoLabel WorkspaceSnapshotHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceSnapshotHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.WorkspaceSnapshotHistoryUndoLabel WorkspaceSnapshotHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.WorkspaceSnapshotHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffExportedResultMessage XliffExportedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffExportedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResultHistoryRedoLabel XliffImportAppliedResultHistoryRedoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportAppliedResultHistoryRedoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResultHistoryUndoLabel XliffImportAppliedResultHistoryUndoLabelValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportAppliedResultHistoryUndoLabelArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResultMessage XliffImportAppliedResultMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportAppliedResultMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResultValidationDiagnosticsItemNotice XliffImportAppliedResultValidationDiagnosticsItemNoticeValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportAppliedResultValidationDiagnosticsItemNoticeArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportPreviewedPreviewMessage XliffImportPreviewedPreviewMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportPreviewedPreviewMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };

    private static Contract.XliffImportPreviewedPreviewRefusalsItemMessage XliffImportPreviewedPreviewRefusalsItemMessageValue(EditorNotice value) => new()
    {
        Code = value.Code,
        Detail = value.Detail,
        Args = value.Args.Select(static arg => new Contract.XliffImportPreviewedPreviewRefusalsItemMessageArgsItem
        {
            Name = arg.Name, Value = arg.Value, Number = arg.Number.HasValue ? new BridgeOptional<double>(arg.Number.Value) : default,
        }).ToArray(),
    };
}
