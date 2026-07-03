using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQManagementAccessOptions
{
    public const string SectionName = "CloudShell:RabbitMQ:ManagementAccess";

    public string UsernameConfigurationKey { get; set; } =
        RabbitMQResourceDefaults.UsernameConfigurationKey;

    public string PasswordConfigurationKey { get; set; } =
        RabbitMQResourceDefaults.PasswordConfigurationKey;

    public string Username { get; set; } =
        RabbitMQResourceDefaults.DefaultUsername;

    public string Password { get; set; } =
        RabbitMQResourceDefaults.DefaultPassword;

    public string VirtualHost { get; set; } =
        RabbitMQResourceDefaults.DefaultVirtualHost;

    public string ManagedUserNamePrefix { get; set; } = "cloudshell";

    public string ManagedUserPasswordSaltConfigurationKey { get; set; } =
        "CloudShell:RabbitMQ:ManagedUserPasswordSalt";
}

public sealed record RabbitMQPrincipalCredentials(
    string UserName,
    string Password);

public interface IRabbitMQPrincipalCredentialProvider
{
    RabbitMQPrincipalCredentials CreateCredentials(
        string targetResourceId,
        ResourcePrincipalReference principal);
}

public sealed class DefaultRabbitMQPrincipalCredentialProvider(
    IConfiguration configuration,
    IOptions<RabbitMQManagementAccessOptions> options) :
    IRabbitMQPrincipalCredentialProvider
{
    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public RabbitMQPrincipalCredentials CreateCredentials(
        string targetResourceId,
        ResourcePrincipalReference principal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);
        ArgumentNullException.ThrowIfNull(principal);

        var userName = CreateUserName(principal, options.ManagedUserNamePrefix);
        return new RabbitMQPrincipalCredentials(
            userName,
            CreatePassword(targetResourceId, principal));
    }

    private string CreatePassword(
        string targetResourceId,
        ResourcePrincipalReference principal)
    {
        var configuredSalt = configuration[options.ManagedUserPasswordSaltConfigurationKey];
        var salt = !string.IsNullOrWhiteSpace(configuredSalt)
            ? configuredSalt
            : ResolveManagementPassword(configuration, options);
        var input = $"{salt}|{targetResourceId.Trim()}|{principal.Id}";
        return Convert
            .ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string CreateUserName(
        ResourcePrincipalReference principal,
        string? prefix)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? "cloudshell"
            : SanitizeUserNamePart(prefix);
        var source = principal.SourceResourceId ?? principal.Id;
        var identityName = principal.SourceIdentityName;
        var baseName = string.IsNullOrWhiteSpace(identityName)
            ? source
            : $"{source}-{identityName}";
        var sanitized = SanitizeUserNamePart(baseName);
        var hash = Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(principal.Id)), 0, 6)
            .ToLowerInvariant();
        var maximumBaseLength = Math.Max(1, 64 - normalizedPrefix.Length - hash.Length - 2);
        if (sanitized.Length > maximumBaseLength)
        {
            sanitized = sanitized[..maximumBaseLength].Trim('-', '.', '_');
        }

        return $"{normalizedPrefix}-{sanitized}-{hash}";
    }

    private static string SanitizeUserNamePart(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.'
                    ? character
                    : '-');
        }

        var sanitized = builder
            .ToString()
            .Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "identity" : sanitized;
    }

    internal static string ResolveManagementUserName(
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options) =>
        !string.IsNullOrWhiteSpace(options.UsernameConfigurationKey) &&
        !string.IsNullOrWhiteSpace(configuration[options.UsernameConfigurationKey])
            ? configuration[options.UsernameConfigurationKey]!
            : options.Username;

    internal static string ResolveManagementPassword(
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options) =>
        !string.IsNullOrWhiteSpace(options.PasswordConfigurationKey) &&
        !string.IsNullOrWhiteSpace(configuration[options.PasswordConfigurationKey])
            ? configuration[options.PasswordConfigurationKey]!
            : options.Password;
}

