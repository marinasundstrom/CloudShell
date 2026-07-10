using CloudShell.ResourceModel;

namespace CloudShell.Abstractions.ResourceManager;

public static class DeploymentArtifactSourceKinds
{
    public const string LocalPath = "localPath";
    public const string UploadedArtifact = "uploadedArtifact";
    public const string Git = "git";
    public const string ContainerImage = "containerImage";
}

public sealed record DeploymentArtifactStoreStatus(
    bool IsEnabled,
    string Kind,
    long MaxUploadBytes,
    IReadOnlyList<string> AllowedPackageKinds);

public sealed record DeploymentArtifactLayoutDescriptor(
    ResourceTypeId ResourceTypeId,
    string Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<string> PackageKinds,
    string? DefaultPackageKind = null,
    string? DefaultEntryPath = null,
    bool EntryPathRequired = false,
    bool IsDefault = false,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public IReadOnlyList<string> SupportedPackageKinds => PackageKinds;

    public IReadOnlyDictionary<string, string> LayoutMetadata => Metadata ?? new Dictionary<string, string>();
}

public sealed record DeploymentArtifactLayoutQuery(
    ResourceTypeId ResourceTypeId,
    string? ProviderId = null,
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed record CreateDeploymentArtifactUploadSessionCommand(
    string ResourceType,
    string ResourceName,
    string PackageKind,
    string? FileName = null,
    long? ContentLength = null,
    string? ContentSha256 = null,
    string? ArtifactLayoutKind = null);

public sealed record DeploymentArtifactUploadSession(
    string UploadId,
    DateTimeOffset ExpiresAt,
    long MaxUploadBytes,
    IReadOnlyList<string> AllowedPackageKinds);

public sealed record CompleteDeploymentArtifactUploadCommand(
    string UploadId,
    string? ContentSha256 = null);

public sealed record DeploymentArtifactRevision(
    string ArtifactId,
    string RevisionId,
    string PackageKind,
    string ContentSha256,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    string? ArtifactLayoutKind = null);

public sealed record DeploymentArtifactValidationContext(
    string ResourceType,
    string ResourceName,
    string ArtifactId,
    string RevisionId,
    string PackageKind,
    string ContentSha256,
    long SizeBytes,
    string? EntryPath = null,
    string? ArtifactLayoutKind = null);

public sealed record ValidateDeploymentArtifactCommand(
    string ResourceType,
    string ResourceName,
    string ArtifactId,
    string RevisionId,
    string? EntryPath = null,
    string? ArtifactLayoutKind = null);

public interface IDeploymentArtifactValidationProvider
{
    string Id { get; }

    bool CanValidate(DeploymentArtifactValidationContext context);

    ValueTask<ResourceDefinitionValidationResult> ValidateDeploymentArtifactAsync(
        DeploymentArtifactValidationContext context,
        Stream artifactContent,
        CancellationToken cancellationToken = default);
}

public interface IDeploymentArtifactLayoutProvider
{
    ResourceTypeId TypeId { get; }

    ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default);
}
