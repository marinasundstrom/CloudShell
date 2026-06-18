using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;

namespace CloudShell.ThirdPartyIdentity;

public sealed class KeycloakResourceIdentityProvisioner(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) :
    IResourceIdentityProvisioner,
    IResourceIdentityProvisioningStatusProvider,
    IResourceIdentityProviderSetupHandler,
    IResourceIdentityCredentialEnvironmentProvider
{
    public string ProviderId => "keycloak";

    public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public bool CanGetProvisioningStatus(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public bool CanSetup(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider) =>
        IsKeycloakProvider(provider);

    public IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
        ResourceIdentityCredentialEnvironmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var clientId = ResolveKeycloakClientId(request.Identity, request.Binding);
        var tokenEndpoint = ResolveTokenEndpoint(request.Provider);
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return [];
        }

        return
        [
            new(
                EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable,
                tokenEndpoint),
            new(
                EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable,
                clientId),
            new(
                EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable,
                ResolveKeycloakClientSecret(request.Provider, request.Identity, clientId)),
            new(
                EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable,
                request.DefaultScope)
        ];
    }

    public async Task<ResourceIdentityProviderSetupResult> SetupAsync(
        ResourceIdentityProviderSetupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var diagnostics = new List<ResourceIdentityProvisioningDiagnostic>();
        var client = httpClientFactory.CreateClient();

        try
        {
            var token = await GetAdminAccessTokenAsync(client, request.Provider, cancellationToken);
            var clientId = ResolveSetting(request.Provider, "ClientId") ??
                configuration["Authentication:OpenIdConnect:ClientId"] ??
                "cloudshell-ui";
            var keycloakClient = await FindClientAsync(
                client,
                request.Provider,
                token,
                clientId,
                cancellationToken);
            if (keycloakClient is null)
            {
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Warning,
                    $"Keycloak client '{clientId}' was not found. Import the sample realm or create the UI client before setup.",
                    ProviderId: request.Provider.Id));
            }
            else
            {
                await EnsureRealmRoleMapperAsync(
                    client,
                    request.Provider,
                    token,
                    keycloakClient.Id,
                    cancellationToken);
                diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                    ResourceIdentityProvisioningDiagnosticSeverity.Information,
                    $"Keycloak client '{clientId}' is configured to emit realm roles for CloudShell authorization.",
                    ProviderId: request.Provider.Id));
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            diagnostics.Add(new ResourceIdentityProvisioningDiagnostic(
                ResourceIdentityProvisioningDiagnosticSeverity.Error,
                exception.Message,
                ProviderId: request.Provider.Id));
        }

        return new ResourceIdentityProviderSetupResult(request.Provider.Id, diagnostics);
    }

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
            var keycloakClientSecret = ResolveKeycloakClientSecret(
                request.Provider,
                entry.Identity,
                keycloakClientId);
            var keycloakClient = await EnsureClientAsync(
                client,
                request.Provider,
                token,
                keycloakClientId,
                keycloakClientSecret,
                entry,
                cancellationToken);
            var grants = request.PermissionGrants
                .Where(grant =>
                    grant.ResourceIdentity is { } grantIdentity &&
                    Matches(entry.Identity, grantIdentity))
                .ToArray();
            var roles = new List<KeycloakRoleRepresentation>();
            foreach (var grant in grants)
            {
                roles.Add(await EnsureClientRoleAsync(
                    client,
                    request.Provider,
                    token,
                    keycloakClient.Id,
                    CreateGrantRoleName(grant),
                    grant,
                    cancellationToken));
            }

            if (roles.Count > 0)
            {
                await AssignServiceAccountRolesAsync(
                    client,
                    request.Provider,
                    token,
                    keycloakClient.Id,
                    roles,
                    cancellationToken);
            }

            await EnsureResourcePermissionMappersAsync(
                client,
                request.Provider,
                token,
                keycloakClient.Id,
                keycloakClient.ClientId,
                grants,
                cancellationToken);

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
        string clientSecret,
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
                secret = clientSecret,
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

    private static async Task<KeycloakRoleRepresentation> EnsureClientRoleAsync(
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
            return await readResponse.Content.ReadFromJsonAsync<KeycloakRoleRepresentation>(
                    cancellationToken) ??
                throw new InvalidOperationException($"Keycloak role '{roleName}' was read but could not be deserialized.");
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

        using var createdReadRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/roles/{Uri.EscapeDataString(roleName)}"),
            token);
        using var createdReadResponse = await client.SendAsync(createdReadRequest, cancellationToken);
        await EnsureSuccessAsync(createdReadResponse, cancellationToken);
        return await createdReadResponse.Content.ReadFromJsonAsync<KeycloakRoleRepresentation>(
                cancellationToken) ??
            throw new InvalidOperationException($"Keycloak role '{roleName}' was created but could not be read.");
    }

    private static async Task AssignServiceAccountRolesAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        IReadOnlyList<KeycloakRoleRepresentation> roles,
        CancellationToken cancellationToken)
    {
        var serviceAccount = await GetServiceAccountUserAsync(
            client,
            provider,
            token,
            internalClientId,
            cancellationToken);
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            CreateAdminUri(
                provider,
                $"users/{Uri.EscapeDataString(serviceAccount.Id)}/role-mappings/clients/{Uri.EscapeDataString(internalClientId)}"),
            token,
            roles.Select(role => new
            {
                id = role.Id,
                name = role.Name
            }).ToArray());
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private static async Task<KeycloakUserRepresentation> GetServiceAccountUserAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/service-account-user"),
            token);
        using var response = await client.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<KeycloakUserRepresentation>(
                cancellationToken) ??
            throw new InvalidOperationException("Keycloak service-account user response could not be read.");
    }

    private static async Task EnsureResourcePermissionMappersAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        string clientId,
        IReadOnlyList<ResourcePermissionGrant> grants,
        CancellationToken cancellationToken)
    {
        const string mapperName = "CloudShell resource permissions";
        using var readRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/protocol-mappers/models"),
            token);
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        await EnsureSuccessAsync(readResponse, cancellationToken);
        var mappers = await readResponse.Content.ReadFromJsonAsync<KeycloakProtocolMapperRepresentation[]>(
            cancellationToken) ?? [];
        if (mappers.Any(mapper => string.Equals(mapper.Name, mapperName, StringComparison.Ordinal)))
        {
            await EnsureHardcodedResourcePermissionMappersAsync(
                client,
                provider,
                token,
                internalClientId,
                grants,
                mappers,
                cancellationToken);
            return;
        }

        using var createRequest = CreateJsonRequest(
            HttpMethod.Post,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/protocol-mappers/models"),
            token,
            new
            {
                name = mapperName,
                protocol = "openid-connect",
                protocolMapper = "oidc-usermodel-client-role-mapper",
                consentRequired = false,
                config = new Dictionary<string, string>
                {
                    ["access.token.claim"] = "true",
                    ["claim.name"] = CloudShellAuthorizationClaimTypes.ResourcePermission,
                    ["jsonType.label"] = "String",
                    ["multivalued"] = "true",
                    ["usermodel.clientRoleMapping.clientId"] = clientId
                }
            });
        using var createResponse = await client.SendAsync(createRequest, cancellationToken);
        await EnsureSuccessAsync(createResponse, cancellationToken);

        await EnsureHardcodedResourcePermissionMappersAsync(
            client,
            provider,
            token,
            internalClientId,
            grants,
            mappers,
            cancellationToken);
    }

    private static async Task EnsureHardcodedResourcePermissionMappersAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        IReadOnlyList<ResourcePermissionGrant> grants,
        IReadOnlyList<KeycloakProtocolMapperRepresentation> existingMappers,
        CancellationToken cancellationToken)
    {
        foreach (var grant in grants)
        {
            var claimValue = CreateGrantRoleName(grant);
            var mapperName = $"CloudShell resource permission {claimValue}";
            if (existingMappers.Any(mapper => string.Equals(mapper.Name, mapperName, StringComparison.Ordinal)))
            {
                continue;
            }

            using var createRequest = CreateJsonRequest(
                HttpMethod.Post,
                CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/protocol-mappers/models"),
                token,
                new
                {
                    name = mapperName,
                    protocol = "openid-connect",
                    protocolMapper = "oidc-hardcoded-claim-mapper",
                    consentRequired = false,
                    config = new Dictionary<string, string>
                    {
                        ["access.token.claim"] = "true",
                        ["claim.name"] = CloudShellAuthorizationClaimTypes.ResourcePermission,
                        ["claim.value"] = claimValue,
                        ["jsonType.label"] = "String"
                    }
                });
            using var createResponse = await client.SendAsync(createRequest, cancellationToken);
            await EnsureSuccessAsync(createResponse, cancellationToken);
        }
    }

    private static async Task EnsureRealmRoleMapperAsync(
        HttpClient client,
        ResourceIdentityProviderDefinition provider,
        string token,
        string internalClientId,
        CancellationToken cancellationToken)
    {
        const string mapperName = "realm roles";
        using var readRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/protocol-mappers/models"),
            token);
        using var readResponse = await client.SendAsync(readRequest, cancellationToken);
        await EnsureSuccessAsync(readResponse, cancellationToken);
        var mappers = await readResponse.Content.ReadFromJsonAsync<KeycloakProtocolMapperRepresentation[]>(
            cancellationToken) ?? [];
        if (mappers.Any(mapper => string.Equals(mapper.Name, mapperName, StringComparison.Ordinal)))
        {
            return;
        }

        using var createRequest = CreateJsonRequest(
            HttpMethod.Post,
            CreateAdminUri(provider, $"clients/{Uri.EscapeDataString(internalClientId)}/protocol-mappers/models"),
            token,
            new
            {
                name = mapperName,
                protocol = "openid-connect",
                protocolMapper = "oidc-usermodel-realm-role-mapper",
                consentRequired = false,
                config = new Dictionary<string, string>
                {
                    ["access.token.claim"] = "true",
                    ["claim.name"] = ResolveSetting(provider, "RoleClaimType") ?? "roles",
                    ["id.token.claim"] = "true",
                    ["jsonType.label"] = "String",
                    ["multivalued"] = "true",
                    ["userinfo.token.claim"] = "true"
                }
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
        => ResolveKeycloakClientId(entry.Identity, entry.Binding);

    private static string ResolveKeycloakClientId(
        ResourceIdentityReference identity,
        ResourceIdentityBinding binding)
    {
        const string clientPrefix = "client:";
        if (!string.IsNullOrWhiteSpace(binding.Subject) &&
            binding.Subject.StartsWith(clientPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return binding.Subject[clientPrefix.Length..].Trim();
        }

        return string.IsNullOrWhiteSpace(identity.Name)
            ? Sanitize(identity.ResourceId)
            : Sanitize(identity.Name);
    }

    private static string ResolveKeycloakClientSecret(
        ResourceIdentityProviderDefinition provider,
        ResourceIdentityReference identity,
        string clientId)
    {
        if (provider.ProviderSettings.TryGetValue($"ClientSecret:{clientId}", out var clientSpecificSecret) &&
            !string.IsNullOrWhiteSpace(clientSpecificSecret))
        {
            return clientSpecificSecret.Trim();
        }

        if (provider.ProviderSettings.TryGetValue("ResourceClientSecret", out var resourceClientSecret) &&
            !string.IsNullOrWhiteSpace(resourceClientSecret))
        {
            return resourceClientSecret.Trim();
        }

        var secretSeed = string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}-{identity.Name}";
        return $"local-development-{Sanitize(secretSeed)}-secret";
    }

    private static string? ResolveTokenEndpoint(ResourceIdentityProviderDefinition provider)
    {
        if (ResolveSetting(provider, "TokenEndpoint") is { } configured)
        {
            return configured;
        }

        if (ResolveSetting(provider, "Authority") is { } authority)
        {
            return $"{authority.TrimEnd('/')}/protocol/openid-connect/token";
        }

        if (ResolveSetting(provider, "AdminBaseAddress") is { } adminBaseAddress)
        {
            var realm = ResolveSetting(provider, "Realm") ?? "cloudshell";
            return $"{adminBaseAddress.TrimEnd('/')}/realms/{Uri.EscapeDataString(realm)}/protocol/openid-connect/token";
        }

        return null;
    }

    private static string CreateGrantRoleName(ResourcePermissionGrant grant) =>
        ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
            grant.TargetResourceId,
            grant.Permission);

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

    private sealed class KeycloakRoleRepresentation
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
    }

    private sealed class KeycloakUserRepresentation
    {
        public string Id { get; set; } = string.Empty;
    }

    private sealed class KeycloakProtocolMapperRepresentation
    {
        public string Name { get; set; } = string.Empty;
    }
}
