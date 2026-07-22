namespace WebUIToolkit.TextResources;

/// <summary>Identifies a generated text resource within one catalog.</summary>
/// <param name="Catalog">The stable catalog identifier.</param>
/// <param name="Id">The build-local ordinal key identifier.</param>
/// <param name="Name">The stable dotted key name.</param>
public readonly record struct TextResourceKey(string Catalog, int Id, string Name);
