using CloudShell.Abstractions.ResourceManager;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.ResourceManager.Recovery;

public interface IResourceRecoveryStore
{
    ResourceRecoveryPolicy? GetPolicy(string resourceId);

    IReadOnlyDictionary<string, ResourceRecoveryPolicy> GetPolicies();

    void SetPolicy(string resourceId, ResourceRecoveryPolicy policy);

    void ClearPolicy(string resourceId);

    ResourceRecoveryRuntimeState GetRuntimeState(string resourceId);

    void SetRuntimeState(string resourceId, ResourceRecoveryRuntimeState state);

    void ClearRuntimeState(string resourceId);
}

public sealed record ResourceRecoveryRuntimeState(
    ResourceRecoveryState State = ResourceRecoveryState.Disabled,
    int ConsecutiveFailures = 0,
    int AttemptCount = 0,
    DateTimeOffset? LastCheckedAt = null,
    DateTimeOffset? LastHealthyAt = null,
    DateTimeOffset? LastAttemptAt = null,
    DateTimeOffset? NextAttemptAt = null,
    string? LastDetail = null);

public sealed class InMemoryResourceRecoveryStore : IResourceRecoveryStore
{
    private readonly ConcurrentDictionary<string, ResourceRecoveryPolicy> policies =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, ResourceRecoveryRuntimeState> runtimeStates =
        new(StringComparer.OrdinalIgnoreCase);

    public ResourceRecoveryPolicy? GetPolicy(string resourceId) =>
        policies.GetValueOrDefault(resourceId);

    public IReadOnlyDictionary<string, ResourceRecoveryPolicy> GetPolicies() =>
        new Dictionary<string, ResourceRecoveryPolicy>(policies, StringComparer.OrdinalIgnoreCase);

    public void SetPolicy(string resourceId, ResourceRecoveryPolicy policy) =>
        policies[resourceId] = policy;

    public void ClearPolicy(string resourceId)
    {
        policies.TryRemove(resourceId, out _);
        ClearRuntimeState(resourceId);
    }

    public ResourceRecoveryRuntimeState GetRuntimeState(string resourceId) =>
        runtimeStates.GetValueOrDefault(resourceId) ?? new ResourceRecoveryRuntimeState();

    public void SetRuntimeState(string resourceId, ResourceRecoveryRuntimeState state) =>
        runtimeStates[resourceId] = state;

    public void ClearRuntimeState(string resourceId) =>
        runtimeStates.TryRemove(resourceId, out _);
}
