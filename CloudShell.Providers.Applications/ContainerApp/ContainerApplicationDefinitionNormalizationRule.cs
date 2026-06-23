namespace CloudShell.Providers.Applications;

public sealed class ContainerApplicationDefinitionNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) =>
        ApplicationResourceTypes.IsContainerApp(definition.ResourceType);

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        if (!ApplicationResourceProjectionSupport.IsContainerBacked(definition))
        {
            return definition with
            {
                ContainerRevision = null,
                ContainerRevisions = [],
                ReplicaManagementPolicy = null,
                ReplicasEnabled = false
            };
        }

        var replicasEnabled = definition.ReplicasEnabled || definition.Replicas > 1;
        var containerRevision = NormalizeNullable(definition.ContainerRevision) ?? CreateContainerRevision();
        return definition with
        {
            ContainerRevision = containerRevision,
            ContainerRevisions = NormalizeContainerRevisions(definition, containerRevision),
            ReplicasEnabled = replicasEnabled
        };
    }

    private static IReadOnlyList<ApplicationContainerRevision> NormalizeContainerRevisions(
        ApplicationResourceDefinition definition,
        string containerRevision)
    {
        var revisions = definition.ContainerRevisions
            .Where(revision => !string.IsNullOrWhiteSpace(revision.Id))
            .Select(revision => revision with
            {
                Id = revision.Id.Trim(),
                Image = NormalizeNullable(revision.Image) ?? NormalizeNullable(definition.ContainerImage) ?? "unresolved",
                RequestedReplicas = Math.Max(1, revision.RequestedReplicas),
                ChangeKind = NormalizeNullable(revision.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.ImageDeployment,
                BasedOnRevisionId = NormalizeNullable(revision.BasedOnRevisionId),
                ProvisionedBy = NormalizeNullable(revision.ProvisionedBy),
                RevisionNumber = Math.Max(0, revision.RevisionNumber)
            })
            .DistinctBy(revision => revision.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (revisions.Any(revision => string.Equals(revision.Id, containerRevision, StringComparison.OrdinalIgnoreCase)))
        {
            return AssignRevisionNumbers(revisions);
        }

        return AssignRevisionNumbers([
            ..revisions,
            new ApplicationContainerRevision(
                containerRevision,
                NormalizeNullable(definition.ContainerImage) ?? "unresolved",
                Math.Max(1, definition.Replicas),
                DateTimeOffset.UtcNow,
                ApplicationContainerRevisionChangeKinds.Initial)
        ]);
    }

    private static IReadOnlyList<ApplicationContainerRevision> AssignRevisionNumbers(
        IReadOnlyList<ApplicationContainerRevision> revisions)
    {
        var numbers = revisions
            .OrderBy(revision => revision.CreatedAt)
            .ThenBy(revision => revision.Id, StringComparer.OrdinalIgnoreCase)
            .Select((revision, index) => new { revision.Id, Number = index + 1 })
            .ToDictionary(item => item.Id, item => item.Number, StringComparer.OrdinalIgnoreCase);
        return revisions
            .Select(revision => revision with { RevisionNumber = numbers[revision.Id] })
            .ToArray();
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CreateContainerRevision() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];
}
