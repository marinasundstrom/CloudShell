namespace CloudShell.ResourceDefinitions;

public sealed record ResourceGraphChangeSet(
    ResourceGraphVersion BaseVersion,
    IReadOnlyList<ResourceChangeApplyResult> Resources,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasChanges => Resources.Any(resource => resource.ChangeSet.HasChanges);

    public bool HasErrors =>
        Diagnostics.Any(diagnostic => diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error) ||
        Resources.Any(resource => resource.HasErrors);

    public IReadOnlyList<ResourceChangeApplyResult> AcceptedResources =>
        Resources.Where(resource => resource.IsAccepted).ToArray();

    public IEnumerable<ResourceDefinition> ToIncrementalDefinitions() =>
        AcceptedResources.Select(resource => resource.ChangeSet.ToIncrementalDefinition());
}

public sealed class ResourceGraphChangeTracker(ResourceGraphSnapshot snapshot)
{
    private readonly List<ResourceChangeApplyResult> _resources = [];
    private readonly List<ResourceDefinitionDiagnostic> _diagnostics = [];

    public ResourceGraphVersion BaseVersion { get; } = snapshot.Version;

    public void Track(ResourceChangeApplyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        _resources.Add(result);
        _diagnostics.AddRange(result.Diagnostics);
    }

    public void TrackDiagnostic(ResourceDefinitionDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);

        _diagnostics.Add(diagnostic);
    }

    public ResourceGraphChangeSet GetChanges() =>
        new(BaseVersion, _resources.ToArray(), _diagnostics.ToArray());
}
