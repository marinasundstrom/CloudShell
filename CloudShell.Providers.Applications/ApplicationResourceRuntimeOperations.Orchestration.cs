using CloudShell.Abstractions.ResourceManager;
namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceRuntimeOperations
{
    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        ContainerOrchestratorDeploymentFactory.CreateService(
            application,
            CreateWorkloadConfiguration(application));

    private ResourceOrchestratorService CreateActiveContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        CreateDefaultContainerOrchestratorDeployment(
            application,
            GetState(application.Id),
            runtimeRevisionScoped: true)
            .Spec
            .Service;

    private ResourceOrchestratorDeployment CreateDefaultContainerOrchestratorDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        bool runtimeRevisionScoped = false)
    {
        var revision = GetEffectiveContainerRevision(application);
        return ContainerOrchestratorDeploymentFactory.CreateDeployment(
            application,
            state,
            CreateWorkloadConfiguration(application),
            runtimeRevisionScoped &&
                ShouldUseRevisionScopedRuntimeInstances(application, revision));
    }

    private bool ShouldUseRevisionScopedRuntimeInstances(
        ApplicationResourceDefinition application,
        string revision) =>
        ContainerRuntimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
            application,
            revision,
            containerDeployments.ListRevisions(application.Id));

    private static ResourceOrchestratorReplicaGroup CreateDefaultContainerReplicaGroup(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

}
