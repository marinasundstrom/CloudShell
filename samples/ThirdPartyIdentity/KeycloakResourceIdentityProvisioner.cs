using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ThirdPartyIdentity;

public sealed class KeycloakResourceIdentityProvisioner(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) :
    IResourceIdentityProvisioner,
    IResourceIdentityProvisioningStatusProvider
{
    public string ProviderId => "keycloak";

    public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public bool CanGetProvisioningStatus(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public async Task<ResourceIdentityProvisioningResult> ProvisionAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ResourceIdentityProvisioningDiagnostic>();
        var client = httpClientFactory.CreateClient();
        var token = await GetAdminAccessTokenAsync(client, request.Provider, cancellationToken);

        foreach (var entry in request.Identities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var keycloakClientId = ResolveKeycloakClientId(entry);
            var keycloakClient = await EnsureClientAsync(
                client,
                request.Provider,
                token,
                keycloakClientId,
                entry,
                cancellationToken);
            var grants = request.PermissionGrants
                .Where(grant => Matches(entry.Identity, grant.Identity))
                .ToArray();

            foreach (var grant in grants)
            {
                await EnsureClientRoleAsync(
                    client,
                    request.Provider,
                    token,
                    keycloakClient.Id,
                    CreateGrantRoleName(grant),
                    grant,
                    cancellationToken);
            }

            diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                ResourceIdentityProvisioningDiagnosticSeverity.Information,
                grants.Length == 0
                    ? $"Provisioned Keycloak client '{keycloakClientId}'. No CloudShell grants were declared for this identity."
                    : $"Provisioned Keycloak client '{keycloakClientId}' with {grants.Length} CloudShell grant role(s).",
                entry.Identity,
                request.Provider.Id));
        }

        return new ResourceIdentityProvisioningResult(request.Provider.Id, diagnostics);
    }

    public async Task<ResourceIdentityProvisioningStatusResult> GetProvisioningStatusAsync(
        ResourceIdentityProvisioningRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ResourceIdentityProvisioningDiagnostic>();
        var statuses = new List<ResourceIdentityProvisioningStatus>();
        var client = httpClientFactory.CreateClient();

        try
        {
            var token = await GetAdminAccessTokenAsync(client, request.Provider, cancellationToken);
            foreach (var entry in request.Identities)
            {
                var keycloakClientId = ResolveKeycloakClientId(entry);
                var existing = await FindClientAsync(
                    client,
                    request.Provider,
                    token,
                    keycloakClientId,
                    cancellationToken);
                statuses.Add(new ResourceIdentityProvisioningStatus(
                    entry.Identity,
                    existing is null
                        ? ResourceIdentityProvisioningState.NotProvisioned
                        : ResourceIdentityProvisioningState.Provisioned,
                    existing is null
                        ? $"Keycloak client '{keycloakClientId}' was not found."
                        : $"Keycloak client '{keycloakClientId}' exists.",
                    DateTimeOffset.UtcNow));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                ResourceIdentityProvisioningDiagnosticSeverity.Error,
                exception.Message,
                ProviderId: request.Provider.Id));
            statuses.AddRange(request.Identities.Select(entry => new ResourceIdentityProvisioningStatus(
                entry.Identity,
                ResourceIdentityProvisioningState.Unknown,
                "Keycloak provisioning status could not be read.",
                DateTimeOffset.UtcNow)));
        }

        return new ResourceIdentityProvisioningStatusResult(
            request.Provider.Id,
            statuses,
            diagnostics);
    }

    private static bool IsKeycloakProvider(ResourceIdentityProviderDefinition provider) =>
        provider.Kind == ResourceIdentityProviderKind.Oidc &&
        (SettingEquals(provider, "Provider", "Keycloak") ||
         provider.Name.Contains("Keycloak", StringComparison.OrdinalIgnoreCase));

    private async Task<string> GetAdminAccessTokenAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        CancellationToken cancellationToken)
    {
        var adminBaseAddress = ResolveSetting(provider, "AdminBaseAddress", "Keycloak:AdminBaseAddress") ??
            "http://localhost:8080";
        var username = configuration["Keycloak:AdminUsername"] ?? "admin";
        var password = configuration["Keycloak:AdminPassword"] ?? "admin";
        var tokenEndpoint = $"{adminBaseAddress.TrimEnd('/')}/realms/master/protocol/openid-connect/token";
        using var request = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = username,
                ["password"] = password
            })
        };
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var token = await response.Content.ReadFromJsonAsync<KeycloakTokenResponse>(
            cancellationToken);
        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new InvalidOperationException("Keycloak admin token response did not contain an access token.");
        }

        return token.AccessToken;
    }

    private static async Task<KeycloakClientRepresentation> EnsureClientAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string clientId,
        ResourceIdentityProvisioningEntry entry,
        CancellationToken cancellationToken)
    {
        var existing = await FindClientAsync(client, provider, token, clientId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            CreateAdminUri(provider, "clients"),
            token,
            new
            {
                clientId,
                name = entry.Binding.Name ?? entry.Identity.Name ?? entry.Identity.ResourceId,
                enabled = true,
                protocol = "openid-connect",
                publicClient = false,
                serviceAccountsEnabled = true,
                standardFlowEnabled = false,
                directAccessGrantsEnabled = false
            });
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await FindClientAsync(client, provider, token, clientId, cancellationToken)
            ?? throw new InvalidOperationException($"Keycloak client '{clientId}' was created but could not be read.");
    }

    private static async Task<KeycloakClientRepresentation?> FindClientAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string clientId,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"{CreateAdminUri(provider, "clients")}?clientId={Uri.EscapeDataString(clientId)}",
            token);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var clients = await response.Content.ReadFromJsonAsync<KeycloakClientRepresentation[]>(
            cancellationToken) ?? [];
        return clients.FirstOrDefault(item =>
            string.Equals(item.ClientId, clientId, StringComparison.Ordinal));
    }

    private static async Task EnsureClientRoleAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        string roleName,
        ResourcePermissionGrant grant,
        CancellationToken cancellationToken)
    {
        using var readRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/roles/{Uri.EscapeDataString(roleName)}"),
            token);
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        if (readResponse.IsSuccessStatusCode)
        {
            return;
        }

        if (readResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
        {
            await EnsureSuccessAsync(readResponse, cancellationToken);
        }

        using var createRequest = CreateJsonRequest(
            HttpMethod.Post,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/roles"),
            token,
            new
            {
                name = roleName,
                description = $"CloudShell grant: {grant.TargetResourceId} {grant.Permission}"
            });
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        await EnsureSuccessAsync(createResponse, cancellationToken);
    }

    private static HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string uri,
        string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string uri,
        string token,
        object value)
    {
        var request = CreateAuthorizedRequest(method, uri, token);
        request.Content = JsonContent.Create(value);
        return request;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            string.IsNullOrWhiteSpace(body)
                ? $"Keycloak request failed with HTTP {(int)response.StatusCode}."
                : $"Keycloak request failed with HTTP {(int)response.StatusCode}: {body}");
    }

    private static string CreateAdminUri(
        ResourceIdentityProviderDefinition provider,
        string path)
    {
        var adminBaseAddress = ResolveSetting(provider, "AdminBaseAddress") ??
            "http://localhost:8080";
        var realm = ResolveSetting(provider, "Realm") ?? "cloudshell";
        return $"{adminBaseAddress.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(realm)}/{path}";
    }

    private static string ResolveKeycloakClientId(ResourceIdentityProvisioningEntry entry)
    {
        const string clientPrefix = "client:";
        if (!string.IsNullOrWhiteSpace(entry.Binding.Subject) &&
            entry.Binding.Subject.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return entry.Binding.Subject[clientPrefix.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(entry.Identity.Name)
            ? Sanitize(entry.Identity.ResourceId)
            : Sanitize(entry.Identity.Name);
    }

    private static string CreateGrantRoleName(ResourcePermissionGrant grant) =>
        Sanitize($"{grant.TargetResourceId}-{grant.Permission}");

    private static bool Matches(
        ResourceIdentityReference declaredIdentity,
        ResourceIdentityReference grantIdentity) =>
        string.Equals(declaredIdentity.ResourceId, grantIdentity.ResourceId, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(grantIdentity.Name) ||
         string.Equals(declaredIdentity.Name, grantIdentity.Name, StringComparison.OrdinalIgnoreCase));

    private static bool SettingEquals(
        ResourceIdentityProviderDefinition provider,
        string key,
        string value) =>
        provider.ProviderSettings.TryGetValue(key, out var setting) &&
        string.Equals(setting, value, StringComparison.OrdinalIgnoreCase);

    private string? ResolveSetting(
        ResourceIdentityProviderDefinition provider,
        string key,
        string configurationKey) =>
        ResolveSetting(provider, key) ?? configuration[configurationKey];

    private static string? ResolveSetting(
        ResourceIdentityProviderDefinition provider,
        string key) =>
        provider.ProviderSettings.TryGetValue(key, out var setting) &&
        !string.IsNullOrWhiteSpace(setting)
            ? setting.Trim()
            : null;

    private static string Sanitize(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }

    private sealed class KeycloakTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }
    }

    private sealed class KeycloakClientRepresentation
    {
        public string Id { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;
    }
}
