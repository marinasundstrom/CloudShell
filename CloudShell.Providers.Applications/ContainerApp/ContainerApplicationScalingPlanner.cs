namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationScalingPlanner
{
    public ContainerApplicationScalingPlan PlanReplicaUpdate(
        ApplicationResourceDefinition application,
        int replicas,
        Func<ApplicationResourceDefinition, ApplicationResourceDefinition> normalize)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(normalize);
        if (replicas < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replicas),
                replicas,
                "Replicas must be greater than or equal to 1.");
        }

        return new ContainerApplicationScalingPlan(
            normalize(application with
            {
                Replicas = replicas,
                ReplicasEnabled = true
            }));
    }
}

internal sealed record ContainerApplicationScalingPlan(
    ApplicationResourceDefinition Definition);
