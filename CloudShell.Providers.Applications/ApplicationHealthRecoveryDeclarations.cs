using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationHealthRecoveryDeclarations
{
    public static IReadOnlyList<ResourceHealthCheck> NormalizeHealthChecks(
        IReadOnlyList<ResourceHealthCheck> healthChecks) =>
        healthChecks
            .Where(check => check.Source is not null || !string.IsNullOrWhiteSpace(check.Path))
            .Select(check => check with
            {
                Path = check.Path.Trim(),
                EndpointName = string.IsNullOrWhiteSpace(check.EndpointName) ? null : check.EndpointName.Trim(),
                Name = string.IsNullOrWhiteSpace(check.Name) ? check.Type.ToString().ToLowerInvariant() : check.Name.Trim(),
                IntervalSeconds = check.IntervalSeconds is null
                    ? null
                    : ResourceOrchestratorSelectionDefaults.NormalizeHealthCheckInterval(check.IntervalSeconds.Value)
            })
            .ToArray();

    public static IReadOnlyList<ResourceRecoveryPolicy> NormalizeRecoveryPolicies(
        IReadOnlyList<ResourceRecoveryPolicy> policies) =>
        policies
            .Select(policy => policy with
            {
                ProbeName = string.IsNullOrWhiteSpace(policy.ProbeName) ? null : policy.ProbeName.Trim(),
                FailureThreshold = Math.Clamp(policy.FailureThreshold, 1, 100),
                StartupGracePeriodSeconds = Math.Clamp(policy.StartupGracePeriodSeconds, 0, 86_400),
                InitialBackoffSeconds = Math.Clamp(policy.InitialBackoffSeconds, 1, 86_400),
                MaxBackoffSeconds = Math.Clamp(
                    Math.Max(policy.MaxBackoffSeconds, policy.InitialBackoffSeconds),
                    1,
                    86_400),
                BackoffMultiplier = Math.Clamp(policy.BackoffMultiplier, 1, 100),
                MaxAttempts = Math.Clamp(policy.MaxAttempts, 1, 10_000),
                ResetAfterHealthySeconds = Math.Clamp(policy.ResetAfterHealthySeconds, 0, 86_400)
            })
            .ToArray();
}
