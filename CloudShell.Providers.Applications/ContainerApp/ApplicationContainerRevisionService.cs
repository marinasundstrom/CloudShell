namespace CloudShell.Providers.Applications;

internal sealed class ApplicationContainerRevisionService(Func<DateTimeOffset>? utcNow = null)
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public IReadOnlyList<ApplicationContainerRevision> AppendRevision(
        ApplicationResourceDefinition application,
        string revisionId,
        string image,
        int requestedReplicas,
        string changeKind,
        string? triggeredBy)
    {
        var revisions = application.ContainerRevisions.ToList();
        var basedOnRevisionId = NormalizeNullable(application.ContainerRevision);
        if (!string.IsNullOrWhiteSpace(basedOnRevisionId) &&
            revisions.All(revision => !string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase)))
        {
            revisions.Add(new ApplicationContainerRevision(
                basedOnRevisionId,
                NormalizeNullable(application.ContainerImage) ?? "unresolved",
                Math.Max(1, application.Replicas),
                utcNow(),
                ApplicationContainerRevisionChangeKinds.Initial));
        }

        revisions.RemoveAll(revision =>
            string.Equals(revision.Id, revisionId, StringComparison.OrdinalIgnoreCase));
        revisions.Add(new ApplicationContainerRevision(
            revisionId,
            image,
            Math.Max(1, requestedReplicas),
            utcNow(),
            changeKind,
            basedOnRevisionId,
            NormalizeNullable(triggeredBy)));
        return AssignRevisionNumbers(revisions);
    }

    public ApplicationContainerRevisionHistoryEntry? CreateBasedOnHistoryEntry(
        ApplicationResourceDefinition application,
        string? basedOnRevisionId)
    {
        if (string.IsNullOrWhiteSpace(basedOnRevisionId))
        {
            return null;
        }

        var basedOnRevision = application.ContainerRevisions.FirstOrDefault(revision =>
            string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase));
        return new ApplicationContainerRevisionHistoryEntry(
            basedOnRevisionId,
            application.Id,
            NormalizeNullable(basedOnRevision?.Image) ?? NormalizeNullable(application.ContainerImage) ?? "unresolved",
            Math.Max(1, basedOnRevision?.RequestedReplicas ?? application.Replicas),
            basedOnRevision?.CreatedAt ?? utcNow(),
            ApplicationContainerRevisionStatuses.Superseded,
            NormalizeNullable(basedOnRevision?.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.Initial,
            NormalizeNullable(basedOnRevision?.BasedOnRevisionId),
            NormalizeNullable(basedOnRevision?.ProvisionedBy),
            RevisionNumber: Math.Max(0, basedOnRevision?.RevisionNumber ?? 0));
    }

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> CreateHistoryEntries(
        ApplicationResourceDefinition application) =>
        application.ContainerRevisions
            .Where(revision => !string.IsNullOrWhiteSpace(revision.Id))
            .Select(revision => new ApplicationContainerRevisionHistoryEntry(
                revision.Id,
                application.Id,
                NormalizeNullable(revision.Image) ?? NormalizeNullable(application.ContainerImage) ?? "unresolved",
                Math.Max(1, revision.RequestedReplicas),
                revision.CreatedAt,
                string.Equals(revision.Id, application.ContainerRevision, StringComparison.OrdinalIgnoreCase)
                    ? ApplicationContainerRevisionStatuses.Active
                    : ApplicationContainerRevisionStatuses.Superseded,
                NormalizeNullable(revision.ChangeKind) ?? ApplicationContainerRevisionChangeKinds.ImageDeployment,
                NormalizeNullable(revision.BasedOnRevisionId),
                NormalizeNullable(revision.ProvisionedBy),
                RevisionNumber: Math.Max(0, revision.RevisionNumber)))
            .ToArray();

    public IReadOnlyList<ApplicationContainerRevision> AssignRevisionNumbers(
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

    public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> AssignHistoryNumbers(
        IReadOnlyList<ApplicationContainerRevisionHistoryEntry> revisions)
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

    public string GetEffectiveRevision(ApplicationResourceDefinition application) =>
        NormalizeNullable(application.ContainerRevision) ?? "unrevisioned";

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
