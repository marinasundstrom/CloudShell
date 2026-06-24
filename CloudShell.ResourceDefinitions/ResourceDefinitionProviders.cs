namespace CloudShell.ResourceDefinitions;

public interface IResourceTypeProvider
{
    ResourceTypeId TypeId { get; }

    ResourceTypeDefinition TypeDefinition { get; }

    bool CanValidate(ResolvedResourceDefinition resource);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResolvedResourceDefinition resource,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceDefinitionCapabilityProvider
{
    ResourceCapabilityId CapabilityId { get; }

    bool CanValidate(
        ResolvedResourceDefinition resource,
        ResourceCapabilityResolution capability);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResolvedResourceDefinition resource,
        ResourceCapabilityResolution capability,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceOperationProvider
{
    ResourceOperationId OperationId { get; }

    ResourceDefinitionValueSource ResolutionLevel { get; }

    bool CanHandle(
        ResolvedResourceDefinition resource,
        ResourceOperationResolution operation);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        ResolvedResourceDefinition resource,
        ResourceOperationResolution operation,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceDefinitionValidationContext(
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed class ResourceDefinitionProviderDispatcher(
    IEnumerable<IResourceTypeProvider> typeProviders,
    IEnumerable<IResourceDefinitionCapabilityProvider> capabilityProviders,
    IEnumerable<IResourceOperationProvider> operationProviders)
{
    private readonly IReadOnlyList<IResourceTypeProvider> _typeProviders =
        typeProviders.ToArray();
    private readonly IReadOnlyList<IResourceDefinitionCapabilityProvider> _capabilityProviders =
        capabilityProviders.ToArray();
    private readonly IReadOnlyList<IResourceOperationProvider> _operationProviders =
        operationProviders.ToArray();

    public async ValueTask<ResourceDefinitionValidationResult> ValidateResourceTypeAsync(
        ResolvedResourceDefinition resource,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = _typeProviders.FirstOrDefault(provider =>
            provider.TypeId == resource.TypeDefinition.TypeId &&
            provider.CanValidate(resource));

        if (provider is null)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceTypeProviderMissing,
                        $"No resource type provider is registered for resource type '{resource.TypeDefinition.TypeId}'.",
                        resource.TypeDefinition.TypeId)
                ]);
        }

        return await provider.ValidateAsync(resource, context, cancellationToken);
    }

    public async ValueTask<ResourceDefinitionValidationResult> ValidateCapabilitiesAsync(
        ResolvedResourceDefinition resource,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var capability in resource.Capabilities)
        {
            var provider = _capabilityProviders.FirstOrDefault(provider =>
                provider.CapabilityId == capability.Id &&
                provider.CanValidate(resource, capability));

            if (provider is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.CapabilityProviderMissing,
                    $"No capability provider is registered for capability '{capability.Id}'.",
                    capability.Id));
                continue;
            }

            var result = await provider.ValidateAsync(resource, capability, context, cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics(diagnostics);
    }

    public async ValueTask<ResourceDefinitionValidationResult> ValidateOperationsAsync(
        ResolvedResourceDefinition resource,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var operation in resource.Operations)
        {
            var provider = _operationProviders.FirstOrDefault(provider =>
                provider.OperationId == operation.Id &&
                provider.ResolutionLevel == operation.Source &&
                provider.CanHandle(resource, operation));

            if (provider is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.OperationProviderMissing,
                    $"No operation provider is registered for operation '{operation.Id}' at the {operation.Source} level.",
                    operation.Id));
                continue;
            }

            var result = await provider.ValidateAsync(resource, operation, context, cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics(diagnostics);
    }
}
