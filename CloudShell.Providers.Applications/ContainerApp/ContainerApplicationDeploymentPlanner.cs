namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentPlanner(
    Func<DateTimeOffset>? utcNow = null,
    Func<string>? createRevisionId = null,
    Func<string>? createDeploymentId = null)
{
    private readonly Func<DateTimeOffset> _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly Func<string> _createRevisionId = createRevisionId ?? CreateContainerRevisionId;
    private readonly Func<string> _createDeploymentId = createDeploymentId ?? CreateContainerDeploymentId;
    private readonly ApplicationContainerRevisionService _revisionService =
        new(utcNow ?? (() => DateTimeOffset.UtcNow));

    public ContainerApplicationImageDeploymentPlan PlanImageDeployment(
        ApplicationResourceDefinition application,
        string image,
        int requestedReplicas,
        bool requestedReplicasSpecified,
        string? triggeredBy,
        string orchestratorDeploymentId,
        Func<ApplicationResourceDefinition, ApplicationResourceDefinition> normalize)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorDeploymentId);
        ArgumentNullException.ThrowIfNull(normalize);

        var normalizedImage = image.Trim();
        var nextRevision = _createRevisionId();
        var deploymentId = _createDeploymentId();
        var createdAt = _utcNow();
        var updated = normalize(application with
        {
            ContainerImage = normalizedImage,
            ContainerBuildContext = null,
            ContainerDockerfile = null,
            ContainerRevision = nextRevision,
            Replicas = Math.Max(1, requestedReplicas),
            ReplicasEnabled = requestedReplicasSpecified
                ? application.ReplicasEnabled || requestedReplicas > 1
                : application.ReplicasEnabled,
            ContainerRevisions = _revisionService.AppendRevision(
                application,
                nextRevision,
                normalizedImage,
                requestedReplicas,
                ApplicationContainerRevisionChangeKinds.ImageDeployment,
                triggeredBy)
        });
        var basedOnRevisionId = NormalizeNullable(application.ContainerRevision);
        var currentRevisionRecord = updated.ContainerRevisions.First(revision =>
            string.Equals(revision.Id, nextRevision, StringComparison.OrdinalIgnoreCase));
        return new ContainerApplicationImageDeploymentPlan(
            updated,
            new ApplicationContainerDeployment(
                deploymentId,
                application.Id,
                nextRevision,
                basedOnRevisionId,
                normalizedImage,
                Math.Max(1, requestedReplicas),
                createdAt,
                ApplicationContainerDeploymentStatuses.Completed,
                ApplicationContainerRevisionChangeKinds.ImageDeployment,
                NormalizeNullable(triggeredBy),
                orchestratorDeploymentId),
            new ApplicationContainerRevisionHistoryEntry(
                nextRevision,
                application.Id,
                normalizedImage,
                Math.Max(1, requestedReplicas),
                createdAt,
                ApplicationContainerRevisionStatuses.Active,
                ApplicationContainerRevisionChangeKinds.ImageDeployment,
                basedOnRevisionId,
                NormalizeNullable(triggeredBy),
                deploymentId,
                currentRevisionRecord.RevisionNumber),
            _revisionService.CreateBasedOnHistoryEntry(application, basedOnRevisionId));
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateContainerRevisionId() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];

    private static string CreateContainerDeploymentId() =>
        $"dep-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];
}

internal sealed record ContainerApplicationImageDeploymentPlan(
    ApplicationResourceDefinition Definition,
    ApplicationContainerDeployment Deployment,
    ApplicationContainerRevisionHistoryEntry Revision,
    ApplicationContainerRevisionHistoryEntry? BasedOnRevision);
