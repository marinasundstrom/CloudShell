using CloudShell.Abstractions.ResourceManager;
using System.Security.Cryptography;
using System.Text;

namespace CloudShell.ControlPlane.Providers;

public static class ContainerApplicationRuntimeRevisions
{
    public static string CreateImageRevisionId(string? registry, string image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var effectiveRegistry = string.IsNullOrWhiteSpace(registry)
            ? ContainerRegistryDefaults.Default
            : registry.Trim();
        var revisionKey = $"{effectiveRegistry}\n{image.Trim()}";
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(revisionKey), hash);
        return $"rev-img-{Convert.ToHexString(hash[..6]).ToLowerInvariant()}";
    }
}
