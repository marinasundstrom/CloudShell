using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;

namespace CloudShell.ControlPlane.Authentication;

public sealed class BuiltInResourceIdentityCredentialEnvironmentProvider(
    IConfiguration configuration) : IResourceIdentityCredentialEnvironmentProvider
{
    private const string TokenEndpointEnvironmentVariable = "CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT";
    private const string ClientIdEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_ID";
    private const string ClientSecretEnvironmentVariable = "CLOUDSHELL_IDENTITY_CLIENT_SECRET";
    private const string ScopeEnvironmentVariable = "CLOUDSHELL_IDENTITY_SCOPE";
    private const string DefaultControlPlaneEndpoint = "http://127.0.0.1:5112";

    public string ProviderId => "built-in";

    public bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider) =>
        provider.Kind == ResourceIdentityProviderKind.BuiltIn;

    public IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
        ResourceIdentityCredentialEnvironmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientId = BuiltInResourceIdentityRegistry.CreateClientId(request.Identity);
        var tokenEndpoint = ResolveTokenEndpoint();
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return [];
        }

        return
        [
            new(TokenEndpointEnvironmentVariable, tokenEndpoint),
            new(ClientIdEnvironmentVariable, clientId),
            new(
                ClientSecretEnvironmentVariable,
                BuiltInResourceIdentityRegistry.ResolveClientSecret(request.Provider, clientId)),
            new(ScopeEnvironmentVariable, request.DefaultScope)
        ];
    }

    private string ResolveTokenEndpoint()
    {
        var endpoint =
            configuration["CloudShell:Identity:TokenEndpoint"] ??
            configuration["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint.Trim();
        }

        var controlPlane =
            configuration["CloudShell:ControlPlane:BaseAddress"] ??
            configuration["CloudShell:PublicEndpoint"] ??
            configuration["CLOUDSHELL_CONTROL_PLANE_ENDPOINT"] ??
            configuration["CLOUDSHELL_CONTROL_PLANE_URL"] ??
            DefaultControlPlaneEndpoint;
        return $"{controlPlane.Trim().TrimEnd('/')}/api/auth/v1/token";
    }
}
