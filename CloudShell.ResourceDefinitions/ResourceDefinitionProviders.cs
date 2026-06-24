namespace CloudShell.ResourceDefinitions;

public interface IResourceDefinitionCapabilityProvider
{
    string CapabilityId { get; }

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
    string OperationId { get; }

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
    IEnumerable<IResourceDefinitionCapabilityProvider> capabilityProviders,
    IEnumerable<IResourceOperationProvider> operationProviders)
{
    private readonly IReadOnlyList<IResourceDefinitionCapabilityProvider> _capabilityProviders =
        capabilityProviders.ToArray();
    private readonly IReadOnlyList<IResourceOperationProvider> _operationProviders =
        operationProviders.ToArray();

    public async ValueTask<ResourceDefinitionValidationResult> ValidateCapabilitiesAsync(
        ResolvedResourceDefinition resource,
        ResourceDefinitionValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var capability in resource.Capabilities)
        {
            var provider = _capabilityProviders.FirstOrDefault(provider =>
                string.Equals(provider.CapabilityId, capability.Id, StringComparison.OrdinalIgnoreCase) &&
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
                string.Equals(provider.OperationId, operation.Id, StringComparison.OrdinalIgnoreCase) &&
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
