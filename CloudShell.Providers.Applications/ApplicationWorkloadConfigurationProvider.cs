using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationWorkloadConfigurationProvider(
    ApplicationResourceEnvironmentVariableResolver environmentVariables)
{
    private static readonly ApplicationWorkloadConfigurationFactory WorkloadConfigurationFactory = new();

    public ResourceWorkloadConfiguration Create(
        ApplicationResourceDefinition application,
        string? resourceGroupId = null,
        IResourceManagerStore? resourceManager = null) =>
        WorkloadConfigurationFactory.Create(
            application,
            environmentVariables.ResolveWorkloadEnvironmentVariables(application, resourceGroupId, resourceManager),
            GetEffectiveObservability(application));

    public ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        environmentVariables.GetEffectiveObservability(definition);
}
