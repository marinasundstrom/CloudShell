using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ControlPlaneIdentityProviderContext
{
    public ControlPlaneIdentityProviderContext(ResourceIdentityProviderDefinition provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        Provider = provider;
    }

    public ResourceIdentityProviderDefinition Provider { get; }

    public string Id => Provider.Id;

    public string Name => Provider.Name;

    public ResourceIdentityProviderKind Kind => Provider.Kind;

    public IReadOnlyDictionary<string, string> ProviderSettings => Provider.ProviderSettings;

    public string? ProvisioningResourceId => Provider.ProvisioningResourceId;

    public ResourcePrincipalReference GetUser(
        string userName,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        return new ResourcePrincipalReference(
            ResourcePrincipalKind.User,
            userName.Trim(),
            displayName,
            Provider.Id);
    }

    public static implicit operator ResourceIdentityProviderDefinition(
        ControlPlaneIdentityProviderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Provider;
    }
}