public sealed class RabbitMQManagementApiAccessReconciler(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<RabbitMQManagementAccessOptions> options,
    IRabbitMQPrincipalCredentialProvider credentialProvider,
    IRabbitMQBootstrapCredentialProvider bootstrapCredentials) :
    IRabbitMQAccessReconciler
{
    public const string HttpClientName = "CloudShell.RabbitMQ.Management";

    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
        Resource resource,
        IReadOnlyList<ResourcePermissionGrant> grants,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(grants);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        if (!TryGetManagementUri(resource, out var managementUri))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.managementEndpointRequired",
                "RabbitMQ access reconciliation requires a resolved management endpoint.",
                resource.EffectiveResourceId));
            return diagnostics;
        }

        var virtualHost = RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options);
        var plans = CreateUserPlans(resource, grants, virtualHost, diagnostics);
        if (plans.Count == 0)
        {
            diagnostics.Add(new ResourceDefinitionDiagnostic(
                ResourceDefinitionDiagnosticSeverity.Information,
                "application.rabbitmq.noBrokerAccessGrants",
                "No RabbitMQ publish, consume, or configure grants were declared for resource identities.",
                resource.EffectiveResourceId));
            return diagnostics;
        }

        var client = httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = managementUri;
        client.DefaultRequestHeaders.Authorization = CreateAuthorizationHeader(resource);

        try
        {
            foreach (var plan in plans)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await EnsureUserAsync(client, plan, cancellationToken);
                await EnsurePermissionsAsync(client, plan, cancellationToken);
            }

            diagnostics.Add(new ResourceDefinitionDiagnostic(
                ResourceDefinitionDiagnosticSeverity.Information,
                "application.rabbitmq.accessReconciled",
                $"Reconciled RabbitMQ broker access for {plans.Count} resource identity user(s).",
                resource.EffectiveResourceId));
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.accessReconciliationFailed",
                exception.Message,
                resource.EffectiveResourceId));
        }

        return diagnostics;
    }

    private List<RabbitMQUserPermissionPlan> CreateUserPlans(
        Resource resource,
        IReadOnlyList<ResourcePermissionGrant> grants,
        string virtualHost,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var plans = new Dictionary<ResourcePrincipalReference, RabbitMQUserPermissionPlan>();
        foreach (var grant in grants)
        {
            if (!IsBrokerPermission(grant.Permission))
            {
                continue;
            }

            if (grant.ResourceIdentity is null)
            {
                diagnostics.Add(new ResourceDefinitionDiagnostic(
                    ResourceDefinitionDiagnosticSeverity.Warning,
                    "application.rabbitmq.resourceIdentityGrantRequired",
                    "RabbitMQ broker access reconciliation can only materialize grants assigned to resource identities.",
                    resource.EffectiveResourceId));
                continue;
            }

            if (!plans.TryGetValue(grant.Principal, out var plan))
            {
                var credentials = credentialProvider.CreateCredentials(
                    resource.EffectiveResourceId,
                    grant.Principal);
                plan = new RabbitMQUserPermissionPlan(
                    grant.Principal,
                    credentials,
                    virtualHost);
                plans.Add(grant.Principal, plan);
            }

            plan.Apply(grant.Permission);
        }

        return plans.Values.ToList();
    }

    private AuthenticationHeaderValue CreateAuthorizationHeader(Resource resource)
    {
        return RabbitMQManagementApiHttp.CreateAuthorizationHeader(
            bootstrapCredentials.ResolveManagementCredentials(
                resource,
                configuration,
                options));
    }

    private async Task EnsureUserAsync(
        HttpClient client,
        RabbitMQUserPermissionPlan plan,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(
            $"api/users/{Uri.EscapeDataString(plan.Credentials.UserName)}",
            new RabbitMQUserRequest(plan.Credentials.Password, Tags: string.Empty),
            cancellationToken);
        await RabbitMQManagementApiHttp.EnsureSuccessAsync(
            response,
            "create or update RabbitMQ user",
            cancellationToken);
    }

    private async Task EnsurePermissionsAsync(
        HttpClient client,
        RabbitMQUserPermissionPlan plan,
        CancellationToken cancellationToken)
    {
        using var response = await client.PutAsJsonAsync(
            $"api/permissions/{Uri.EscapeDataString(plan.VirtualHost)}/{Uri.EscapeDataString(plan.Credentials.UserName)}",
            new RabbitMQPermissionsRequest(
                plan.Configure ? ".*" : string.Empty,
                plan.Write ? ".*" : string.Empty,
                plan.Read ? ".*" : string.Empty),
            cancellationToken);
        await RabbitMQManagementApiHttp.EnsureSuccessAsync(
            response,
            "apply RabbitMQ permissions",
            cancellationToken);
    }

    private static bool TryGetManagementUri(
        Resource resource,
        out Uri managementUri) =>
        RabbitMQManagementApiHttp.TryGetResourceModelManagementUri(resource, out managementUri);

    private static bool IsBrokerPermission(string permission) =>
        RabbitMQManagementAccessRules.IsBrokerPermission(permission);

    private sealed record RabbitMQUserRequest(
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("tags")] string Tags);

    private sealed record RabbitMQPermissionsRequest(
        [property: JsonPropertyName("configure")] string Configure,
        [property: JsonPropertyName("write")] string Write,
        [property: JsonPropertyName("read")] string Read);

    private sealed class RabbitMQUserPermissionPlan(
        ResourcePrincipalReference principal,
        RabbitMQPrincipalCredentials credentials,
        string virtualHost)
    {
        public ResourcePrincipalReference Principal { get; } = principal;

        public RabbitMQPrincipalCredentials Credentials { get; } = credentials;

        public string VirtualHost { get; } =
            string.IsNullOrWhiteSpace(virtualHost) ? "/" : virtualHost.Trim();

        public bool Configure { get; private set; }

        public bool Read { get; private set; }

        public bool Write { get; private set; }

        public void Apply(string permission)
        {
            if (string.Equals(permission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase))
            {
                Configure = true;
            }
            else if (string.Equals(permission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase))
            {
                Read = true;
            }
            else if (string.Equals(permission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase))
            {
                Write = true;
            }
        }
    }
}

