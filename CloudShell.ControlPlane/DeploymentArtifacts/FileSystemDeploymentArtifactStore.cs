using System.Security.Cryptography;
using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.DeploymentArtifacts;

public sealed class FileSystemDeploymentArtifactStore(
    IOptions<DeploymentArtifactOptions> options,
    IConfiguration configuration,
    IHostEnvironment environment) : IDeploymentArtifactStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public DeploymentArtifactStoreStatus GetStatus()
    {
        var store = GetStoreOptions();
        return new(
            IsEnabled: IsFileSystemStore(store),
            Kind: NormalizeKind(store.Kind),
            MaxUploadBytes: NormalizeMaxUploadBytes(store.MaxUploadBytes),
            AllowedPackageKinds: NormalizeAllowedPackageKinds(store.AllowedPackageKinds));
    }

    public async Task<DeploymentArtifactUploadSession> CreateUploadSessionAsync(
        CreateDeploymentArtifactUploadSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var store = RequireFileSystemStore();
        ValidateUploadCommand(command, store);

        var uploadId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var metadata = new UploadSessionMetadata(
            uploadId,
            NormalizeNullable(command.ResourceId),
            command.ResourceType.Trim(),
            command.ResourceName.Trim(),
            NormalizePackageKind(command.PackageKind),
            NormalizeNullable(command.FileName),
            command.ContentLength,
            NormalizeNullable(command.ContentSha256)?.ToLowerInvariant(),
            NormalizeNullable(command.ArtifactLayoutKind),
            now,
            now.AddMinutes(NormalizeTimeoutMinutes(store.UploadSessionTimeoutMinutes)));

        var directory = GetUploadDirectory(uploadId, create: true);
        await WriteJsonAsync(
            Path.Combine(directory, "upload.json"),
            metadata,
            cancellationToken);

        return new(
            uploadId,
            metadata.ExpiresAt,
            NormalizeMaxUploadBytes(store.MaxUploadBytes),
            NormalizeAllowedPackageKinds(store.AllowedPackageKinds));
    }

    public async Task WriteUploadContentAsync(
        string resourceId,
        string uploadId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(uploadId);
        ArgumentNullException.ThrowIfNull(content);

        var store = RequireFileSystemStore();
        var metadata = await ReadUploadMetadataAsync(uploadId, cancellationToken);
        EnsureSessionActive(metadata);
        EnsureResourceMatches(resourceId, metadata);

        var maxBytes = NormalizeMaxUploadBytes(store.MaxUploadBytes);
        var path = Path.Combine(GetUploadDirectory(uploadId), GetPackageFileName(metadata.PackageKind));
        await using var output = File.Create(path);

        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    $"Deployment artifact upload '{uploadId}' exceeds the configured {maxBytes} byte limit.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task<DeploymentArtifactRevision> CompleteUploadAsync(
        string resourceId,
        CompleteDeploymentArtifactUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.UploadId);

        RequireFileSystemStore();
        var metadata = await ReadUploadMetadataAsync(command.UploadId, cancellationToken);
        EnsureSessionActive(metadata);
        EnsureResourceMatches(resourceId, metadata);

        var uploadDirectory = GetUploadDirectory(command.UploadId);
        var packagePath = Path.Combine(uploadDirectory, GetPackageFileName(metadata.PackageKind));
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException(
                $"Deployment artifact upload '{command.UploadId}' has no uploaded content.",
                packagePath);
        }

        var (hash, sizeBytes) = await ComputeHashAsync(packagePath, cancellationToken);
        var expectedHash = NormalizeNullable(command.ContentSha256) ??
            NormalizeNullable(metadata.ContentSha256);
        if (!string.IsNullOrWhiteSpace(expectedHash) &&
            !string.Equals(hash, expectedHash.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Deployment artifact upload '{command.UploadId}' content hash did not match the expected SHA-256 value.");
        }

        var createdAt = DateTimeOffset.UtcNow;
        var artifactId = CreateArtifactId(metadata.ResourceId ?? metadata.ResourceName);
        var revisionId = CreateRevisionId(createdAt, hash);
        var revisionDirectory = GetRevisionDirectory(artifactId, revisionId, create: true);
        var committedPackagePath = Path.Combine(revisionDirectory, GetPackageFileName(metadata.PackageKind));
        File.Copy(packagePath, committedPackagePath, overwrite: false);

        var revision = new DeploymentArtifactRevision(
            artifactId,
            revisionId,
            metadata.PackageKind,
            hash,
            sizeBytes,
            createdAt,
            metadata.ArtifactLayoutKind,
            DeploymentArtifactSourceKinds.UploadedArtifact,
            SourceVersion: null,
            CanRehydrate: false);
        await WriteJsonAsync(
            Path.Combine(revisionDirectory, "revision.json"),
            revision,
            cancellationToken);

        Directory.Delete(uploadDirectory, recursive: true);
        return revision;
    }

    public async Task<DeploymentArtifactRevision?> GetRevisionAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);

        RequireFileSystemStore();
        if (!ArtifactMatchesResource(resourceId, artifactId))
        {
            return null;
        }

        var path = Path.Combine(GetRevisionDirectory(artifactId, revisionId), "revision.json");
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DeploymentArtifactRevision>(
            stream,
            SerializerOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<DeploymentArtifactRevision>> ListRevisionsAsync(
        string resourceId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        RequireFileSystemStore();
        if (!ArtifactMatchesResource(resourceId, artifactId))
        {
            return [];
        }

        var artifactDirectory = GetArtifactDirectory(artifactId);
        if (!Directory.Exists(artifactDirectory))
        {
            return [];
        }

        var revisions = new List<DeploymentArtifactRevision>();
        foreach (var revisionDirectory in Directory.EnumerateDirectories(artifactDirectory))
        {
            var path = Path.Combine(revisionDirectory, "revision.json");
            if (!File.Exists(path))
            {
                continue;
            }

            await using var stream = File.OpenRead(path);
            var revision = await JsonSerializer.DeserializeAsync<DeploymentArtifactRevision>(
                stream,
                SerializerOptions,
                cancellationToken);
            if (revision is not null)
            {
                revisions.Add(revision);
            }
        }

        return revisions
            .OrderByDescending(revision => revision.CreatedAt)
            .ThenByDescending(revision => revision.RevisionId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<Stream> OpenRevisionContentAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        var revision = await GetRevisionAsync(resourceId, artifactId, revisionId, cancellationToken) ??
            throw new FileNotFoundException(
                $"Deployment artifact revision '{artifactId}/revisions/{revisionId}' was not found.");
        var path = Path.Combine(
            GetRevisionDirectory(artifactId, revisionId),
            GetPackageFileName(revision.PackageKind));
        return File.OpenRead(path);
    }

    public Task DeleteResourceArtifactsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        if (!IsFileSystemStore(GetStoreOptions()))
        {
            return Task.CompletedTask;
        }

        var artifactId = CreateArtifactId(resourceId);
        var artifactDirectory = GetArtifactDirectory(artifactId);
        if (Directory.Exists(artifactDirectory))
        {
            Directory.Delete(artifactDirectory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private DeploymentArtifactStoreOptions GetStoreOptions() => options.Value.Store;

    private DeploymentArtifactStoreOptions RequireFileSystemStore()
    {
        var store = GetStoreOptions();
        if (!IsFileSystemStore(store))
        {
            throw new InvalidOperationException(
                "Deployment artifact uploads are disabled. Configure DeploymentArtifacts:Store:Kind to FileSystem and set DeploymentArtifacts:Store:RootPath to enable uploads.");
        }

        return store;
    }

    private static bool IsFileSystemStore(DeploymentArtifactStoreOptions store) =>
        string.Equals(store.Kind, DeploymentArtifactStoreKinds.FileSystem, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeKind(string? kind) =>
        string.IsNullOrWhiteSpace(kind) ? DeploymentArtifactStoreKinds.Disabled : kind.Trim();

    private string GetRootDirectory(bool create = false)
    {
        var root = CloudShellDataDirectory.ResolvePath(
            GetStoreOptions().RootPath,
            configuration,
            environment);
        if (create)
        {
            Directory.CreateDirectory(root);
        }

        return root;
    }

    private string GetUploadDirectory(string uploadId, bool create = false)
    {
        var directory = Path.Combine(GetRootDirectory(create), ".staging", NormalizePathSegment(uploadId));
        if (create)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private string GetRevisionDirectory(string artifactId, string revisionId, bool create = false)
    {
        var directory = Path.Combine(
            GetArtifactDirectory(artifactId, create),
            NormalizePathSegment(revisionId));
        if (create)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private string GetArtifactDirectory(string artifactId, bool create = false)
    {
        var directory = Path.Combine(
            GetRootDirectory(create),
            "revisions",
            NormalizePathSegment(artifactId));
        if (create)
        {
            Directory.CreateDirectory(directory);
        }

        return directory;
    }

    private async Task<UploadSessionMetadata> ReadUploadMetadataAsync(
        string uploadId,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(GetUploadDirectory(uploadId), "upload.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Deployment artifact upload session '{uploadId}' was not found.",
                path);
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<UploadSessionMetadata>(
            stream,
            SerializerOptions,
            cancellationToken) ??
            throw new InvalidDataException(
                $"Deployment artifact upload session '{uploadId}' metadata could not be read.");
    }

    private static void EnsureSessionActive(UploadSessionMetadata metadata)
    {
        if (metadata.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException(
                $"Deployment artifact upload session '{metadata.UploadId}' has expired.");
        }
    }

    private static void EnsureResourceMatches(string resourceId, UploadSessionMetadata metadata)
    {
        var expectedResourceId = metadata.ResourceId ?? metadata.ResourceName;
        if (!string.Equals(resourceId.Trim(), expectedResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Deployment artifact upload session '{metadata.UploadId}' belongs to resource '{expectedResourceId}'.");
        }
    }

    private static bool ArtifactMatchesResource(string resourceId, string artifactId) =>
        string.Equals(artifactId, CreateArtifactId(resourceId), StringComparison.OrdinalIgnoreCase);

    private static void ValidateUploadCommand(
        CreateDeploymentArtifactUploadSessionCommand command,
        DeploymentArtifactStoreOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ResourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ResourceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.PackageKind);

        var packageKind = NormalizePackageKind(command.PackageKind);
        if (!NormalizeAllowedPackageKinds(options.AllowedPackageKinds)
            .Contains(packageKind, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Deployment artifact package kind '{command.PackageKind}' is not allowed.");
        }

        if (command.ContentLength is < 0)
        {
            throw new InvalidDataException("Deployment artifact content length cannot be negative.");
        }

        var maxBytes = NormalizeMaxUploadBytes(options.MaxUploadBytes);
        if (command.ContentLength is long contentLength && contentLength > maxBytes)
        {
            throw new InvalidDataException(
                $"Deployment artifact upload content length exceeds the configured {maxBytes} byte limit.");
        }
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            SerializerOptions,
            cancellationToken);
    }

    private static async Task<(string Hash, long SizeBytes)> ComputeHashAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (Convert.ToHexString(hash).ToLowerInvariant(), stream.Length);
    }

    private static string CreateArtifactId(string resourceName) =>
        $"deployment-artifact:{resourceName.Trim()}";

    private static string CreateRevisionId(DateTimeOffset createdAt, string hash) =>
        $"{createdAt.UtcDateTime:yyyyMMddTHHmmssfffZ}-{hash[..12]}";

    private static string GetPackageFileName(string packageKind) =>
        $"package.{NormalizePackageKind(packageKind)}";

    private static IReadOnlyList<string> NormalizeAllowedPackageKinds(
        IReadOnlyList<string>? allowedPackageKinds) =>
        (allowedPackageKinds is { Count: > 0 }
            ? allowedPackageKinds
            : ["zip", "tar.gz"])
        .Where(kind => !string.IsNullOrWhiteSpace(kind))
        .Select(NormalizePackageKind)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(kind => kind, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string NormalizePackageKind(string packageKind) =>
        packageKind.Trim().ToLowerInvariant();

    private static long NormalizeMaxUploadBytes(long maxUploadBytes) =>
        maxUploadBytes > 0 ? maxUploadBytes : 256L * 1024L * 1024L;

    private static int NormalizeTimeoutMinutes(int timeoutMinutes) =>
        timeoutMinutes > 0 ? timeoutMinutes : 60;

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizePathSegment(string value)
    {
        var normalized = new string(value
            .Select(character => char.IsLetterOrDigit(character) ||
                character is '-' or '_' or '.'
                    ? character
                    : '-')
            .ToArray())
            .Trim('-', '.', '_');

        return string.IsNullOrWhiteSpace(normalized) ? "artifact" : normalized;
    }

    private sealed record UploadSessionMetadata(
        string UploadId,
        string? ResourceId,
        string ResourceType,
        string ResourceName,
        string PackageKind,
        string? FileName,
        long? ContentLength,
        string? ContentSha256,
        string? ArtifactLayoutKind,
        DateTimeOffset CreatedAt,
        DateTimeOffset ExpiresAt);
}
