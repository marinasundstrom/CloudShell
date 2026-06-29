using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel.ReferenceProviders;
using GraphResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ThirdPartyIdentity;

public sealed class ThirdPartyIdentityAspNetCoreProjectIdentityEnvironmentProvider(
    ResourceDeclarationStore declarations,
    ResourceIdentityProviderCatalog identityProviders,
    IEnumerable<IResourceIdentityCredentialEnvironmentProvider> credentialEnvironmentProviders) :
    IAspNetCoreProjectRuntimeEnvironmentProvider
{
    private const string DefaultScope = "ControlPlane.Access";

    private readonly IReadOnlyList<IResourceIdentityCredentialEnvironmentProvider> _credentialEnvironmentProviders =
        credentialEnvironmentProviders.ToArray();

    public ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != AspNetCoreProjectResourceTypeProvider.ResourceTypeId ||
            declarations.GetDeclaration(resource.EffectiveResourceId)?.IdentityBinding is not { } binding)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var providerCatalog = declarations.CreateIdentityProviderCatalog(identityProviders);
        var resolution = providerCatalog.Resolve(binding);
        if (resolution.Provider is null)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var credentialProvider = _credentialEnvironmentProviders.FirstOrDefault(provider =>
            provider.CanCreateEnvironment(resolution.Provider));
        if (credentialProvider is null)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var identity = ResourceIdentityReference.ForResource(
            resource.EffectiveResourceId,
            binding.Name);
        var scope = binding.IdentityScopes.Count == 0
            ? DefaultScope
            : binding.IdentityScopes[0];
        var variables = credentialProvider
            .CreateEnvironment(new ResourceIdentityCredentialEnvironmentRequest(
                resolution.Provider,
                identity,
                binding,
                scope))
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key.Trim(),
                group => group.Last().Value ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);

        return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(variables);
    }
}
