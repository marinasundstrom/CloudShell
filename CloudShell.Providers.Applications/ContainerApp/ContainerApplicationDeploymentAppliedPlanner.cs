using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentAppliedPlanner
{
    public ApplicationResourceDefinition? PlanAppliedDeployment(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        Func<ApplicationResourceDefinition, ApplicationResourceDefinition> normalize)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(applyResult);
        ArgumentNullException.ThrowIfNull(normalize);

        var environmentRevisionId = applyResult.Revision.Id.ToString();
        if (string.IsNullOrWhiteSpace(environmentRevisionId))
        {
            return null;
        }

        return normalize(application with
        {
            DeploymentEnvironmentRevisionId = environmentRevisionId
        });
    }
}
