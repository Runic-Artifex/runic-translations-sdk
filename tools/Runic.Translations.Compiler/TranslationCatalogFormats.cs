using System;
using System.Collections.Generic;

// Runic.Translations.Build links this file, as it links PathContainment: its source discovery task runs on every build
// and must not load the compiler, which the Build package does not ship beside the task.
#if RUNIC_TRANSLATIONS_BUILD
namespace Runic.Translations.Build;
#else
namespace Runic.Translations.Compiler;
#endif

/// <summary>
/// The two spellings of a catalog. <see cref="Runic"/> is today's: the project file <c>runic.json</c>, RMF2 sources
/// <c>*.rmf2</c>, the MF2 namespace <c>runic:</c> and the package folder <c>runic/&lt;catalog&gt;/</c>.
/// <see cref="Rax"/> is the Runic Artifex rename's (D079.26, D079.28, D079.29): <c>rax.json</c>, <c>*.ramf2</c>,
/// <c>rax:</c> and <c>rax/&lt;catalog&gt;/</c>. The content is the same.
/// </summary>
internal enum TranslationCatalogFormat
{
    Runic,
    Rax,
}

/// <summary>
/// The catalog formats a package may ship (rename plan §6.4, P6). A library catalog is read in either format: a
/// package built before the rename ships the former one, and Command Line, whose package carries the
/// <c>runic-command-line</c> catalog, moves to the new one before Translations does. Each name tells its format, so a
/// <c>.ramf2</c> source spells the namespace <c>rax:</c> and is read as if it spelled the current one. When a package
/// ships both, the new names are preferred (<see cref="PackagePreference"/>). Packing, the project's own catalog and
/// everything else keep <see cref="Current"/>.
/// </summary>
/// <remarks>
/// 0.24 prepares the rename: <see cref="Current"/> is still <see cref="TranslationCatalogFormat.Runic"/>, so no package
/// catalog is in the former format and <see cref="FormerFormatWarning"/> reports nothing. The rename changes
/// <see cref="Current"/> to <see cref="TranslationCatalogFormat.Rax"/>; from then on a package catalog in the
/// <see cref="TranslationCatalogFormat.Runic"/> format is still read, with a warning (the build's <c>RTR0109</c>),
/// through 1.0.x (D080.7). The rename keeps the <c>runic</c> spellings in this file (they name the former format).
/// </remarks>
internal static class TranslationCatalogFormats
{
    /// <summary>The format this release writes and the project's own catalog uses.</summary>
    internal const TranslationCatalogFormat Current = TranslationCatalogFormat.Runic;

    /// <summary>The order in which a package's catalog is looked for: the new names first.</summary>
    internal static readonly IReadOnlyList<TranslationCatalogFormat> PackagePreference = [TranslationCatalogFormat.Rax, TranslationCatalogFormat.Runic];

    internal static string ProjectFileName(TranslationCatalogFormat format) => format == TranslationCatalogFormat.Rax ? "rax.json" : "runic.json";

    internal static string SourceExtension(TranslationCatalogFormat format) => format == TranslationCatalogFormat.Rax ? ".ramf2" : ".rmf2";

    /// <summary>The MF2 namespace of the functions, options and markup Runic defines, with its colon.</summary>
    internal static string Namespace(TranslationCatalogFormat format) => format == TranslationCatalogFormat.Rax ? "rax:" : "runic:";

    /// <summary>The package folder that holds packed catalogs, <c>runic</c> or <c>rax</c>.</summary>
    internal static string PackageFolder(TranslationCatalogFormat format) => format == TranslationCatalogFormat.Rax ? "rax" : "runic";

    /// <summary>The format of a catalog by its project file: <c>rax.json</c> is <see cref="TranslationCatalogFormat.Rax"/>, any other name <see cref="TranslationCatalogFormat.Runic"/>.</summary>
    internal static TranslationCatalogFormat OfProject(string path) =>
        string.Equals(FileName(path), ProjectFileName(TranslationCatalogFormat.Rax), StringComparison.Ordinal) ? TranslationCatalogFormat.Rax : TranslationCatalogFormat.Runic;

    /// <summary>The format of a source by its extension: <c>.ramf2</c> is <see cref="TranslationCatalogFormat.Rax"/>; <c>.rmf2</c> and <c>.mf2</c> are <see cref="TranslationCatalogFormat.Runic"/>.</summary>
    internal static TranslationCatalogFormat OfSource(string path) =>
        path.EndsWith(SourceExtension(TranslationCatalogFormat.Rax), StringComparison.OrdinalIgnoreCase) ? TranslationCatalogFormat.Rax : TranslationCatalogFormat.Runic;

    /// <summary>Whether <paramref name="path"/> is an RMF2 resource source in either format (<c>.rmf2</c> or <c>.ramf2</c>).</summary>
    internal static bool IsResourceSource(string path) =>
        path.EndsWith(".rmf2", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".ramf2", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether <paramref name="path"/> is a source of a catalog in <paramref name="format"/>: <c>.mf2</c>, or that format's resource extension.</summary>
    internal static bool IsSource(string path, TranslationCatalogFormat format) =>
        path.EndsWith(".mf2", StringComparison.OrdinalIgnoreCase) || path.EndsWith(SourceExtension(format), StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name of an RMF2 resource source without its extension (<c>en</c> for <c>en.rmf2</c> and <c>en.ramf2</c>); null for another file.</summary>
    internal static string? ResourceStem(string fileName)
    {
        foreach (TranslationCatalogFormat format in PackagePreference)
        {
            string extension = SourceExtension(format);
            if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return fileName.Substring(0, fileName.Length - extension.Length);
        }
        return null;
    }

    /// <summary>
    /// A function, option or markup name as the compiler knows it: in a source of the format that is not
    /// <see cref="Current"/>, that format's namespace becomes the current one (<c>rax:uuid</c> in a <c>.ramf2</c>
    /// source reads as <c>runic:uuid</c> in 0.24). Every other name is unchanged.
    /// </summary>
    internal static string CanonicalName(string name, TranslationCatalogFormat sourceFormat)
    {
        if (sourceFormat == Current) return name;
        string prefix = Namespace(sourceFormat);
        return name.StartsWith(prefix, StringComparison.Ordinal) ? string.Concat(Namespace(Current).AsSpan(), name.AsSpan(prefix.Length)) : name;
    }

    /// <summary>Whether a package catalog in <paramref name="format"/> is in the former format, which warns.</summary>
    internal static bool IsFormer(TranslationCatalogFormat format, TranslationCatalogFormat current = Current) =>
        format == TranslationCatalogFormat.Runic && current != TranslationCatalogFormat.Runic;

    /// <summary>
    /// The warning for a package catalog read in <paramref name="format"/>, or null when
    /// it is not the former format. Always null in 0.24, where the former format is still <see cref="Current"/>.
    /// </summary>
    internal static string? FormerFormatWarning(string catalog, string? package, TranslationCatalogFormat format, TranslationCatalogFormat current = Current)
    {
        if (!IsFormer(format, current)) return null;
        string from = string.IsNullOrEmpty(package) ? "A package" : "Package '" + package + "'";
        return from + " ships catalog '" + catalog + "' in the former format (" + ProjectFileName(format) + ", *" + SourceExtension(format) + ", " +
            Namespace(format) + ", " + PackageFolder(format) + "/). It is read as before; ask its author for a release built with this Translations release, " +
            "which ships " + ProjectFileName(current) + ", *" + SourceExtension(current) + " and " + Namespace(current) + ".";
    }

    private static string FileName(string path)
    {
        int slash = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return slash < 0 ? path : path.Substring(slash + 1);
    }
}
