using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Build.Framework;

namespace Runic.Translations.Build;

// A task output item: the package takes no Microsoft.Build.Utilities dependency.
internal sealed class TaskItem : ITaskItem
{
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    internal TaskItem(string itemSpec) => ItemSpec = itemSpec;

    public string ItemSpec { get; set; }
    public ICollection MetadataNames => _metadata.Keys;
    public int MetadataCount => _metadata.Count;
    public string GetMetadata(string metadataName) => _metadata.TryGetValue(metadataName, out string? value) ? value : string.Empty;
    public void SetMetadata(string metadataName, string metadataValue) => _metadata[metadataName] = metadataValue ?? string.Empty;
    public void RemoveMetadata(string metadataName) => _metadata.Remove(metadataName);
    public void CopyMetadataTo(ITaskItem destinationItem)
    {
        foreach (KeyValuePair<string, string> pair in _metadata)
            if (string.IsNullOrEmpty(destinationItem.GetMetadata(pair.Key))) destinationItem.SetMetadata(pair.Key, pair.Value);
    }
    public IDictionary CloneCustomMetadata() => new Dictionary<string, string>(_metadata, StringComparer.OrdinalIgnoreCase);
}
