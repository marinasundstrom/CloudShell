using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.DeploymentArtifacts;

public interface IDeploymentArtifactStore : IDeploymentArtifactContentStore
{
    DeploymentArtifactStoreStatus GetStatus();

    Task<DeploymentArtifactUploadSession> CreateUploadSessionAsync(
        CreateDeploymentArtifactUploadSessionCommand command,
        CancellationToken cancellationToken = default);

    Task WriteUploadContentAsync(
        string resourceId,
        string uploadId,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<DeploymentArtifactRevision> CompleteUploadAsync(
        string resourceId,
        CompleteDeploymentArtifactUploadCommand command,
        CancellationToken cancellationToken = default);

    Task<DeploymentArtifactRevision?> GetRevisionAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default);

    Task<Stream> OpenRevisionContentAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default);

    Task DeleteResourceArtifactsAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<Stream> IDeploymentArtifactContentStore.OpenDeploymentArtifactContentAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken) =>
        OpenRevisionContentAsync(resourceId, artifactId, revisionId, cancellationToken);
}
