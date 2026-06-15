using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Components;

public static class ResourceNameNormalizer
{
    public static string Normalize(string name, string prefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);

        return ResourceId.FromName(prefix, name).Value;
    }
}
