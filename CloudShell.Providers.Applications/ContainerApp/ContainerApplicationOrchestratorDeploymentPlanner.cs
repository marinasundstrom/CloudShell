using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationOrchestratorDeploymentPlanner(
    ApplicationContainerRevisionService? revisions = null,
    ContainerApplicationRuntimeRevisionPolicy? runtimeRevisionPolicy = null,
    ApplicationContainerOrchestratorDeploymentFactory? deployments = null)
{
    private readonly ApplicationContainerRevisionService revisions = revisions ?? new();
    private readonly ContainerApplicationRuntimeRevisionPolicy runtimeRevisionPolicy =
        runtimeRevisionPolicy ?? new();
    private readonly ApplicationContainerOrchestratorDeploymentFactory deployments =
        deployments ?? new();

    public ContainerApplicationOrchestratorDeploymentPlan PlanDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        ResourceWorkloadConfiguration workload,
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisionHistory)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(workload);
        ArgumentNullException.ThrowIfNull(revisionHistory);

        var revision = revisions.GetEffectiveRevision(application);
        var deployment = deployments.CreateDeployment(
            application,
            state,
            workload,
            useRuntimeRevisionScopedInstances:
                runtimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
                    application,
                    revision,
                    revisionHistory));
        var definition = deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId);

        return new ContainerApplicationOrchestratorDeploymentPlan(
            deployment with
            {
                Spec = deployment.Spec with
                {
                    Definition = definition
                }
            },
            definition);
    }
}

internal sealed record ContainerApplicationOrchestratorDeploymentPlan(
    ResourceOrchestratorDeployment Deployment,
    ResourceOrchestratorDeploymentDefinition Definition);
