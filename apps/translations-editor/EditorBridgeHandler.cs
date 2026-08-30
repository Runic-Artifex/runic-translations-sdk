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
    public async ValueTask<Contract.ApplicationInitialized> InitializeApplicationAsync(
        Contract.InitializeApplication command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "ApplicationInitialized",
        Snapshot = ApplicationInitializedSnapshotValue(
            await session.LoadAsync(cancellationToken).ConfigureAwait(false)),
    };

    public async ValueTask<Contract.WorkspaceLoaded> LoadWorkspaceAsync(
        Contract.LoadWorkspace command,
        BridgeCommandContext context,
        CancellationToken cancellationToken) => new()
    {
        Tag = "WorkspaceLoaded",
        Snapshot = WorkspaceLoadedSnapshotValue(
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
                Message = value.Message,
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
                Message = value.Message,
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
                            $"The review entry '{entry.Key}' ({entry.Locale}) lists the sample key '{sample.Key}' more than once.",
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
                Message = value.Message,
                Review = value.Review is null ? null : ReviewSavedResultReviewValue(value.Review),
                History = value.History is null ? null : new Contract.ReviewSavedResultHistory
                {
                    CanUndo = value.History.CanUndo,
                    CanRedo = value.History.CanRedo,
                    UndoLabel = value.History.UndoLabel,
                    RedoLabel = value.History.RedoLabel,
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
                Message = value.Message,
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
                Message = value.Message,
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
                Message = value.Message,
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
                Message = value.Message,
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
                    Message = value.Message,
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
                Message = value.Message,
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
                Message = value.Message,
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
                    Message = value.Message,
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
        value.Layer,
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
        value.Layer,
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
        value.LayerName,
        value.GenerateEsm,
        value.IncludeStarterMessage);

    private static EditorProjectCreationRequest ProjectRequest(Contract.CreateProjectRequest value) => new(
        value.Directory,
        value.CatalogId,
        value.DefaultLocale,
        value.AdditionalLocales.Select(static locale => new EditorProjectLocaleRequest(locale.Tag, locale.Fallback)).ToArray(),
        value.CodeNamespace,
        value.ClassName,
        value.LayerName,
        value.GenerateEsm,
        value.IncludeStarterMessage);
private static Contract.ApplicationInitializedSnapshotReview ApplicationInitializedSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.ApplicationInitializedSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.ApplicationInitializedSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.ApplicationInitializedSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };

    private static Contract.ApplicationInitializedSnapshot ApplicationInitializedSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : ApplicationInitializedSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.ApplicationInitializedSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.ApplicationInitializedSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.ApplicationInitializedSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.ApplicationInitializedSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : ApplicationInitializedSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.ApplicationInitializedSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.ApplicationInitializedSnapshotCatalog ApplicationInitializedSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.ApplicationInitializedSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.ApplicationInitializedSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.WorkspaceLoadedSnapshotReview WorkspaceLoadedSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.WorkspaceLoadedSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.WorkspaceLoadedSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.WorkspaceLoadedSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };

    private static Contract.WorkspaceLoadedSnapshot WorkspaceLoadedSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : WorkspaceLoadedSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.WorkspaceLoadedSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.WorkspaceLoadedSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.WorkspaceLoadedSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.WorkspaceLoadedSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : WorkspaceLoadedSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.WorkspaceLoadedSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.WorkspaceLoadedSnapshotCatalog WorkspaceLoadedSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.WorkspaceLoadedSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.WorkspaceLoadedSnapshotCatalogLayersItem
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
private static Contract.MutationAppliedResultSnapshotReview MutationAppliedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.MutationAppliedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.MutationAppliedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.MutationAppliedResultSnapshotReviewTerminologyItem
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
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : MutationAppliedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.MutationAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.MutationAppliedResultSnapshot MutationAppliedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : MutationAppliedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.MutationAppliedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.MutationAppliedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.MutationAppliedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.MutationAppliedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : MutationAppliedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.MutationAppliedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.MutationAppliedResultSnapshotCatalog MutationAppliedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.MutationAppliedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.MutationAppliedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.TransactionRecoveredResultSnapshotReview TransactionRecoveredResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.TransactionRecoveredResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.TransactionRecoveredResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.TransactionRecoveredResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.TransactionRecoveredResult TransactionRecoveredResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : TransactionRecoveredResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.TransactionRecoveredResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.TransactionRecoveredResultSnapshot TransactionRecoveredResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : TransactionRecoveredResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.TransactionRecoveredResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.TransactionRecoveredResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.TransactionRecoveredResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.TransactionRecoveredResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : TransactionRecoveredResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.TransactionRecoveredResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.TransactionRecoveredResultSnapshotCatalog TransactionRecoveredResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.TransactionRecoveredResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.TransactionRecoveredResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.UndoAppliedResultSnapshotReview UndoAppliedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.UndoAppliedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.UndoAppliedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.UndoAppliedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.UndoAppliedResult UndoAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : UndoAppliedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.UndoAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.UndoAppliedResultSnapshot UndoAppliedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : UndoAppliedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.UndoAppliedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.UndoAppliedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.UndoAppliedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.UndoAppliedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : UndoAppliedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.UndoAppliedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.UndoAppliedResultSnapshotCatalog UndoAppliedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.UndoAppliedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.UndoAppliedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.RedoAppliedResultSnapshotReview RedoAppliedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.RedoAppliedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.RedoAppliedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.RedoAppliedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.RedoAppliedResult RedoAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : RedoAppliedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.RedoAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.RedoAppliedResultSnapshot RedoAppliedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : RedoAppliedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.RedoAppliedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.RedoAppliedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.RedoAppliedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.RedoAppliedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : RedoAppliedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.RedoAppliedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.RedoAppliedResultSnapshotCatalog RedoAppliedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.RedoAppliedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.RedoAppliedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.DocumentSavedResultSnapshotReview DocumentSavedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.DocumentSavedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.DocumentSavedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.DocumentSavedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.DocumentSavedResult DocumentSavedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : DocumentSavedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.DocumentSavedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.DocumentSavedResultSnapshot DocumentSavedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : DocumentSavedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.DocumentSavedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.DocumentSavedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.DocumentSavedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.DocumentSavedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : DocumentSavedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.DocumentSavedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.DocumentSavedResultSnapshotCatalog DocumentSavedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.DocumentSavedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.DocumentSavedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.ProjectCreatedResultSnapshotReview ProjectCreatedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.ProjectCreatedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.ProjectCreatedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.ProjectCreatedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.ProjectCreatedResult ProjectCreatedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : ProjectCreatedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.ProjectCreatedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.ProjectCreatedResultSnapshot ProjectCreatedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : ProjectCreatedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.ProjectCreatedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.ProjectCreatedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.ProjectCreatedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.ProjectCreatedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : ProjectCreatedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.ProjectCreatedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.ProjectCreatedResultSnapshotCatalog ProjectCreatedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.ProjectCreatedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.ProjectCreatedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

