using System.Collections.Concurrent;
using System.IO.Compression;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public interface IApplicationArtifactMaterializer
{
    ValueTask<ApplicationArtifactMaterializationResult> MaterializeAsync(
        Resource resource,
        ApplicationArtifactReference artifact,
        string artifactFolder,
        CancellationToken cancellationToken = default);
}

public interface IApplicationArtifactFolderResolver
{
    string GetArtifactFolder(Resource resource);
}

public sealed record ApplicationArtifactMaterializationResult(
    string RootDirectory,
    string EntryPath,
    ApplicationArtifactReference Artifact);

public sealed class ApplicationArtifactFolderResolver : IApplicationArtifactFolderResolver
{
    public string GetArtifactFolder(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return Path.Combine(
            Path.GetTempPath(),
            "cloudshell",
            "resource-artifacts",
            NormalizePathSegment(resource.EffectiveResourceId));
    }

    internal static string NormalizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var trimmed = value.Trim();
        var chars = trimmed.Select(character =>
            invalid.Contains(character) || character is ':' or '/' or '\\'
                ? '_'
                : character);
        return string.Concat(chars);
    }
}

public sealed class ApplicationArtifactMaterializer(
    IDeploymentArtifactContentStore? contentStore = null) : IApplicationArtifactMaterializer
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> MaterializationLocks = new(
        StringComparer.OrdinalIgnoreCase);

    public async ValueTask<ApplicationArtifactMaterializationResult> MaterializeAsync(
        Resource resource,
        ApplicationArtifactReference artifact,
        string artifactFolder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactFolder);

        if (contentStore is null)
        {
            throw new InvalidOperationException(
                "Application artifact content is not available on this host.");
        }

        if (!string.Equals(artifact.PackageKind, "zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Application artifact package kind '{artifact.PackageKind}' is not supported by the local materializer.");
        }

        var rootDirectory = Path.GetFullPath(artifactFolder);
        var markerPath = Path.Combine(rootDirectory, ".cloudshell-artifact-materialized");
        var materializationLock = MaterializationLocks.GetOrAdd(rootDirectory, _ => new SemaphoreSlim(1, 1));

        await materializationLock.WaitAsync(cancellationToken);
        try
        {
            var marker = File.Exists(markerPath)
                ? await File.ReadAllTextAsync(markerPath, cancellationToken)
                : null;
            if (!string.Equals(marker, artifact.ContentSha256, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(rootDirectory))
                {
                    Directory.Delete(rootDirectory, recursive: true);
                }

                Directory.CreateDirectory(rootDirectory);
                await using var content = await contentStore.OpenDeploymentArtifactContentAsync(
                    resource.EffectiveResourceId,
                    artifact.ArtifactId,
                    artifact.RevisionId,
                    cancellationToken);
                ExtractZip(content, rootDirectory);
                await File.WriteAllTextAsync(
                    markerPath,
                    artifact.ContentSha256,
                    cancellationToken);
            }
        }
        finally
        {
            materializationLock.Release();
        }

        return new(
            rootDirectory,
            ResolveEntryPath(rootDirectory, artifact.EntryPath),
            artifact);
    }

    private static void ExtractZip(
        Stream content,
        string destinationDirectory)
    {
        using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: false);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!destinationRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            destinationRoot += Path.DirectorySeparatorChar;
        }

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Application artifact entry '{entry.FullName}' escapes the materialization directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            entry.ExtractToFile(targetPath, overwrite: true);
        }
    }

    private static string ResolveEntryPath(
        string rootDirectory,
        string? entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath) ||
            string.Equals(entryPath.Trim(), ".", StringComparison.Ordinal))
        {
            return rootDirectory;
        }

        var root = Path.GetFullPath(rootDirectory);
        var path = Path.GetFullPath(Path.Combine(root, entryPath.Trim()));
        return path.StartsWith(root, StringComparison.Ordinal)
            ? path
            : throw new InvalidDataException(
                $"Application artifact entry path '{entryPath}' escapes the materialization directory.");
    }
}
