using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class ProjectResourceIdentityEnvironmentResolver(
    IEnumerable<IResourceIdentityCredentialEnvironmentProvider> credentialEnvironmentProviders,
    ResourceIdentityProviderCatalog? identityProviders = null,
    ResourceDeclarationStore? declarations = null) :
    IAspNetCoreProjectRuntimeEnvironmentProvider,
    IJavaScriptAppRuntimeEnvironmentProvider,
    IJavaAppRuntimeEnvironmentProvider,
    IGoAppRuntimeEnvironmentProvider,
    IPythonAppRuntimeEnvironmentProvider
{
    private const string DefaultScope = "ControlPlane.Access";
    private readonly IReadOnlyList<IResourceIdentityCredentialEnvironmentProvider> credentialEnvironmentProviders =
        credentialEnvironmentProviders.ToArray();

    public ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
        Resource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (!IsSupportedProjectResource(resource) ||
            GetIdentityBinding(resource) is not { } binding)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var providerCatalog = declarations?.CreateIdentityProviderCatalog(
                identityProviders ?? new ResourceIdentityProviderCatalog()) ??
            identityProviders ??
            new ResourceIdentityProviderCatalog();
        var resolution = providerCatalog.Resolve(binding);
        if (resolution.Provider is null)
        {
            return ValueTask.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        var credentialProvider = credentialEnvironmentProviders.FirstOrDefault(provider =>
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

    private static bool IsSupportedProjectResource(Resource resource) =>
        resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == JavaAppResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == GoAppResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == PythonAppResourceTypeProvider.ResourceTypeId ||
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId;

    private ResourceIdentityBinding? GetIdentityBinding(Resource resource)
    {
        if (declarations?.GetDeclaration(resource.EffectiveResourceId)?.IdentityBinding is { } binding)
        {
            return binding;
        }

        return resource.State.ToDefinition().GetIdentityAttribute() is { } attribute
            ? ToIdentityBinding(attribute)
            : null;
    }

    private static ResourceIdentityBinding? ToIdentityBinding(
        ResourceIdentityBindingAttribute attribute)
    {
        if (string.Equals(
                attribute.Kind,
                ResourceIdentityBindingAttributeKinds.Provider,
                StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(attribute.ProviderId)
                ? null
                : new ResourceIdentityBinding(
                    attribute.ProviderId,
                    attribute.Subject,
                    attribute.Scopes,
                    attribute.Claims,
                    ResourceIdentityBindingKind.Provider,
                    attribute.Name);
        }

        if (string.Equals(
                attribute.Kind,
                ResourceIdentityBindingAttributeKinds.Required,
                StringComparison.OrdinalIgnoreCase))
        {
            return new ResourceIdentityBinding(
                attribute.ProviderId,
                attribute.Subject,
                attribute.Scopes,
                attribute.Claims,
                ResourceIdentityBindingKind.Required,
                attribute.Name);
        }

        return null;
    }
}
