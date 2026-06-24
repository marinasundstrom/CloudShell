namespace CloudShell.ResourceDefinitions;

public interface IResourceChangeApplyProvider
{
    ResourceTypeId TypeId { get; }

    bool CanApply(ResourceChangeSet changes);

    ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceChangeApplyContext(
    string? EnvironmentId = null,
    string? PrincipalId = null,
    bool Commit = false);

public sealed record ResourceChangeApplyResult(
    ResourceChangeSet ChangeSet,
    ResourceState? AcceptedState,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);

    public bool IsAccepted => AcceptedState is not null && !HasErrors;

    public ResourceDefinition? ToAcceptedDefinition() =>
        AcceptedState?.ToDefinition();

    public static ResourceChangeApplyResult Accepted(ResourceChangeSet changeSet) =>
        new(changeSet, changeSet.ProposedState, changeSet.Diagnostics);

    public static ResourceChangeApplyResult Rejected(
        ResourceChangeSet changeSet,
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        new(changeSet, null, diagnostics.ToArray());
}

public sealed class ResourceChangeApplyDispatcher(
    IEnumerable<IResourceChangeApplyProvider> providers)
{
    private readonly IReadOnlyList<IResourceChangeApplyProvider> _providers =
        providers.ToArray();

    public async ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(context);

        if (!changes.HasChanges)
        {
            return ResourceChangeApplyResult.Accepted(changes);
        }

        if (changes.HasErrors)
        {
            return ResourceChangeApplyResult.Rejected(changes, changes.Diagnostics);
        }

        var provider = _providers.FirstOrDefault(provider =>
            provider.TypeId == changes.Resource.Type.TypeId &&
            provider.CanApply(changes));

        if (provider is null)
        {
            return ResourceChangeApplyResult.Rejected(
                changes,
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceChangeApplyProviderMissing,
                        $"No change apply provider is registered for resource type '{changes.Resource.Type.TypeId}'.",
                        changes.Resource.EffectiveResourceId)
                ]);
        }

        return await provider.ApplyChangesAsync(changes, context, cancellationToken);
    }
}
