using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Runic.Translations.Tooling;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(SourceV3MigrationReportJson))]
[JsonSerializable(typeof(IReadOnlyList<SourceMigrationLoss>))]
internal sealed partial class ToolingJsonContext : JsonSerializerContext;