public interface IRabbitMQPermissionGrantEffectivenessProvider
{
    string ProviderId { get; }

    bool CanGetStatus(ResourcePermissionGrantStatusRequest request);

    Task<ResourcePermissionGrantStatus> GetStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class RabbitMQManagementApiPermissionGrantEffectivenessProvider(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IOptions<RabbitMQManagementAccessOptions> options,
    IRabbitMQPrincipalCredentialProvider credentialProvider,
    IRabbitMQBootstrapCredentialProvider bootstrapCredentials) :
    IRabbitMQPermissionGrantEffectivenessProvider
{
    private readonly RabbitMQManagementAccessOptions options = options.Value;

    public string ProviderId => RabbitMQResourceTypeProvider.ProviderId;

    public bool CanGetStatus(ResourcePermissionGrantStatusRequest request) =>
        string.Equals(
            request.TargetResource.EffectiveTypeId,
            RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
            StringComparison.OrdinalIgnoreCase) &&
        RabbitMQManagementAccessRules.IsBrokerPermission(request.Grant.Permission);

    public async Task<ResourcePermissionGrantStatus> GetStatusAsync(
        ResourcePermissionGrantStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Grant.ResourceIdentity is null)
        {
            return CreateStatus(
                request,
                ResourcePermissionGrantEffectivenessState.NotApplied,
                "RabbitMQ broker permissions can only be materialized for resource identity grants.");
        }

        if (!RabbitMQManagementApiHttp.TryGetResourceManagerManagementUri(
                request.TargetResource,
                out var managementUri))
        {
            return CreateStatus(
                request,
                ResourcePermissionGrantEffectivenessState.Unknown,
                "RabbitMQ broker permission status requires a resolved management endpoint.");
        }

        var credentials = credentialProvider.CreateCredentials(
            request.TargetResource.Id,
            request.Grant.Principal);
        var client = httpClientFactory.CreateClient(RabbitMQManagementApiAccessReconciler.HttpClientName);
        client.BaseAddress = managementUri;
        client.DefaultRequestHeaders.Authorization =
            RabbitMQManagementApiHttp.CreateAuthorizationHeader(
                bootstrapCredentials.ResolveManagementCredentials(
                    request.TargetResource,
                    configuration,
                    options));

        try
        {
            using var response = await client.GetAsync(
                $"api/permissions/{Uri.EscapeDataString(ResolveVirtualHost(request.TargetResource))}/{Uri.EscapeDataString(credentials.UserName)}",
                cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return CreateStatus(
                    request,
                    ResourcePermissionGrantEffectivenessState.NotApplied,
                    $"RabbitMQ user '{credentials.UserName}' does not have broker-native permissions for this virtual host.");
            }

            await RabbitMQManagementApiHttp.EnsureSuccessAsync(
                response,
                "read RabbitMQ permissions",
                cancellationToken);
            var permissions = await response.Content.ReadFromJsonAsync<RabbitMQObservedPermissions>(
                cancellationToken);
            if (permissions is null)
            {
                return CreateStatus(
                    request,
                    ResourcePermissionGrantEffectivenessState.Unknown,
                    "RabbitMQ Management API returned an empty permission response.");
            }

            return RabbitMQManagementAccessRules.HasPermission(permissions, request.Grant.Permission)
                ? CreateStatus(
                    request,
                    ResourcePermissionGrantEffectivenessState.Applied,
                    "RabbitMQ broker-native permissions match the requested CloudShell grant.")
                : CreateStatus(
                    request,
                    ResourcePermissionGrantEffectivenessState.Drifted,
                    "RabbitMQ broker-native permissions do not include the requested CloudShell grant.");
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return CreateStatus(
                request,
                ResourcePermissionGrantEffectivenessState.Failed,
                exception.Message);
        }
    }

