using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Configuration;

public sealed partial class SecretsVaultProvider(
    SecretsVaultStore store,
    ConfigurationProviderOptions options,
    IHostEnvironment environment,
    LocalProcessRunner processes,
    ResourceDeclarationStore declarations) :
    IResourceProvider,
    ILogProvider,
    IResourceProcedureProvider,
    ISecretReferenceResolver,
    IResourceEnvironmentVariableProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceTemplateProvider
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);

    public const string ProviderId = "secrets-vault";

    public const string ResourceType = "secrets.vault";

    public string Id => ProviderId;

    public string DisplayName => "Secrets Vault";

    public SecretsVaultDefinition? GetVault(string id) => store.GetVault(id);

    public IReadOnlyList<SecretsVaultDefinition> GetVaults() => store.GetVaults();

    public IReadOnlyList<Resource> GetResources() =>
        store.GetVaults()
            .Select(CreateResource)
            .ToArray();

    public IReadOnlyList<EnvironmentVariableAssignment> GetEnvironmentVariables(string resourceId)
    {
        if (store.GetVault(resourceId) is not { } vault)
        {
            return [];
        }

        return
        [
            new($"CLOUDSHELL_SECRETS_{CreateEnvironmentName(vault.Name)}_VAULT_ID", vault.Id),
            new($"CLOUDSHELL_SECRETS_{CreateEnvironmentName(vault.Name)}_ENDPOINT", GetSecretsEndpoint(vault.Id)),
            new($"CLOUDSHELL_SECRETS_{CreateEnvironmentName(vault.Id)}_VAULT_ID", vault.Id),
            new($"CLOUDSHELL_SECRETS_{CreateEnvironmentName(vault.Id)}_ENDPOINT", GetSecretsEndpoint(vault.Id))
        ];
    }

    public IReadOnlyList<LogDescriptor> GetLogs() => store
        .GetVaults()
        .Select(vault => new LogDescriptor(
            GetLogId(vault.Id),
            "Secrets Vault service logs",
            DisplayName,
            vault.Name,
            LogSourceKind.Resource,
            ResourceId: vault.Id,
            SupportsStreaming: true,
            Description: "Secrets Vault service stdout, stderr, and lifecycle events."))
        .ToArray();

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var resourceId = GetResourceIdFromLogId(logId);
        return resourceId is null || store.GetVault(resourceId) is null
            ? Task.FromResult<IReadOnlyList<LogEntry>>([])
            : processes.ReadLogAsync(
                GetServiceResourceId(resourceId),
                maxEntries,
                before,
                cancellationToken);
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resourceId = GetResourceIdFromLogId(logId);
        if (resourceId is null || store.GetVault(resourceId) is null)
        {
            yield break;
        }

        await foreach (var entry in processes.StreamLogAsync(
                           GetServiceResourceId(resourceId),
                           initialEntries,
                           cancellationToken))
        {
            yield return entry;
        }
    }

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: false,
            StartAsDependency: true,
            StartAfterCreate: false);

    public async Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var vault = options.DeclaredSecretsVaults.FirstOrDefault(vault =>
            string.Equals(vault.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Secrets Vault declaration '{declaration.ResourceId}' was not found.");

        await SetupVaultAsync(
            vault.Definition,
            declaration.ResourceGroupId,
            registrations,
            cancellationToken);
    }

    public async Task SetupVaultAsync(
        SecretsVaultDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = EnsureServiceEndpoint(Normalize(
            string.IsNullOrWhiteSpace(definition.Id)
                ? definition with { Id = CreateUniqueId(definition.Name) }
                : definition));

        store.Save(normalized);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task UpdateVaultAsync(
        SecretsVaultDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(definition);
        var existing = store.GetVault(normalized.Id);
        if (existing is null)
        {
            throw new InvalidOperationException($"Secrets Vault '{normalized.Id}' is not configured.");
        }

        store.Save(EnsureServiceEndpoint(normalized with
        {
            Endpoint = normalized.Endpoint ?? existing.Endpoint,
            HealthChecks = normalized.HealthChecks.Count == 0 ? existing.HealthChecks : normalized.HealthChecks
        }));

        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (store.GetVault(context.Resource.Id) is not null)
        {
            var stopAction = context.Resource.ResourceActions.FirstOrDefault(action =>
                action.Kind == ResourceActionKind.Stop);
            if (stopAction is not null)
            {
                await ExecuteActionAsync(context, stopAction, cancellationToken);
            }
        }

        processes.Remove(GetServiceResourceId(context.Resource.Id));
        store.Remove(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Secrets Vault removed.");
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var vault = store.GetVault(context.Resource.Id)
            ?? throw new InvalidOperationException($"Secrets Vault '{context.Resource.Id}' is not configured.");
        var process = CreateServiceProcessDefinition(vault);
        switch (action.Kind)
        {
            case ResourceActionKind.Start:
                store.Save(vault);
                await processes.StartAsync(process, cancellationToken);
                await WaitForServiceReadyAsync(GetServiceBaseUrl(vault), cancellationToken);
                break;
            case ResourceActionKind.Stop:
                await processes.StopAsync(process, cancellationToken: cancellationToken);
                break;
            case ResourceActionKind.Restart:
                store.Save(vault);
                await processes.StopAsync(process, cancellationToken: cancellationToken);
                await processes.StartAsync(process, cancellationToken);
                await WaitForServiceReadyAsync(GetServiceBaseUrl(vault), cancellationToken);
                break;
            default:
                throw new NotSupportedException(
                    $"Secrets Vault resources do not support action '{action.DisplayName}'.");
        }

        return ResourceProcedureResult.Completed(CreateActionMessage(action, context.Resource.Name));
    }

    public ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        var vault = store.GetVault(reference.VaultResourceId);
        if (vault is null)
        {
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Secrets Vault '{reference.VaultResourceId}' was not found."));
        }

        if (context.Identity is { } identity &&
            !declarations
                .CreatePermissionGrantEvaluator()
                .Evaluate(
                    identity,
                    vault.Id,
                    SecretsVaultResourceOperationPermissions.ReadSecrets)
                .IsAllowed)
        {
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Identity '{FormatIdentity(identity)}' is not allowed to read secrets from Secrets Vault '{reference.VaultResourceId}'."));
        }

        var candidates = vault.Secrets
            .Where(secret => string.Equals(secret.Name, reference.SecretName, StringComparison.OrdinalIgnoreCase))
            .Where(secret => string.IsNullOrWhiteSpace(reference.Version) ||
                string.Equals(secret.Version, reference.Version, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var resolved = candidates.LastOrDefault();
        if (resolved is null)
        {
            var version = string.IsNullOrWhiteSpace(reference.Version)
                ? string.Empty
                : $" version '{reference.Version}'";
            return ValueTask.FromResult(ResourceSettingResolutionResult.Failed(
                $"Secret '{reference.SecretName}'{version} was not found in Secrets Vault '{reference.VaultResourceId}'."));
        }

        return ValueTask.FromResult(ResourceSettingResolutionResult.Resolved(resolved.Value));
    }

    private static string FormatIdentity(ResourceIdentityReference identity) =>
        string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";

    public bool CanExport(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, ResourceType, StringComparison.OrdinalIgnoreCase) &&
        store.GetVault(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var vault = store.GetVault(resource.Id)
            ?? throw new InvalidOperationException($"Secrets Vault '{resource.Id}' is not configured.");

        var configuration = new SecretsVaultTemplateConfiguration(
            vault.Secrets
                .Select(secret => secret with { Value = string.Empty })
                .ToArray());

        return Task.FromResult(new ResourceTemplateDefinition(
            vault.Name,
            Id,
            ResourceType,
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions),
            vault.Id));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ResourceType, ResourceType, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The Secrets Vault template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<SecretsVaultTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The Secrets Vault template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? CreateId(template.Name)
            : template.ResourceId.Trim();
        await SetupVaultAsync(
            new SecretsVaultDefinition(
                resourceId,
                template.Name,
                configuration.Secrets),
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported Secrets Vault '{template.Name}'. Secret values must be supplied after import.");
    }

    private Resource CreateResource(SecretsVaultDefinition vault) =>
        new(
            vault.Id,
            vault.Name,
            "Secrets Vault",
            DisplayName,
            "provider-owned",
            GetState(vault),
            [ResourceEndpoint.FromAddress("secrets", GetSecretsEndpoint(vault.Id), "http")],
            $"{vault.Secrets.Count} secrets",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ResourceType,
            Actions: CreateActions(vault),
            HealthChecks: vault.HealthChecks,
            ResourceClass: ResourceClass.SecretsVault,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["secretsVault.secrets"] = vault.Secrets.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.EndpointCount] = "1"
            });

    private static SecretsVaultDefinition Normalize(SecretsVaultDefinition vault)
    {
        var id = string.IsNullOrWhiteSpace(vault.Id)
            ? CreateId(vault.Name)
            : vault.Id.Trim();

        return vault with
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(vault.Name)
                ? id
                : vault.Name.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(vault.Endpoint) ? null : vault.Endpoint.TrimEnd('/'),
            HealthChecks = NormalizeHealthChecks(vault.HealthChecks),
            Secrets = vault.Secrets
                .Where(secret => !string.IsNullOrWhiteSpace(secret.Name))
                .Select(secret => secret with
                {
                    Name = secret.Name.Trim(),
                    Value = secret.Value ?? string.Empty,
                    Version = string.IsNullOrWhiteSpace(secret.Version) ? null : secret.Version.Trim()
                })
                .ToArray()
        };
    }

    public static string CreateId(string name)
    {
        var slug = SlugPattern()
            .Replace(name.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"secrets-vault:{Guid.NewGuid():N}"
            : $"secrets-vault:{slug}";
    }

    private string CreateUniqueId(string name)
    {
        var candidate = CreateId(name);
        if (GetVault(candidate) is null)
        {
            return candidate;
        }

        var suffix = 2;
        while (GetVault($"{candidate}-{suffix}") is not null)
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private SecretsVaultDefinition EnsureServiceEndpoint(SecretsVaultDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Endpoint)
            ? definition with { Endpoint = CreateUniqueServiceEndpoint(definition.Id) }
            : definition;

    private ResourceState GetState(SecretsVaultDefinition vault) =>
        processes.IsRunning(CreateServiceProcessDefinition(vault))
            ? ResourceState.Running
            : ResourceState.Stopped;

    private IReadOnlyList<ResourceAction> CreateActions(SecretsVaultDefinition vault) =>
        GetState(vault) == ResourceState.Running
            ? [ResourceAction.Stop, ResourceAction.Restart]
            : [ResourceAction.Start];

    private static string CreateActionMessage(ResourceAction action, string resourceName) =>
        action.Kind switch
        {
            ResourceActionKind.Start => $"Started {resourceName}.",
            ResourceActionKind.Stop => $"Stopped {resourceName}.",
            ResourceActionKind.Restart => $"Restarted {resourceName}.",
            _ => $"{action.DisplayName} requested for {resourceName}."
        };

    private LocalProcessDefinition CreateServiceProcessDefinition(SecretsVaultDefinition definition)
    {
        var endpoint = GetServiceBaseUrl(definition);
        return new LocalProcessDefinition(
            GetServiceResourceId(definition.Id),
            options.ServiceExecutablePath,
            CreateServiceArguments(endpoint),
            options.SecretsServiceWorkingDirectory ?? options.ServiceWorkingDirectory,
            CreateServiceEnvironment(definition),
            LocalProcessLifetime.Detached);
    }

    private IReadOnlyList<EnvironmentVariableAssignment> CreateServiceEnvironment(
        SecretsVaultDefinition definition)
    {
        var environmentVariables = new List<EnvironmentVariableAssignment>
        {
            new("ASPNETCORE_ENVIRONMENT", environment.EnvironmentName),
            new("CloudShell__SecretsVaultService__DefinitionsPath", ResolveDefinitionsPath()),
            new("CloudShell__SecretsVaultService__ResourceId", definition.Id)
        };

        AddAuthenticationEnvironment(environmentVariables);
        return environmentVariables;
    }

    private void AddAuthenticationEnvironment(List<EnvironmentVariableAssignment> environmentVariables)
    {
        environmentVariables.Add(new(
            "Authentication__BuiltInAuthority__Enabled",
            "true"));

        if (!string.IsNullOrWhiteSpace(options.ServiceAuthenticationIssuer))
        {
            environmentVariables.Add(new(
                "Authentication__BuiltInAuthority__Issuer",
                options.ServiceAuthenticationIssuer));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAuthenticationAudience))
        {
            environmentVariables.Add(new(
                "Authentication__BuiltInAuthority__Audience",
                options.ServiceAuthenticationAudience));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceAuthenticationSigningKeyPem))
        {
            environmentVariables.Add(new(
                "Authentication__BuiltInAuthority__SigningKeyPem",
                options.ServiceAuthenticationSigningKeyPem));
        }

        AddServiceBearerEnvironment(environmentVariables);
    }

    private void AddServiceBearerEnvironment(List<EnvironmentVariableAssignment> environmentVariables)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceBearerAuthority) &&
            string.IsNullOrWhiteSpace(options.ServiceBearerMetadataAddress) &&
            string.IsNullOrWhiteSpace(options.ServiceBearerSigningKeyPem))
        {
            return;
        }

        environmentVariables.Add(new(
            "Authentication__ServiceBearer__Enabled",
            "true"));

        if (!string.IsNullOrWhiteSpace(options.ServiceBearerAuthority))
        {
            environmentVariables.Add(new(
                "Authentication__ServiceBearer__Authority",
                options.ServiceBearerAuthority));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceBearerMetadataAddress))
        {
            environmentVariables.Add(new(
                "Authentication__ServiceBearer__MetadataAddress",
                options.ServiceBearerMetadataAddress));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceBearerIssuer))
        {
            environmentVariables.Add(new(
                "Authentication__ServiceBearer__Issuer",
                options.ServiceBearerIssuer));
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceBearerAudience))
        {
            environmentVariables.Add(new(
                "Authentication__ServiceBearer__Audience",
                options.ServiceBearerAudience));
        }

        environmentVariables.Add(new(
            "Authentication__ServiceBearer__RequireHttpsMetadata",
            options.ServiceBearerRequireHttpsMetadata ? "true" : "false"));

        if (!string.IsNullOrWhiteSpace(options.ServiceBearerSigningKeyPem))
        {
            environmentVariables.Add(new(
                "Authentication__ServiceBearer__SigningKeyPem",
                options.ServiceBearerSigningKeyPem));
        }
    }

    private string CreateServiceArguments(string endpoint)
    {
        var project = string.IsNullOrWhiteSpace(options.SecretsServiceProjectPath)
            ? "CloudShell.SecretsVaultService/CloudShell.SecretsVaultService.csproj"
            : options.SecretsServiceProjectPath;

        return $"run --project {QuoteCommandArgument(project)} --no-launch-profile --urls {QuoteCommandArgument(endpoint)}";
    }

    private string CreateUniqueServiceEndpoint(string resourceId)
    {
        var port = CreateServicePort(resourceId);
        var usedEndpoints = store.GetVaults()
            .Select(GetServiceBaseUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var endpoint = CreateServiceEndpoint(port);
        while (usedEndpoints.Contains(endpoint))
        {
            endpoint = CreateServiceEndpoint(++port);
        }

        return endpoint;
    }

    private string GetServiceBaseUrl(SecretsVaultDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Endpoint)
            ? CreateServiceEndpoint(CreateServicePort(definition.Id))
            : definition.Endpoint.TrimEnd('/');

    private string CreateServiceEndpoint(int port) =>
        $"{options.ServiceUrlScheme.TrimEnd(':')}://{options.ServiceHost.Trim()}:{port}";

    private int CreateServicePort(string resourceId)
    {
        uint hash = 0;
        foreach (var character in resourceId)
        {
            hash = unchecked((hash * 31) + char.ToUpperInvariant(character));
        }

        return options.SecretsServiceBasePort + (int)(hash % 1000);
    }

    private string GetServiceResourceId(string resourceId)
        => ConfigurationResourceProvider.CreateServiceResourceId(resourceId, options.SecretsServiceProcessIdPrefix);

    private string ResolveDefinitionsPath() =>
        Path.IsPathRooted(options.SecretsVaultDefinitionsPath)
            ? options.SecretsVaultDefinitionsPath
            : Path.GetFullPath(options.SecretsVaultDefinitionsPath, environment.ContentRootPath);

    private static async Task WaitForServiceReadyAsync(
        string baseUrl,
        CancellationToken cancellationToken)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var healthUrl = $"{baseUrl.TrimEnd('/')}/healthz";
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(250, cancellationToken);
        }

        throw new TimeoutException(
            $"Secrets Vault endpoint '{healthUrl}' did not become ready within 20 seconds.",
            lastException);
    }

    private string GetSecretsEndpoint(string resourceId)
    {
        var vault = store.GetVault(resourceId);
        var endpoint = vault is null
            ? options.PublicBaseUrl.TrimEnd('/')
            : GetServiceBaseUrl(vault);

        return $"{endpoint}/api/secrets/vaults/{Uri.EscapeDataString(resourceId)}/secrets";
    }

    private static string QuoteCommandArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string CreateEnvironmentName(string name)
    {
        var normalized = EnvironmentNamePattern()
            .Replace(name.Trim().ToUpperInvariant(), "_")
            .Trim('_');

        return string.IsNullOrWhiteSpace(normalized) ? "VAULT" : normalized;
    }

    private static IReadOnlyList<ResourceHealthCheck> NormalizeHealthChecks(
        IReadOnlyList<ResourceHealthCheck> healthChecks) =>
        healthChecks
            .Where(check => !string.IsNullOrWhiteSpace(check.Path))
            .Select(check => check with
            {
                Path = check.Path.Trim(),
                EndpointName = string.IsNullOrWhiteSpace(check.EndpointName) ? null : check.EndpointName.Trim(),
                Name = string.IsNullOrWhiteSpace(check.Name) ? check.Type.ToString().ToLowerInvariant() : check.Name.Trim()
            })
            .ToArray();

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string GetLogId(string resourceId) => $"{resourceId}:secrets-vault-service-logs";

    private static string? GetResourceIdFromLogId(string logId) =>
        logId.EndsWith(":secrets-vault-service-logs", StringComparison.OrdinalIgnoreCase)
            ? logId[..^":secrets-vault-service-logs".Length]
            : null;

    private sealed record SecretsVaultTemplateConfiguration(
        IReadOnlyList<SecretsVaultSecret> Secrets);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^A-Z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentNamePattern();
}
