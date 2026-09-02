using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Runic.Translations.Tool;

internal static class SchemaResources
{
    private static readonly (string FileName, string ResourceName)[] Schemas =
    [
        ("locale-artifact-v1.schema.json", "Runic.Translations.Tool.Schemas.locale-artifact-v1.schema.json"),
        ("locale-artifact-v2.schema.json", "Runic.Translations.Tool.Schemas.locale-artifact-v2.schema.json"),
        ("external-pack-v1.schema.json", "Runic.Translations.Tool.Schemas.external-pack-v1.schema.json"),
        ("template-manifest-v1.schema.json", "Runic.Translations.Tool.Schemas.template-manifest-v1.schema.json"),
        ("asset-manifest-v1.schema.json", "Runic.Translations.Tool.Schemas.asset-manifest-v1.schema.json"),
        ("web-module-manifest-v1.schema.json", "Runic.Translations.Tool.Schemas.web-module-manifest-v1.schema.json"),
        ("message-ast-v2.schema.json", "Runic.Translations.Tool.Schemas.message-ast-v2.schema.json"),
        ("locale-pack-v2.schema.json", "Runic.Translations.Tool.Schemas.locale-pack-v2.schema.json"),
        ("editor-state-v1.schema.json", "Runic.Translations.Tool.Schemas.editor-state-v1.schema.json"),
        ("capabilities-v1.schema.json", "Runic.Translations.Tool.Schemas.capabilities-v1.schema.json"),
        ("project-v1.schema.json", "Runic.Translations.Tool.Schemas.project-v1.schema.json"),
    ];

    internal static IReadOnlyList<ToolArtifact> Read()
    {
        Assembly assembly = typeof(SchemaResources).Assembly;
        var artifacts = new List<ToolArtifact>(Schemas.Length);
        for (int index = 0; index < Schemas.Length; index++)
        {
            (string fileName, string resourceName) = Schemas[index];
            using Stream? stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                throw new InvalidOperationException($"bundled schema resource '{resourceName}' is missing.");
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            artifacts.Add(new ToolArtifact(fileName, buffer.ToArray()));
        }

        return ArtifactFiles.Normalize(artifacts);
    }
}
