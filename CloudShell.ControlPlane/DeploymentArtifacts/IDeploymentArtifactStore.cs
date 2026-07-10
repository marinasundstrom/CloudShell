using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.DeploymentArtifacts;

public interface IDeploymentArtifactStore
{
    DeploymentArtifactStoreStatus GetStatus();

    Task<DeploymentArtifactUploadSession> CreateUploadSessionAsync(
        CreateDeploymentArtifactUploadSessionCommand command,
        CancellationToken cancellationToken = default);

    Task WriteUploadContentAsync(
        string uploadId,
        Stream content,
        CancellationToken cancellationToken = default);

    Task<DeploymentArtifactRevision> CompleteUploadAsync(
        CompleteDeploymentArtifactUploadCommand command,
        CancellationToken cancellationToken = default);

    Task<DeploymentArtifactRevision?> GetRevisionAsync(
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default);
}
