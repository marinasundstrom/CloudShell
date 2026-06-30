using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceModel;

public static class ControlPlaneIdentityProviderDefinitionExtensions
{
    public static ResourcePrincipalReference GetUser(
        this ResourceIdentityProviderDefinition provider,
        string userName,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);

        return new ResourcePrincipalReference(
            ResourcePrincipalKind.User,
            userName.Trim(),
            displayName,
            provider.Id);
    }
}
