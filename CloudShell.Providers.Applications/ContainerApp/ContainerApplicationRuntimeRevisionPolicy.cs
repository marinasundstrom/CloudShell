namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationRuntimeRevisionPolicy
{
    public bool ShouldUseRevisionScopedRuntimeInstances(
        ApplicationResourceDefinition application,
        string? revision,
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisionHistory)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(revisionHistory);
        if (string.IsNullOrWhiteSpace(revision))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(application.DeploymentEnvironmentRevisionId) ||
            revisionHistory.Any(entry =>
                string.Equals(entry.Id, revision, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Status, ApplicationContainerRevisionStatuses.Active, StringComparison.OrdinalIgnoreCase));
    }
}
