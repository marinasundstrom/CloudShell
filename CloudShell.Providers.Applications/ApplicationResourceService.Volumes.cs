using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private static IReadOnlyList<ResourceVolumeMount> NormalizeVolumeMounts(
        IReadOnlyList<ResourceVolumeMount> volumeMounts) =>
        volumeMounts
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                !string.IsNullOrWhiteSpace(mount.TargetPath))
            .Select(mount => mount with
            {
                VolumeReference = mount.NormalizedVolumeReference,
                TargetPath = mount.NormalizedTargetPath,
                Name = mount.NormalizedName
            })
            .ToArray();

    internal static IReadOnlyList<string> CreateLocalContainerVolumeArguments(
        IReadOnlyList<ResourceVolumeMount> mounts,
        IResourceManagerStore? resourceManager,
        string contentRootPath) =>
        CreateLocalContainerVolumeMaterializations(mounts, resourceManager, contentRootPath)
            .Select(materialization => materialization.Argument)
            .ToArray();

    internal static IReadOnlyList<ResourceVolumeMountMaterialization> CreateLocalProcessVolumeMaterializations(
        IReadOnlyList<ResourceVolumeMount> mounts,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        string workingDirectory) =>
        mounts
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                !string.IsNullOrWhiteSpace(mount.TargetPath))
            .Select(mount => CreateLocalProcessVolumeMaterialization(
                mount,
                resourceManager,
                contentRootPath,
                workingDirectory))
            .ToArray();

    private static IReadOnlyList<LocalContainerVolumeMaterialization> CreateLocalContainerVolumeMaterializations(
        IReadOnlyList<ResourceVolumeMount> mounts,
        IResourceManagerStore? resourceManager,
        string contentRootPath) =>
        mounts
            .Where(mount =>
                !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                !string.IsNullOrWhiteSpace(mount.TargetPath))
            .Select(mount => CreateLocalContainerVolumeMaterialization(
                mount,
                resourceManager,
                contentRootPath))
            .ToArray();

    internal static string? GetVolumeMountUnavailableReason(
        IReadOnlyList<ResourceVolumeMount> mounts,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        ContainerHostDescriptor? containerHost = null)
    {
        foreach (var mount in mounts.Where(mount =>
                     !string.IsNullOrWhiteSpace(mount.VolumeReference) &&
                     !string.IsNullOrWhiteSpace(mount.TargetPath)))
        {
            var reason = GetVolumeMountUnavailableReason(
                mount.NormalizedVolumeReference,
                resourceManager,
                contentRootPath,
                containerHost);
            if (!string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }
        }

        return null;
    }

    private static string? GetVolumeMountUnavailableReason(
        string volumeReference,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        ContainerHostDescriptor? containerHost)
    {
        var volume = resourceManager?.GetResource(volumeReference);
        if (volume is null)
        {
            return null;
        }

        if (!IsVolumeResource(volume))
        {
            return $"Volume reference '{volumeReference}' points to resource '{volume.Name}', which is not a volume resource.";
        }

        var medium = GetAttribute(volume, ResourceAttributeNames.VolumeStorageMedium);
        if (!string.IsNullOrWhiteSpace(medium) &&
            !string.Equals(medium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return $"Volume resource '{volume.Id}' uses storage medium '{medium}', which cannot be mounted by the current resource materializer.";
        }

        if (string.Equals(medium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            var hostReason = GetContainerHostStorageMountUnavailableReason(
                containerHost,
                StorageMedia.FileSystem,
                $"volume resource '{volume.Id}'");
            if (!string.IsNullOrWhiteSpace(hostReason))
            {
                return hostReason;
            }
        }

        return GetStorageOwnedVolumeUnavailableReason(
            volume,
            resourceManager,
            contentRootPath,
            containerHost);
    }

    private static string? GetStorageOwnedVolumeUnavailableReason(
        Resource volume,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        ContainerHostDescriptor? containerHost)
    {
        var storageResourceId = GetAttribute(volume, ResourceAttributeNames.VolumeStorageResourceId);
        var subPath = GetAttribute(volume, ResourceAttributeNames.VolumeSubPath);
        if (string.IsNullOrWhiteSpace(storageResourceId))
        {
            return null;
        }

        var storage = resourceManager?.GetResource(storageResourceId);
        if (storage is null)
        {
            return $"Volume resource '{volume.Id}' references storage resource '{storageResourceId}', but that storage resource was not found.";
        }

        var storageMedium = GetAttribute(storage, ResourceAttributeNames.StorageMedium);
        if (!string.IsNullOrWhiteSpace(storageMedium) &&
            !string.Equals(storageMedium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return $"Storage resource '{storage.Id}' uses storage medium '{storageMedium}', which cannot be mounted by the current resource materializer.";
        }

        if (string.Equals(storageMedium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            var hostReason = GetContainerHostStorageMountUnavailableReason(
                containerHost,
                StorageMedia.FileSystem,
                $"storage resource '{storage.Id}'");
            if (!string.IsNullOrWhiteSpace(hostReason))
            {
                return hostReason;
            }
        }

        if (string.IsNullOrWhiteSpace(subPath))
        {
            return null;
        }

        if (Path.IsPathRooted(subPath))
        {
            return $"Volume resource '{volume.Id}' has absolute subpath '{subPath}'. Storage-owned volume subpaths must be relative.";
        }

        var storageRoot = GetAttribute(storage, ResourceAttributeNames.StorageLocation);
        if (string.IsNullOrWhiteSpace(storageRoot) ||
            string.Equals(storageRoot, "provider default", StringComparison.OrdinalIgnoreCase))
        {
            storageRoot = Path.Combine(
                contentRootPath,
                "Data",
                "storage",
                CreateStableIdentifier(storage.Id));
        }

        var fullStorageRoot = ResolveContentRootPath(storageRoot, contentRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(fullStorageRoot, subPath));
        return IsPathWithin(fullPath, fullStorageRoot)
            ? null
            : $"Volume resource '{volume.Id}' has subpath '{subPath}' outside storage resource '{storage.Id}'.";
    }

    private static string? GetContainerHostStorageMountUnavailableReason(
        ContainerHostDescriptor? containerHost,
        string storageMedium,
        string sourceDescription)
    {
        if (containerHost is null ||
            !string.Equals(storageMedium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase) ||
            containerHost.HostCapabilities.Contains(
                ContainerHostCapabilityIds.StorageMountFileSystem,
                StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"Container host '{containerHost.Id}' does not advertise required storage capability '{ContainerHostCapabilityIds.StorageMountFileSystem}' for {sourceDescription}.";
    }

    private static IReadOnlyList<ResourceVolumeMountMaterialization> MarkVolumeMountsNotActive(
        IEnumerable<ResourceVolumeMountMaterialization> mounts,
        DateTimeOffset observedAt) =>
        mounts
            .Select(mount => mount with
            {
                Status = ResourceVolumeMountMaterializationStatus.NotActive,
                ObservedAt = observedAt
            })
            .ToArray();

    private static LocalContainerVolumeMaterialization CreateLocalContainerVolumeMaterialization(
        ResourceVolumeMount mount,
        IResourceManagerStore? resourceManager,
        string contentRootPath)
    {
        var source = ResolveLocalContainerVolumeSource(
            mount.NormalizedVolumeReference,
            resourceManager,
            contentRootPath);
        var argument = mount.ReadOnly
            ? $"{source}:{mount.NormalizedTargetPath}:ro"
            : $"{source}:{mount.NormalizedTargetPath}";
        return new LocalContainerVolumeMaterialization(
            argument,
            new ResourceVolumeMountMaterialization(
                mount.NormalizedVolumeReference,
                mount.NormalizedTargetPath,
                source,
                mount.ReadOnly,
                ObservedAt: DateTimeOffset.UtcNow));
    }

    private static ResourceVolumeMountMaterialization CreateLocalProcessVolumeMaterialization(
        ResourceVolumeMount mount,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        string workingDirectory)
    {
        var source = ResolveLocalFileSystemVolumeSource(
            mount.NormalizedVolumeReference,
            resourceManager,
            contentRootPath,
            "local process runner");
        var target = ResolveLocalProcessVolumeTarget(
            mount.NormalizedTargetPath,
            workingDirectory);
        MaterializeLocalProcessVolumeTarget(
            mount,
            source,
            target);
        return new ResourceVolumeMountMaterialization(
            mount.NormalizedVolumeReference,
            mount.NormalizedTargetPath,
            source,
            mount.ReadOnly,
            ObservedAt: DateTimeOffset.UtcNow);
    }

    private static string ResolveLocalContainerVolumeSource(
        string volumeReference,
        IResourceManagerStore? resourceManager,
        string contentRootPath)
    {
        var volume = resourceManager?.GetResource(volumeReference);
        if (volume is null)
        {
            return volumeReference;
        }

        if (!IsVolumeResource(volume))
        {
            throw new InvalidOperationException(
                $"Volume reference '{volumeReference}' points to resource '{volume.Name}', which is not a volume resource.");
        }

        var medium = GetAttribute(volume, ResourceAttributeNames.VolumeStorageMedium);
        if (!string.IsNullOrWhiteSpace(medium) &&
            !string.Equals(medium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Volume resource '{volume.Id}' uses storage medium '{medium}', which cannot be mounted by the default local container runner.");
        }

        return ResolveLocalFileSystemVolumeSource(
            volumeReference,
            resourceManager,
            contentRootPath,
            "default local container runner");
    }

    private static string ResolveLocalFileSystemVolumeSource(
        string volumeReference,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        string materializerDescription)
    {
        var volume = resourceManager?.GetResource(volumeReference);
        if (volume is null)
        {
            var resolvedPath = ResolveContentRootPath(volumeReference, contentRootPath);
            Directory.CreateDirectory(resolvedPath);
            return resolvedPath;
        }

        if (!IsVolumeResource(volume))
        {
            throw new InvalidOperationException(
                $"Volume reference '{volumeReference}' points to resource '{volume.Name}', which is not a volume resource.");
        }

        var medium = GetAttribute(volume, ResourceAttributeNames.VolumeStorageMedium);
        if (!string.IsNullOrWhiteSpace(medium) &&
            !string.Equals(medium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Volume resource '{volume.Id}' uses storage medium '{medium}', which cannot be mounted by the {materializerDescription}.");
        }

        var path = GetAttribute(volume, ResourceAttributeNames.VolumeLocation);
        if (string.IsNullOrWhiteSpace(path))
        {
            path = ResolveStorageOwnedVolumePath(
                volume,
                resourceManager,
                contentRootPath,
                materializerDescription);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.Combine(
                contentRootPath,
                "Data",
                "storage",
                CreateStableIdentifier(volume.Id));
        }

        var fullPath = ResolveContentRootPath(path, contentRootPath);
        Directory.CreateDirectory(fullPath);
        return fullPath;
    }

    private static string ResolveStorageOwnedVolumePath(
        Resource volume,
        IResourceManagerStore? resourceManager,
        string contentRootPath,
        string materializerDescription)
    {
        var storageResourceId = GetAttribute(volume, ResourceAttributeNames.VolumeStorageResourceId);
        var subPath = GetAttribute(volume, ResourceAttributeNames.VolumeSubPath);
        if (string.IsNullOrWhiteSpace(storageResourceId))
        {
            return string.Empty;
        }

        var storage = resourceManager?.GetResource(storageResourceId)
            ?? throw new InvalidOperationException(
                $"Volume resource '{volume.Id}' references storage resource '{storageResourceId}', but that storage resource was not found.");
        var storageMedium = GetAttribute(storage, ResourceAttributeNames.StorageMedium);
        if (!string.IsNullOrWhiteSpace(storageMedium) &&
            !string.Equals(storageMedium, StorageMedia.FileSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Storage resource '{storage.Id}' uses storage medium '{storageMedium}', which cannot be mounted by the {materializerDescription}.");
        }

        var storageRoot = GetAttribute(storage, ResourceAttributeNames.StorageLocation);
        if (string.IsNullOrWhiteSpace(storageRoot) ||
            string.Equals(storageRoot, "provider default", StringComparison.OrdinalIgnoreCase))
        {
            storageRoot = Path.Combine(
                contentRootPath,
                "Data",
                "storage",
                CreateStableIdentifier(storage.Id));
        }

        var fullStorageRoot = ResolveContentRootPath(storageRoot, contentRootPath);
        if (string.IsNullOrWhiteSpace(subPath))
        {
            return Path.Combine(fullStorageRoot, CreateStableIdentifier(volume.Id));
        }

        if (Path.IsPathRooted(subPath))
        {
            throw new InvalidOperationException(
                $"Volume resource '{volume.Id}' has absolute subpath '{subPath}'. Storage-owned volume subpaths must be relative.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(fullStorageRoot, subPath));
        if (!IsPathWithin(fullPath, fullStorageRoot))
        {
            throw new InvalidOperationException(
                $"Volume resource '{volume.Id}' has subpath '{subPath}' outside storage resource '{storage.Id}'.");
        }

        return fullPath;
    }

    private static string ResolveLocalProcessVolumeTarget(
        string targetPath,
        string workingDirectory) =>
        Path.GetFullPath(Path.IsPathRooted(targetPath)
            ? targetPath
            : Path.Combine(workingDirectory, targetPath));

    private static void MaterializeLocalProcessVolumeTarget(
        ResourceVolumeMount mount,
        string source,
        string target)
    {
        Directory.CreateDirectory(source);

        if (PathsEqual(source, target))
        {
            return;
        }

        if (Directory.Exists(target) || File.Exists(target))
        {
            if (IsDirectoryLinkTo(target, source))
            {
                return;
            }

            throw new InvalidOperationException(
                $"Volume mount target '{target}' for volume '{mount.NormalizedVolumeReference}' already exists and is not linked to '{source}'. Remove it or choose an unused target path.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        Directory.CreateSymbolicLink(target, source);
    }

    private static bool IsDirectoryLinkTo(string target, string source)
    {
        var info = new DirectoryInfo(target);
        if (!info.Exists || !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return false;
        }

        var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
        return resolved is not null && PathsEqual(resolved.FullName, source);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.Ordinal);

    private static string ResolveContentRootPath(string path, string contentRootPath) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(contentRootPath, path));

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

    private static bool IsVolumeResource(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "cloudshell.volume", StringComparison.OrdinalIgnoreCase) ||
        resource.HasCapability(ResourceCapabilityIds.StorageVolume);

    private sealed record LocalContainerVolumeMaterialization(
        string Argument,
        ResourceVolumeMountMaterialization RuntimeState);
}
