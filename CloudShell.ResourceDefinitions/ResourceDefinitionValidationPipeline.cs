namespace CloudShell.ResourceDefinitions;

public sealed class ResourceDefinitionValidationPipeline
{
    private readonly ResourceResolver _resolver;
    private readonly ResourceProviderDispatcher _dispatcher;
    private readonly ResourceCapabilityResolver _capabilityResolver;
    private readonly ResourceOperationResolver _operationResolver;

    public ResourceDefinitionValidationPipeline(
        IEnumerable<ResourceClassDefinition> classDefinitions,
        IEnumerable<IResourceTypeProvider> typeProviders,
        IEnumerable<IResourceCapabilityProvider>? capabilityProviders = null,
        IEnumerable<IResourceOperationProvider>? operationProviders = null,
        IEnumerable<IResourceAttributeValidator>? attributeValidators = null,
        IEnumerable<IResourceCapabilityProjector>? capabilityProjectors = null,
        IEnumerable<IResourceOperationProjector>? operationProjectors = null)
    {
        ArgumentNullException.ThrowIfNull(classDefinitions);
        ArgumentNullException.ThrowIfNull(typeProviders);

        var resolvedClassDefinitions = classDefinitions
            .GroupBy(classDefinition => classDefinition.ClassId)
            .Select(group => group.Last())
            .ToArray();
        var materializedTypeProviders = typeProviders.ToArray();
        _resolver = new(
            resolvedClassDefinitions,
            materializedTypeProviders.Select(provider => provider.TypeDefinition),
            attributeValidators);
        _dispatcher = new(
            materializedTypeProviders,
            capabilityProviders ?? [],
            operationProviders ?? []);
        _capabilityResolver = new(capabilityProjectors ?? []);
        _operationResolver = new(operationProjectors ?? []);
    }

    public async ValueTask<ResourceDefinitionValidationPipelineResult> ValidateAsync(
        ResourceDefinition definition,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(context);

        var resolved = _resolver.Resolve(
            definition,
            new ResourceDefinitionResolutionContext(
                context.EnvironmentId,
                context.PrincipalId));
        var diagnostics = new List<ResourceDefinitionDiagnostic>(resolved.Diagnostics);
        var providerContext = new ResourceProviderContext(
            context.EnvironmentId,
            context.PrincipalId);

        var typeResult = await _dispatcher.ValidateResourceTypeAsync(
            resolved,
            providerContext,
            cancellationToken);
        diagnostics.AddRange(typeResult.Diagnostics);

        var capabilityResult = await _dispatcher.ValidateCapabilitiesAsync(
            resolved,
            providerContext,
            cancellationToken);
        diagnostics.AddRange(capabilityResult.Diagnostics);

        var operationResult = await _dispatcher.ValidateOperationsAsync(
            resolved,
            providerContext,
            cancellationToken);
        diagnostics.AddRange(operationResult.Diagnostics);

        await _capabilityResolver.BindAsync(
            resolved,
            new ResourceCapabilityProjectionContext(
                context.EnvironmentId,
                context.PrincipalId),
            cancellationToken);
        await _operationResolver.BindAsync(
            resolved,
            new ResourceOperationProjectionContext(
                context.EnvironmentId,
                context.PrincipalId),
            cancellationToken);

        return new(resolved, diagnostics);
    }
}

public sealed record ResourceDefinitionValidationPipelineResult(
    Resource Resource,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
