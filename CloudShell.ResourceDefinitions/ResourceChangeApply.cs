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

        var readOnlyDiagnostics = ValidateReadOnlyAttributeChanges(changes);
        if (readOnlyDiagnostics.Count > 0)
        {
            return ResourceChangeApplyResult.Rejected(
                changes,
                [.. changes.Diagnostics, .. readOnlyDiagnostics]);
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

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateReadOnlyAttributeChanges(
        ResourceChangeSet changes)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var change in changes.AttributeChanges)
        {
            if (IsReadOnlyAttribute(changes.Resource, change.AttributeId))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange,
                    $"Attribute '{change.AttributeId}' is read-only and cannot be changed through resource definition apply.",
                    change.AttributeId));
            }
        }

        return diagnostics;
    }

    private static bool IsReadOnlyAttribute(
        Resource resource,
        ResourceAttributeId attributeId)
    {
        var hasClassDefinition =
            TryGetReadOnly(resource.Class.Definition.Attributes, attributeId, out var classReadOnly);
        var hasTypeDefinition =
            TryGetReadOnly(resource.Type.Definition.Attributes, attributeId, out var typeReadOnly);

        if (hasTypeDefinition)
        {
            return typeReadOnly == true;
        }

        if (hasClassDefinition)
        {
            return classReadOnly == true;
        }

        return resource.Attributes.Resolve(attributeId)?.ReadOnly == true;
    }

    private static bool TryGetReadOnly(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions,
        ResourceAttributeId attributeId,
        out bool? readOnly)
    {
        if (attributeDefinitions is not null &&
            attributeDefinitions.TryGetValue(attributeId, out var attributeDefinition) &&
            attributeDefinition.ReadOnly.HasValue)
        {
            readOnly = attributeDefinition.ReadOnly;
            return true;
        }

        readOnly = null;
        return false;
    }
}

public sealed class ResourceDefinitionGraphChangeApplier(
    ResourceResolver resolver,
    ResourceChangeApplyDispatcher applyDispatcher,
    IEnumerable<IResourceDefinitionGraphValidator>? graphValidators = null)
{
    private readonly IReadOnlyList<IResourceDefinitionGraphValidator> _graphValidators =
        (graphValidators ?? []).ToArray();

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

        if (!tracker.GetChanges().HasErrors)
        {
            await ValidateProposedGraphAsync(
                snapshot,
                tracker,
                context,
                cancellationToken);
        }

        return tracker.GetChanges();
    }

    private async ValueTask ValidateProposedGraphAsync(
        ResourceGraphSnapshot snapshot,
        ResourceGraphChangeTracker tracker,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken)
    {
        if (_graphValidators.Count == 0)
        {
            return;
        }

        var changes = tracker.GetChanges();
        var acceptedById = changes.AcceptedResources
            .Where(resource => resource.AcceptedState is not null)
            .ToDictionary(
                resource => resource.AcceptedState!.EffectiveResourceId,
                resource => resource.AcceptedState!,
                StringComparer.OrdinalIgnoreCase);
        var proposedStates = new List<ResourceState>();
        var replacedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in snapshot.Resources)
        {
            if (acceptedById.TryGetValue(existing.EffectiveResourceId, out var accepted))
            {
                proposedStates.Add(accepted);
                replacedIds.Add(existing.EffectiveResourceId);
                continue;
            }

            proposedStates.Add(existing);
        }

        proposedStates.AddRange(acceptedById
            .Where(resource => !replacedIds.Contains(resource.Key))
            .Select(resource => resource.Value));

        var resolutionContext = ToResolutionContext(context);
        var resources = proposedStates
            .Select(state => resolver.Resolve(state, resolutionContext))
            .ToArray();
        var graphContext = new ResourceDefinitionGraphValidationContext(
            new ResourceDefinitionGraph(proposedStates
                .Select(state => state.ToDefinition())
                .ToArray()),
            resources,
            context.EnvironmentId,
            context.PrincipalId);

        foreach (var validator in _graphValidators)
        {
            var result = await validator.ValidateAsync(
                graphContext,
                cancellationToken);
            foreach (var diagnostic in result.Diagnostics)
            {
                tracker.TrackDiagnostic(diagnostic);
            }
        }
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
            foreach (var dependency in definition.ResourceDependencyIds)
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
