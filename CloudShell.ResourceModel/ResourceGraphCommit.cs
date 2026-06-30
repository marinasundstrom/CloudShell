namespace CloudShell.ResourceModel;

public sealed record ResourceGraphCommitContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    DateTimeOffset? Timestamp = null);

public sealed record ResourceGraphCommitResult(
    ResourceGraphVersion Version,
    ResourceGraphSnapshot? Snapshot,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics,
    ResourceGraphCommitSummary Summary)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsCommitted => Snapshot is not null && !HasErrors;
}

public enum ResourceGraphCommitStatus
{
    Committed,
    NoChanges,
    Rejected,
    VersionConflict
}

public sealed record ResourceGraphCommitSummary(
    ResourceGraphCommitStatus Status,
    ResourceGraphVersion BaseVersion,
    ResourceGraphVersion ResultVersion,
    int AcceptedResourceCount,
    int AttributeChangeCount,
    int CapabilityChangeCount,
    IReadOnlyList<ResourceGraphResourceChangeSummary> Resources,
    string Message)
{
    public static ResourceGraphCommitSummary NoChanges(
        ResourceGraphVersion version) =>
        new(
            ResourceGraphCommitStatus.NoChanges,
            version,
            version,
            0,
            0,
            0,
            [],
            "No resource graph changes were committed.");

    public static ResourceGraphCommitSummary Rejected(
        ResourceGraphChangeSet changes,
        ResourceGraphVersion currentVersion) =>
        new(
            ResourceGraphCommitStatus.Rejected,
            changes.BaseVersion,
            currentVersion,
            changes.AcceptedResources.Count,
            CountAttributeChanges(changes),
            CountCapabilityChanges(changes),
            SummarizeAcceptedResources(changes, snapshot: null),
            "Resource graph changes were rejected.");

    public static ResourceGraphCommitSummary VersionConflict(
        ResourceGraphVersion expectedVersion,
        ResourceGraphVersion currentVersion) =>
        new(
            ResourceGraphCommitStatus.VersionConflict,
            expectedVersion,
            currentVersion,
            0,
            0,
            0,
            [],
            $"Resource graph version '{expectedVersion}' is stale. Current version is '{currentVersion}'.");

    public static ResourceGraphCommitSummary Committed(
        ResourceGraphChangeSet changes,
        ResourceGraphVersion committedVersion,
        ResourceGraphSnapshot snapshot) =>
        new(
            ResourceGraphCommitStatus.Committed,
            changes.BaseVersion,
            committedVersion,
            changes.AcceptedResources.Count,
            CountAttributeChanges(changes),
            CountCapabilityChanges(changes),
            SummarizeAcceptedResources(changes, snapshot),
            $"Committed {changes.AcceptedResources.Count} resource change(s).");

    private static int CountAttributeChanges(ResourceGraphChangeSet changes) =>
        changes.AcceptedResources.Sum(resource => resource.ChangeSet.AttributeChanges.Count);

    private static int CountCapabilityChanges(ResourceGraphChangeSet changes) =>
        changes.AcceptedResources.Sum(resource => resource.ChangeSet.CapabilityChanges.Count);

    private static IReadOnlyList<ResourceGraphResourceChangeSummary> SummarizeAcceptedResources(
        ResourceGraphChangeSet changes,
        ResourceGraphSnapshot? snapshot) =>
        changes.AcceptedResources
            .Select(resource =>
            {
                var resourceId = resource.ChangeSet.Resource.EffectiveResourceId;
                var committedState = snapshot?.Resources.FirstOrDefault(state =>
                    string.Equals(
                        state.EffectiveResourceId,
                        resourceId,
                        StringComparison.OrdinalIgnoreCase));

                return new ResourceGraphResourceChangeSummary(
                    resourceId,
                    resource.ChangeSet.Resource.Type.TypeId,
                    resource.ChangeSet.Resource.Revision,
                    committedState?.Revision,
                    resource.ChangeSet.AttributeChanges
                        .Select(change => change.AttributeId)
                        .ToArray(),
                    resource.ChangeSet.CapabilityChanges
                        .Select(change => change.CapabilityId)
                        .ToArray());
            })
            .ToArray();
}

public sealed record ResourceGraphResourceChangeSummary(
    string ResourceId,
    ResourceTypeId TypeId,
    ResourceRevision PreviousRevision,
    ResourceRevision? CommittedRevision,
    IReadOnlyList<ResourceAttributeId> AttributeChanges,
    IReadOnlyList<ResourceCapabilityId> CapabilityChanges);
