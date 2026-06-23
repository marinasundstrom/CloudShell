namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentFailurePlanner
{
    public ContainerApplicationDeploymentFailurePlan PlanApplyFailure(
        ApplicationResourceDefinition application,
        string orchestratorDeploymentId,
        string revisionId,
        ApplicationContainerDeployment? appDeployment,
        Func<ApplicationResourceDefinition, ApplicationResourceDefinition> normalize)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorDeploymentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(revisionId);
        ArgumentNullException.ThrowIfNull(normalize);

        var basedOnRevisionId =
            NormalizeNullable(appDeployment?.BasedOnRevisionId) ??
            application.ContainerRevisions
                .FirstOrDefault(revision =>
                    string.Equals(revision.Id, revisionId, StringComparison.OrdinalIgnoreCase))
                ?.BasedOnRevisionId;

        return new ContainerApplicationDeploymentFailurePlan(
            appDeployment?.Id ?? orchestratorDeploymentId,
            revisionId,
            basedOnRevisionId,
            CreateRestoredDefinition(application, revisionId, basedOnRevisionId, normalize));
    }

    private static ApplicationResourceDefinition? CreateRestoredDefinition(
        ApplicationResourceDefinition application,
        string failedRevisionId,
        string? basedOnRevisionId,
        Func<ApplicationResourceDefinition, ApplicationResourceDefinition> normalize)
    {
        if (string.IsNullOrWhiteSpace(basedOnRevisionId))
        {
            return null;
        }

        var basedOnRevision = application.ContainerRevisions.FirstOrDefault(revision =>
            string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase));
        if (basedOnRevision is null)
        {
            return null;
        }

        return normalize(application with
        {
            ContainerImage = basedOnRevision.Image,
            ContainerRevision = basedOnRevision.Id,
            Replicas = Math.Max(1, basedOnRevision.RequestedReplicas),
            ReplicasEnabled = basedOnRevision.RequestedReplicas > 1,
            ContainerRevisions = application.ContainerRevisions
                .Where(revision => !string.Equals(
                    revision.Id,
                    failedRevisionId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray()
        });
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed record ContainerApplicationDeploymentFailurePlan(
    string DeploymentId,
    string RevisionId,
    string? BasedOnRevisionId,
    ApplicationResourceDefinition? RestoredDefinition);
