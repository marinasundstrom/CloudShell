namespace CloudShell.Abstractions.ResourceManager;

public interface IResourceOrchestrationSettings
{
    ResourceOrchestratorSelection Get();

    ResourceHealthCheckIntervalSettings GetHealthCheckIntervalSettings();

    ResourceDependencyStartFailureSettings GetDependencyStartFailureSettings();

    void Select(
        string orchestratorId,
        string? preferredContainerHostId = null,
        int healthCheckIntervalSeconds = ResourceOrchestratorSelectionDefaults.DefaultHealthCheckIntervalSeconds,
        DependencyStartFailureBehavior dependencyStartFailureBehavior = DependencyStartFailureBehavior.FailAction);
}

public static class ResourceOrchestratorSelectionDefaults
{
    public const int DefaultHealthCheckIntervalSeconds = 10;
    public const int MinimumHealthCheckIntervalSeconds = 1;
    public const int MaximumHealthCheckIntervalSeconds = 3600;

    public static int NormalizeHealthCheckInterval(int value) =>
        Math.Clamp(
            value,
            MinimumHealthCheckIntervalSeconds,
            MaximumHealthCheckIntervalSeconds);
}

public sealed record ResourceOrchestratorSelection(
    string OrchestratorId,
    string? PreferredContainerHostId,
    int HealthCheckIntervalSeconds,
    DateTimeOffset UpdatedAt,
    DependencyStartFailureBehavior DependencyStartFailureBehavior = DependencyStartFailureBehavior.FailAction)
{
    public static ResourceOrchestratorSelection Default { get; } = new(
        "default",
        null,
        ResourceOrchestratorSelectionDefaults.DefaultHealthCheckIntervalSeconds,
        DateTimeOffset.MinValue,
        DependencyStartFailureBehavior.FailAction);
}

public sealed record ResourceHealthCheckIntervalSettings(
    int Seconds,
    bool IsConfigured);

public sealed record ResourceDependencyStartFailureSettings(
    DependencyStartFailureBehavior Behavior,
    bool IsConfigured);

public enum DependencyStartFailureBehavior
{
    FailAction,
    WarnAndContinue
}