private static Contract.WorkspaceOpenedResultSnapshotReview WorkspaceOpenedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.WorkspaceOpenedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.WorkspaceOpenedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.WorkspaceOpenedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };
    private static Contract.WorkspaceOpenedResult WorkspaceOpenedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : WorkspaceOpenedResultSnapshotValue(value.Snapshot),
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
                }).ToArray(),
        },
        History = value.History is null ? null : new Contract.WorkspaceOpenedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };


    private static Contract.WorkspaceOpenedResultSnapshot WorkspaceOpenedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : WorkspaceOpenedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.WorkspaceOpenedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.WorkspaceOpenedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.WorkspaceOpenedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.WorkspaceOpenedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : WorkspaceOpenedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.WorkspaceOpenedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.WorkspaceOpenedResultSnapshotCatalog WorkspaceOpenedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.WorkspaceOpenedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.WorkspaceOpenedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResult XliffImportAppliedResultValue(EditorOperationResult value) => new()
    {
        Ok = value.Ok,
        Kind = value.Kind,
        Message = value.Message,
        Snapshot = value.Snapshot is null ? null : XliffImportAppliedResultSnapshotValue(value.Snapshot),
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
            }).ToArray(),
        },
        History = value.History is null ? null : new Contract.XliffImportAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

private static Contract.XliffImportAppliedResultSnapshotReview XliffImportAppliedResultSnapshotReviewValue(EditorReviewSnapshot value) => new()
    {
        Path = value.Path,
        Revision = value.Revision,
        Error = value.Error,
        Entries = value.Entries.Select(static value => new Contract.XliffImportAppliedResultSnapshotReviewEntriesItem
        {
            Key = value.Key,
            Locale = value.Locale,
            State = value.State,
            Note = value.Note,
            SourceFingerprint = value.SourceFingerprint,
            Samples = value.Samples.OrderBy(static sample => sample.Key, StringComparer.Ordinal)
                .Select(static sample => new Contract.XliffImportAppliedResultSnapshotReviewEntriesItemSamplesItem
                {
                    Key = sample.Key,
                    Value = sample.Value,
                }).ToArray(),
        }).ToArray(),
        Terminology = value.Terminology.Select(static value => new Contract.XliffImportAppliedResultSnapshotReviewTerminologyItem
        {
            Source = value.Source,
            Preferred = value.Preferred,
            Locale = value.Locale,
            Note = value.Note,
        }).ToArray(),
    };

    private static Contract.XliffImportAppliedResultSnapshot XliffImportAppliedResultSnapshotValue(WorkspaceSnapshot value) => new()
    {
        Root = value.Root,
        Catalog = value.Catalog is null ? null : XliffImportAppliedResultSnapshotCatalog(value.Catalog),
        Catalogs = value.Catalogs.Select(static value => new Contract.XliffImportAppliedResultSnapshotCatalogsItem
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
        Documents = value.Documents.Select(static value => new Contract.XliffImportAppliedResultSnapshotDocumentsItem
        {
            Path = value.Path,
            Content = value.Content,
            Revision = value.Revision,
            IsManifest = value.IsManifest,
            IsMalformed = value.IsMalformed,
            Locale = value.Locale,
            Layer = value.Layer,
        }).ToArray(),
        Diagnostics = value.Diagnostics.Select(static value => new Contract.XliffImportAppliedResultSnapshotDiagnosticsItem
                {
                    Id = value.Id,
                    Severity = value.Severity,
                    Message = value.Message,
                    Path = value.Path,
                    Line = value.Line,
                    Column = value.Column,
                    EndLine = value.EndLine,
                    EndColumn = value.EndColumn,
                }).ToArray(),
        Success = value.Success,
        PendingTransaction = value.PendingTransaction is null
            ? null
            : new Contract.XliffImportAppliedResultSnapshotPendingTransaction
            {
                CatalogId = value.PendingTransaction.CatalogId,
                Paths = value.PendingTransaction.Paths.ToArray(),
            },
        Review = value.Review is null ? null : XliffImportAppliedResultSnapshotReviewValue(value.Review),
        History = value.History is null ? null : new Contract.XliffImportAppliedResultSnapshotHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
        },
    };

    private static Contract.XliffImportAppliedResultSnapshotCatalog XliffImportAppliedResultSnapshotCatalog(EditorCatalog value) => new()
    {
        Id = value.Id,
        SchemaVersion = value.SchemaVersion,
        DefaultLocale = value.DefaultLocale,
        Locales = value.Locales.Select(static value => new Contract.XliffImportAppliedResultSnapshotCatalogLocalesItem
        {
            Tag = value.Tag,
            Fallback = value.Fallback,
        }).ToArray(),
        Layers = value.Layers.Select(static value => new Contract.XliffImportAppliedResultSnapshotCatalogLayersItem
        {
            Name = value.Name,
            Priority = value.Priority,
        }).ToArray(),
    };

    private static Contract.ReviewJsonImportAppliedResult ReviewJsonImportAppliedResultValue(EditorReviewOperationResult value) => new()
    {
        Ok = value.Ok,
        Message = value.Message,
        Review = value.Review is null ? null : ReviewJsonImportAppliedResultReviewValue(value.Review),
        History = value.History is null ? null : new Contract.ReviewJsonImportAppliedResultHistory
        {
            CanUndo = value.History.CanUndo,
            CanRedo = value.History.CanRedo,
            UndoLabel = value.History.UndoLabel,
            RedoLabel = value.History.RedoLabel,
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
        Message = value.Message,
    };

    private static Contract.DiagnosticBundleDeletedResult DiagnosticBundleDeletedResultValue(EditorDiagnosticBundleActionResult value) => new()
    {
        Ok = value.Ok,
        Message = value.Message,
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

}