    private string ResolveVirtualHost(CloudShell.Abstractions.ResourceManager.Resource resource) =>
        RabbitMQResourceConfiguration.ResolveVirtualHost(resource, options);

    private ResourcePermissionGrantStatus CreateStatus(
        ResourcePermissionGrantStatusRequest request,
        ResourcePermissionGrantEffectivenessState state,
        string detail) =>
        new(
            request.Grant,
            state,
            detail,
            ProviderId,
            DateTimeOffset.UtcNow);
}

public sealed record RabbitMQObservedPermissions(
    [property: JsonPropertyName("configure")] string? Configure = null,
    [property: JsonPropertyName("write")] string? Write = null,
    [property: JsonPropertyName("read")] string? Read = null);

internal static class RabbitMQManagementAccessRules
{
    public static bool IsBrokerPermission(string permission) =>
        string.Equals(permission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(permission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase);

    public static bool HasPermission(
        RabbitMQObservedPermissions permissions,
        string grantPermission) =>
        string.Equals(grantPermission, RabbitMQResourceOperationPermissions.Configure, StringComparison.OrdinalIgnoreCase)
            ? HasPermissionExpression(permissions.Configure)
            : string.Equals(grantPermission, RabbitMQResourceOperationPermissions.Publish, StringComparison.OrdinalIgnoreCase)
                ? HasPermissionExpression(permissions.Write)
                : string.Equals(grantPermission, RabbitMQResourceOperationPermissions.Consume, StringComparison.OrdinalIgnoreCase) &&
                    HasPermissionExpression(permissions.Read);

    private static bool HasPermissionExpression(string? expression) =>
        !string.IsNullOrWhiteSpace(expression);
}

internal static class RabbitMQManagementApiHttp
{
    public static AuthenticationHeaderValue CreateAuthorizationHeader(
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options) =>
        CreateAuthorizationHeader(new RabbitMQBootstrapCredentials(
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementUserName(
                configuration,
                options),
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementPassword(
                configuration,
                options),
            IsCloudShellManaged: false));

    public static AuthenticationHeaderValue CreateAuthorizationHeader(
        RabbitMQBootstrapCredentials credentials)
    {
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{credentials.UserName}:{credentials.Password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(detail)
            ? $"RabbitMQ Management API failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}."
            : $"RabbitMQ Management API failed to {operation}: {(int)response.StatusCode} {response.ReasonPhrase}. {detail}";
        throw new HttpRequestException(message);
    }

    public static bool TryGetResourceModelManagementUri(
        Resource resource,
        out Uri managementUri)
    {
        var endpoint = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                RabbitMQResourceTypeProvider.Attributes.EndpointRequests)?
            .FirstOrDefault(endpoint =>
                string.Equals(endpoint.Name, "management", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(endpoint.Protocol, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(endpoint.Host) &&
                endpoint.Port is > 0);
        if (endpoint is not null &&
            Uri.TryCreate(
                $"http://{endpoint.Host!.Trim()}:{endpoint.Port!.Value}",
                UriKind.Absolute,
                out var endpointUri))
        {
            managementUri = EnsureTrailingSlash(endpointUri);
            return true;
        }

        managementUri = null!;
        return false;
    }

    public static bool TryGetResourceManagerManagementUri(
        CloudShell.Abstractions.ResourceManager.Resource resource,
        out Uri managementUri)
    {
        if (resource.TryGetResolvedEndpointUri("management", out var endpointUri))
        {
            managementUri = EnsureTrailingSlash(endpointUri);
            return true;
        }

        managementUri = null!;
        return false;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + "/");
}
