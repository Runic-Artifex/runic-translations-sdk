using System;
using System.Security.Cryptography;
using System.Text;

namespace RunicTextResources.Compiler.Generation;

/// <summary>Identifies one independently rendered compiler output.</summary>
public enum TextResourceGeneratedOutputKind
{
    /// <summary>Strongly typed key declarations.</summary>
    CSharpKeys,
    /// <summary>Strongly typed snapshot accessors.</summary>
    CSharpAccessors,
    /// <summary>Reflection-free compiled catalog data.</summary>
    CSharpCatalogData,
    /// <summary>Application-facing provider registration.</summary>
    CSharpRegistration,
    /// <summary>A resolved, canonical locale artifact.</summary>
    LocaleJson,
    /// <summary>The versioned template compiler edge contract.</summary>
    TemplateManifestJson,
    /// <summary>The versioned TypeScript key and argument edge contract.</summary>
    TypeScriptContract,
    /// <summary>The versioned host asset inventory for selected non-C# outputs.</summary>
    AssetManifestJson,
}

/// <summary>A deterministic, UTF-8 compiler output for exactly one concern.</summary>
public sealed class TextResourceGeneratedOutput
{
    private readonly byte[] _utf8Bytes;

    internal TextResourceGeneratedOutput(TextResourceGeneratedOutputKind kind, string relativePath, string mediaType, string text)
    {
        Kind = kind;
        RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
        ArgumentNullException.ThrowIfNull(text);

        Text = NormalizeLineEndings(text);
        _utf8Bytes = new UTF8Encoding(false, true).GetBytes(Text);
        Sha256 = ComputeSha256(_utf8Bytes);
    }

    /// <summary>The concern represented by this output.</summary>
    public TextResourceGeneratedOutputKind Kind { get; }

    /// <summary>A normalized, relative suggested output path.</summary>
    public string RelativePath { get; }

    /// <summary>The output media type without a character-set parameter.</summary>
    public string MediaType { get; }

    /// <summary>The generated text with LF line endings.</summary>
    public string Text { get; }

    /// <summary>The lowercase SHA-256 of the complete UTF-8 output, prefixed with <c>sha256:</c>.</summary>
    public string Sha256 { get; }

    /// <summary>Returns a defensive copy of the BOM-less UTF-8 output.</summary>
    public byte[] GetUtf8Bytes() => (byte[])_utf8Bytes.Clone();

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        var result = new StringBuilder("sha256:".Length + (hash.Length * 2));
        result.Append("sha256:");
        for (int i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        return result.ToString();
    }
}
