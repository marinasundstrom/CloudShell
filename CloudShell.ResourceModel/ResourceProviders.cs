namespace CloudShell.ResourceModel;

public interface IResourceTypeProvider
{
    ResourceTypeId TypeId { get; }

    ResourceTypeDefinition TypeDefinition { get; }

    bool CanValidate(Resource resource);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceCapabilityProvider
{
    ResourceCapabilityId CapabilityId { get; }

    bool CanValidate(
        Resource resource,
        ResourceCapabilityResolution capability);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceCapabilityResolution capability,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default);
}

public interface IResourceOperationProvider
{
    ResourceOperationId OperationId { get; }

    ResourceDefinitionValueSource ResolutionLevel { get; }

    bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation);

    ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default);
}

public sealed record ResourceDefinitionValidationContext(
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed record ResourceProviderContext(
    string? EnvironmentId = null,
    string? PrincipalId = null);

public sealed class ResourceProviderDispatcher(
    IEnumerable<IResourceTypeProvider> typeProviders,
    IEnumerable<IResourceCapabilityProvider> capabilityProviders,
    IEnumerable<IResourceOperationProvider> operationProviders)
{
    private readonly IReadOnlyList<IResourceTypeProvider> _typeProviders =
        typeProviders.ToArray();
    private readonly IReadOnlyList<IResourceCapabilityProvider> _capabilityProviders =
        capabilityProviders.ToArray();
    private readonly IReadOnlyList<IResourceOperationProvider> _operationProviders =
        operationProviders.ToArray();

    public async ValueTask<ResourceDefinitionValidationResult> ValidateResourceTypeAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = _typeProviders.FirstOrDefault(provider =>
            provider.TypeId == resource.Type.TypeId &&
            provider.CanValidate(resource));

        if (provider is null)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceTypeProviderMissing,
                        $"No resource type provider is registered for resource type '{resource.Type.TypeId}'.",
                        resource.Type.TypeId)
                ]);
        }

        return await provider.ValidateAsync(resource, context, cancellationToken);
    }

    public async ValueTask<ResourceDefinitionValidationResult> ValidateCapabilitiesAsync(
        Resource resource,
        ResourceProviderContext context,
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
                continue;
            }

            var result = await provider.ValidateAsync(resource, capability, context, cancellationToken);
            diagnostics.AddRange(result.Diagnostics);
        }

        return ResourceDefinitionValidationResult.FromDiagnostics(diagnostics);
    }

    public async ValueTask<ResourceDefinitionValidationResult> ValidateOperationsAsync(
        Resource resource,
        ResourceProviderContext context,
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
