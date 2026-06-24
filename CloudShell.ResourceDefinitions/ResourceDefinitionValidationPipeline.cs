namespace CloudShell.ResourceDefinitions;

public sealed class ResourceDefinitionValidationPipeline
{
    private readonly ResourceResolver _resolver;
    private readonly ResourceDefinitionProviderDispatcher _dispatcher;
    private readonly ResourceCapabilityResolver _capabilityResolver;

    public ResourceDefinitionValidationPipeline(
        IEnumerable<ResourceClassDefinition> classDefinitions,
        IEnumerable<IResourceTypeProvider> typeProviders,
        IEnumerable<IResourceCapabilityProvider>? capabilityProviders = null,
        IEnumerable<IResourceOperationProvider>? operationProviders = null,
        IEnumerable<IResourceAttributeValidator>? attributeValidators = null,
        IEnumerable<IResourceCapabilityProjector>? capabilityProjectors = null)
    {
        ArgumentNullException.ThrowIfNull(classDefinitions);
        ArgumentNullException.ThrowIfNull(typeProviders);

        var materializedTypeProviders = typeProviders.ToArray();
        _resolver = new(
            classDefinitions,
            materializedTypeProviders.Select(provider => provider.TypeDefinition),
            attributeValidators);
        _dispatcher = new(
            materializedTypeProviders,
            capabilityProviders ?? [],
            operationProviders ?? []);
        _capabilityResolver = new(capabilityProjectors ?? []);
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

        var typeResult = await _dispatcher.ValidateResourceTypeAsync(
            resolved,
            context,
            cancellationToken);
        diagnostics.AddRange(typeResult.Diagnostics);

        var capabilityResult = await _dispatcher.ValidateCapabilitiesAsync(
            resolved,
            context,
            cancellationToken);
        diagnostics.AddRange(capabilityResult.Diagnostics);

        var operationResult = await _dispatcher.ValidateOperationsAsync(
            resolved,
            context,
            cancellationToken);
        diagnostics.AddRange(operationResult.Diagnostics);

        var projection = new ResourceDefinitionProjection(
            resolved,
            _capabilityResolver,
            new ResourceCapabilityProjectionContext(
                context.EnvironmentId,
                context.PrincipalId));

        return new(resolved, projection, diagnostics);
    }
}

public sealed record ResourceDefinitionValidationPipelineResult(
    Resource Resource,
    ResourceDefinitionProjection Projection,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(diagnostic =>
        diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
}
