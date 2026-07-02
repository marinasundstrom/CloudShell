using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Platform;

public sealed class VolumeFilesystemMonitoringProvider(
    PlatformResourceStore store,
    IHostEnvironment? environment = null) : IResourceMonitoringProvider
{
    private const string ProviderName = "cloudshell.volume.filesystem";
    private readonly string? contentRootPath = environment?.ContentRootPath;

    public bool CanMonitor(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, PlatformResourceProvider.VolumeResourceType, StringComparison.OrdinalIgnoreCase) &&
        ResolveVolumePath(resource) is not null;

    public Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = ResolveVolumePath(resource);
        if (resolved is null)
        {
            return Task.FromResult<ResourceMonitoringSnapshot?>(null);
        }

        var now = DateTimeOffset.UtcNow;
        if (!Directory.Exists(resolved.Path))
        {
            return Task.FromResult<ResourceMonitoringSnapshot?>(new ResourceMonitoringSnapshot(
                resource.Id,
                ProviderName,
                now,
                [],
                "pending",
                $"Volume path '{resolved.Path}' has not been materialized yet."));
        }

        var usedBytes = GetDirectorySize(resolved.Path, cancellationToken);
        var metrics = new List<ResourceMetricSample>
        {
            new(
                "storage.volume.used",
                usedBytes,
                "bytes",
                now,
                "Volume used",
                "Current filesystem bytes used by the volume.",
                CreateAttributes(resolved))
        };

        var maxSizeBytes = GetMaxSizeBytes(resource);
        if (maxSizeBytes is { } maxSize)
        {
            metrics.Add(new ResourceMetricSample(
                "storage.volume.maxSize",
                maxSize,
                "bytes",
                now,
                "Volume max size",
                "Configured maximum size in bytes for the volume.",
                CreateAttributes(resolved)));
            metrics.Add(new ResourceMetricSample(
                "storage.volume.free",
                Math.Max(0, maxSize - usedBytes),
                "bytes",
                now,
                "Max size remaining",
                "Configured volume max-size bytes remaining.",
                CreateAttributes(resolved)));
            metrics.Add(new ResourceMetricSample(
                "storage.volume.maxSizeReached",
                usedBytes >= maxSize ? 1 : 0,
                "count",
                now,
                "Max size reached",
                "Whether the observed volume usage has reached or exceeded the configured max size.",
                CreateAttributes(resolved)));
            metrics.Add(new ResourceMetricSample(
                "storage.volume.utilization",
                maxSize == 0 ? 0 : usedBytes * 100d / maxSize,
                "%",
                now,
                "Max size utilization",
                "Current percentage of configured volume max size used.",
                CreateAttributes(resolved)));
        }

        return Task.FromResult<ResourceMonitoringSnapshot?>(new ResourceMonitoringSnapshot(
            resource.Id,
            ProviderName,
            now,
            metrics,
            maxSizeBytes is { } limit && usedBytes >= limit ? "warning" : "available",
            maxSizeBytes is { } maxSizeLimit && usedBytes >= maxSizeLimit
                ? $"Volume path '{resolved.Path}' is using {usedBytes.ToString(CultureInfo.InvariantCulture)} bytes, at or above max size {maxSizeLimit.ToString(CultureInfo.InvariantCulture)} bytes."
                : $"Volume path '{resolved.Path}' is available."));
    }

    private VolumePathResolution? ResolveVolumePath(Resource resource)
    {
        var location = GetAttribute(resource, ResourceAttributeNames.VolumeLocation);
        if (!string.IsNullOrWhiteSpace(location))
        {
            return new VolumePathResolution(
                ResolveStoragePath(location),
                "direct",
                null);
        }

        var storageResourceId = GetAttribute(resource, ResourceAttributeNames.VolumeStorageResourceId);
        var subPath = GetAttribute(resource, ResourceAttributeNames.VolumeSubPath);
        if (string.IsNullOrWhiteSpace(storageResourceId) ||
            string.IsNullOrWhiteSpace(subPath) ||
            Path.IsPathRooted(subPath))
        {
            return null;
        }

        var storage = store.GetStorage(storageResourceId);
        if (storage is null ||
            !string.Equals(storage.Medium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var storageRoot = ResolveStoragePath(GetStorageObservationRoot(storage));
        var volumePath = Path.GetFullPath(Path.Combine(storageRoot, subPath));
        return IsPathWithin(volumePath, storageRoot)
            ? new VolumePathResolution(volumePath, "storage", storage.Id)
            : null;
    }

    private static long? GetMaxSizeBytes(Resource resource) =>
        long.TryParse(
            GetAttribute(resource, ResourceAttributeNames.VolumeMaxSizeBytes),
            out var maxSizeBytes) &&
        maxSizeBytes > 0
            ? maxSizeBytes
            : null;

    private static string? GetAttribute(Resource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private string GetStorageObservationRoot(StorageResourceDefinition storage) =>
        !string.IsNullOrWhiteSpace(storage.Location)
            ? storage.Location
            : Path.Combine(
                contentRootPath ?? Directory.GetCurrentDirectory(),
                "Data",
                "storage",
                CreateStableIdentifier(storage.Id));

    private string ResolveStoragePath(string location)
    {
        var trimmed = location.Trim();
        if (Path.IsPathFullyQualified(trimmed))
        {
            return Path.GetFullPath(trimmed);
        }

        return Path.GetFullPath(Path.Combine(contentRootPath ?? Directory.GetCurrentDirectory(), trimmed));
    }

    private static long GetDirectorySize(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var pending = new Stack<string>();
        pending.Push(path);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(current))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += new FileInfo(file).Length;
                }

                foreach (var directory in Directory.EnumerateDirectories(current))
                {
                    pending.Push(directory);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return total;
    }

    private static IReadOnlyDictionary<string, string> CreateAttributes(VolumePathResolution resolution)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["storage.volume.path"] = resolution.Path,
            ["storage.volume.source"] = resolution.Source
        };
        if (!string.IsNullOrWhiteSpace(resolution.StorageResourceId))
        {
            attributes["storage.volume.storageResourceId"] = resolution.StorageResourceId;
        }

        return attributes;
    }

    private static string CreateStableIdentifier(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "cloudshell" : identifier;
    }

    private static bool IsPathWithin(string candidatePath, string rootPath)
    {
        var normalizedRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedCandidate, normalizedRoot, StringComparison.Ordinal) ||
            normalizedCandidate.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.Ordinal);
    }

    private sealed record VolumePathResolution(
        string Path,
        string Source,
        string? StorageResourceId);
}
