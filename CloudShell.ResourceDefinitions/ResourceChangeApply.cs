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

public sealed class ResourceDefinitionGraphChangeApplier(
    ResourceResolver resolver,
    ResourceChangeApplyDispatcher applyDispatcher)
{
    public async ValueTask<ResourceGraphChangeSet> ApplyDefinitionsAsync(
        ResourceGraphSnapshot snapshot,
        IEnumerable<ResourceDefinition> definitions,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default) =>
        await ApplyDefinitionsAsync(
            snapshot,
            definitions,
            context,
            ResourceDefinitionGraphChangeApplierOptions.Default,
            cancellationToken);

    public async ValueTask<ResourceGraphChangeSet> ApplyDefinitionsAsync(
        ResourceGraphSnapshot snapshot,
        IEnumerable<ResourceDefinition> definitions,
        ResourceChangeApplyContext context,
        ResourceDefinitionGraphChangeApplierOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        var incomingDefinitions = definitions.ToArray();
        var tracker = new ResourceGraphChangeTracker(snapshot);
        var statesById = snapshot.Resources.ToDictionary(
            resource => resource.EffectiveResourceId,
            resource => resource,
            StringComparer.OrdinalIgnoreCase);

        foreach (var diagnostic in ValidateIncomingDefinitions(
            incomingDefinitions,
            statesById.Keys))
        {
            tracker.TrackDiagnostic(diagnostic);
        }

        if (tracker.GetChanges().HasErrors)
        {
            return tracker.GetChanges();
        }

        foreach (var definition in incomingDefinitions)
        {
            if (!statesById.TryGetValue(definition.EffectiveResourceId, out var state))
            {
                if (options.CreateMissingResources)
                {
                    var createdResource = resolver.Resolve(
                        definition,
                        ToResolutionContext(context));
                    var createdChanges = ResourceChangeSet.FromNewResource(createdResource);
                    var acceptedCreate = await applyDispatcher.ApplyChangesAsync(
                        createdChanges,
                        context,
                        cancellationToken);

                    tracker.Track(acceptedCreate);
                    continue;
                }

                tracker.TrackDiagnostic(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                    $"Resource '{definition.EffectiveResourceId}' does not exist in the resource graph.",
                    definition.EffectiveResourceId));
                continue;
            }

            var resource = resolver.Resolve(
                state,
                ToResolutionContext(context));
            var changes = resource.ApplyDefinition(definition);
            var accepted = await applyDispatcher.ApplyChangesAsync(
                changes,
                context,
                cancellationToken);

            tracker.Track(accepted);
        }

        return tracker.GetChanges();
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateIncomingDefinitions(
        IReadOnlyList<ResourceDefinition> definitions,
        IEnumerable<string> existingResourceIds)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var incomingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            if (!incomingIds.Add(definition.EffectiveResourceId))
            {
                duplicateIds.Add(definition.EffectiveResourceId);
            }
        }

        foreach (var duplicateId in duplicateIds)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.DuplicateResourceDefinition,
                $"Resource definition '{duplicateId}' is declared more than once in the graph.",
                duplicateId));
        }

        var knownResourceIds = new HashSet<string>(existingResourceIds, StringComparer.OrdinalIgnoreCase);
        knownResourceIds.UnionWith(incomingIds);

        foreach (var definition in definitions)
        {
            foreach (var dependency in definition.ResourceDependencies)
            {
                if (!knownResourceIds.Contains(dependency))
                {
                    diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceDependencyMissing,
                        $"Resource definition '{definition.EffectiveResourceId}' depends on missing resource '{dependency}'.",
                        definition.EffectiveResourceId));
                }
            }
        }

        return diagnostics;
    }

    private static ResourceDefinitionResolutionContext ToResolutionContext(
        ResourceChangeApplyContext context) =>
        new(context.EnvironmentId, context.PrincipalId);
}

public sealed record ResourceDefinitionGraphChangeApplierOptions(
    bool CreateMissingResources = false)
{
    public static ResourceDefinitionGraphChangeApplierOptions Default { get; } = new();

    public static ResourceDefinitionGraphChangeApplierOptions CreateMissing { get; } =
        new(CreateMissingResources: true);
}
