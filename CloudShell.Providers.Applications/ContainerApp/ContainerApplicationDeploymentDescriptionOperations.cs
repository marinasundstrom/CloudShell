using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentDescriptionOperations(
    ApplicationResourceStore store,
    ApplicationRuntimeStateStore runtimeStates,
    IApplicationResourceRunningStateOperations runningState,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationWorkloadConfigurationProvider workloadConfigurations,
    ContainerApplicationOrchestratorDeploymentPlanner deploymentPlanner) :
    IContainerApplicationDeploymentDescriptionOperations
{
    private static readonly TimeSpan StartingStateTimeout = TimeSpan.FromMinutes(5);

    public bool CanDescribeDeployment(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = GetContainerApplication(context.Resource.Id);
        var state = context.Resource.State ?? GetState(application.Id);
        return Task.FromResult<ResourceOrchestratorDeployment?>(
            deploymentPlanner.PlanDeployment(
                application,
                state,
                workloadConfigurations.Create(application),
                containerDeployments.ListRevisions(application.Id))
                .Deployment);
    }

    private ResourceState GetState(string applicationId) =>
        new ApplicationRuntimeStateTracker(
            runtimeStates,
            runningState.IsRunning,
            transientStateTimeout: StartingStateTimeout)
            .GetState(applicationId);

    private ApplicationResourceDefinition GetContainerApplication(string resourceId)
    {
        var application = store.GetApplication(resourceId)
            ?? throw new InvalidOperationException(
                $"Container app resource '{resourceId}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is not a container app.");
        }

        return application;
    }
}
