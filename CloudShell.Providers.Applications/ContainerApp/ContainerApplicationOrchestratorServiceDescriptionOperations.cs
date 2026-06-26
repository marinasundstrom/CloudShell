using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationOrchestratorServiceDescriptionOperations(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationWorkloadConfigurationProvider workloadConfigurations) :
    IContainerApplicationOrchestratorServiceDescriptionOperations
{
    private static readonly ApplicationContainerRevisionService RevisionService = new();
    private static readonly ContainerApplicationRuntimeRevisionPolicy RuntimeRevisionPolicy = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory OrchestratorDeploymentFactory = new();

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop or ResourceActionKind.Restart;

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        var revision = RevisionService.GetEffectiveRevision(application);
        return Task.FromResult(OrchestratorDeploymentFactory.CreateDeployment(
            application,
            context.Resource.State ?? ResourceState.Unknown,
            workloadConfigurations.Create(application),
            useRuntimeRevisionScopedInstances: RuntimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
                application,
                revision,
                containerDeployments.ListRevisions(application.Id)))
            .Spec
            .Service);
    }
}
