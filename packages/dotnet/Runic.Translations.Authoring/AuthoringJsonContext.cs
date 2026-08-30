using System.Text.Json.Serialization;

namespace Runic.Translations.Authoring;

[JsonSerializable(typeof(TranslationWorkspaceTransaction.TransactionJournal))]
internal sealed partial class AuthoringJsonContext : JsonSerializerContext;
