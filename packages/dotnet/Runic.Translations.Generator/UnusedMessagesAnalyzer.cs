using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Runic.Translations.Generator;

/// <summary>
/// Reports, as information (<c>RTR0100</c>), the messages of a generated catalog that no C# code of the project
/// references: through the readable members (<c>Text.Messages.greeting</c>), the encoded members, the
/// <c>References</c> factories or the key properties. Opt-in with <c>RunicTranslationsReportUnusedMessages=true</c>;
/// it runs at the end of a full compilation, so command-line builds and full-solution analysis show it.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnusedMessagesAnalyzer : DiagnosticAnalyzer
{
    internal const string EnableProperty = "build_property.RunicTranslationsReportUnusedMessages";
    private const string KeySuffix = "Keys";
    private static readonly Regex Forwarded = new(@"(?:__text|Keys)\.r_([0-9a-f]+)\b", RegexOptions.CultureInvariant);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [TranslationsDiagnostics.MessageNotReferenced];

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        // Generated code counts (XAML .g.cs files, other source generators), except the catalog's own generated types,
        // whose readable members and reference factories forward to the encoded ones.
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze);
        context.RegisterCompilationStartAction(static start =>
        {
            if (!start.Options.AnalyzerConfigOptionsProvider.GlobalOptions.TryGetValue(EnableProperty, out string? enabled) ||
                !string.Equals(enabled?.Trim(), "true", StringComparison.OrdinalIgnoreCase)) return;
            INamedTypeSymbol? keyType = start.Compilation.GetTypeByMetadataName("Runic.Translations.TranslationKey");
            if (keyType is null) return;
            var catalogs = new List<Catalog>();
            var members = new Dictionary<ISymbol, (Catalog Catalog, string[] Keys)>(SymbolEqualityComparer.Default);
            var generated = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (INamedTypeSymbol keys in Types(start.Compilation.Assembly.GlobalNamespace))
            {
                if (!keys.IsStatic || !keys.Name.EndsWith(KeySuffix, StringComparison.Ordinal) || keys.Name.Length == KeySuffix.Length) continue;
                IPropertySymbol[] properties = keys.GetMembers().OfType<IPropertySymbol>()
                    .Where(property => property.IsStatic && SymbolEqualityComparer.Default.Equals(property.Type, keyType) && Decode(property.Name) is not null).ToArray();
                if (properties.Length == 0) continue;
                string className = keys.Name.Substring(0, keys.Name.Length - KeySuffix.Length);
                var catalog = new Catalog((keys.ContainingNamespace.IsGlobalNamespace ? "" : keys.ContainingNamespace.ToDisplayString() + ".") + className,
                    properties.Select(property => Decode(property.Name)!).Distinct(StringComparer.Ordinal).OrderBy(static key => key, StringComparer.Ordinal).ToArray());
                catalogs.Add(catalog);
                generated.Add(keys);
                foreach (string suffix in GeneratedSuffixes)
                    foreach (INamedTypeSymbol type in keys.ContainingNamespace.GetTypeMembers(className + suffix)) generated.Add(type);
                foreach (IPropertySymbol property in properties) members[property] = (catalog, [Decode(property.Name)!]);
                INamedTypeSymbol? text = keys.ContainingNamespace.GetTypeMembers(className).FirstOrDefault();
                if (text is null) continue;
                foreach (ISymbol member in text.GetMembers())
                    if (Decode(member.Name.StartsWith("r_args_", StringComparison.Ordinal) ? member.Name.Substring("r_args_".Length) : member.Name) is { } key) members[member] = (catalog, [key]);
                // Readable members and reference factories forward to an encoded member or key.
                IEnumerable<INamedTypeSymbol> forwarding = keys.ContainingNamespace.GetTypeMembers(className + "Messages").Concat(text.GetTypeMembers("References"));
                foreach (INamedTypeSymbol type in forwarding)
                    foreach (ISymbol member in type.GetMembers())
                    {
                        if (member.IsImplicitlyDeclared || member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.PropertyGet }) continue;
                        string[] forwarded = member.DeclaringSyntaxReferences
                            .SelectMany(reference => Forwarded.Matches(reference.GetSyntax(start.CancellationToken).ToString()).Cast<Match>())
                            .Select(match => Decode("r_" + match.Groups[1].Value)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
                        if (forwarded.Length != 0) members[member] = (catalog, forwarded);
                    }
            }
            if (catalogs.Count == 0) return;
            var used = new ConcurrentDictionary<(Catalog, string), byte>();
            start.RegisterOperationAction(operation =>
            {
                if (operation.IsGeneratedCode && InCatalogType(operation.ContainingSymbol, generated)) return;
                ISymbol? target = operation.Operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IPropertyReferenceOperation property => property.Property,
                    IMethodReferenceOperation method => method.Method,
                    _ => null,
                };
                if (target is not null && members.TryGetValue(target.OriginalDefinition, out var found))
                    foreach (string key in found.Keys) used.TryAdd((found.Catalog, key), 0);
            }, OperationKind.Invocation, OperationKind.PropertyReference, OperationKind.MethodReference);
            start.RegisterCompilationEndAction(end =>
            {
                foreach (Catalog catalog in catalogs)
                    foreach (string key in catalog.Keys)
                        if (!used.ContainsKey((catalog, key)))
                            end.ReportDiagnostic(Diagnostic.Create(TranslationsDiagnostics.MessageNotReferenced, Location.None,
                                "Message '" + key + "' of " + catalog.Name + " is not referenced from the C# code of this project. Remove it, or ignore this if it is used by key, from XAML, from another assembly or from the web."));
            });
        });
    }

    // The types the translations generator writes for a catalog X: X, XKeys, XMessages, XCatalog, XCatalogData, XSlots.
    private static readonly string[] GeneratedSuffixes = ["", "Messages", "Catalog", "CatalogData", "Slots"];

    // Whether generated code belongs to a catalog's generated types (a user's partial X in a hand-written file is not
    // generated code, so its uses count).
    private static bool InCatalogType(ISymbol? symbol, HashSet<INamedTypeSymbol> generated)
    {
        for (INamedTypeSymbol? type = symbol as INamedTypeSymbol ?? symbol?.ContainingType; type is not null; type = type.ContainingType)
            if (generated.Contains(type.OriginalDefinition)) return true;
        return false;
    }

    private sealed class Catalog(string name, string[] keys)
    {
        internal string Name { get; } = name;
        internal string[] Keys { get; } = keys;
    }

    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol root)
    {
        var pending = new Stack<INamespaceSymbol>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            INamespaceSymbol current = pending.Pop();
            foreach (INamespaceSymbol child in current.GetNamespaceMembers()) pending.Push(child);
            foreach (INamedTypeSymbol type in current.GetTypeMembers()) yield return type;
        }
    }

    // An encoded name is "r_" and the lowercase hex of the key's UTF-8 bytes.
    private static string? Decode(string name)
    {
        if (!name.StartsWith("r_", StringComparison.Ordinal) || name.Length < 4 || name.Length % 2 != 0) return null;
        var bytes = new byte[(name.Length - 2) / 2];
        for (int index = 0; index < bytes.Length; index++)
        {
            int high = Hex(name[2 + index * 2]), low = Hex(name[3 + index * 2]);
            if (high < 0 || low < 0) return null;
            bytes[index] = (byte)(high * 16 + low);
        }
        try { return new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { return null; }
        static int Hex(char value) => value is >= '0' and <= '9' ? value - '0' : value is >= 'a' and <= 'f' ? value - 'a' + 10 : -1;
    }
}
