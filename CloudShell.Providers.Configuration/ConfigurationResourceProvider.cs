using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Configuration;

public sealed partial class ConfigurationResourceProvider :
    IResourceProvider,
    ILogProvider,
    IResourceProcedureProvider,
    IResourceTemplateProvider,
    IResourceEnvironmentVariableProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConfigurationStore store;
    private readonly ConfigurationProviderOptions options;
    private readonly IHostEnvironment environment;
    private readonly LocalProcessRunner processes;

    public ConfigurationResourceProvider(
        ConfigurationStore store,
        ConfigurationProviderOptions options,
        IHostEnvironment environment,
        LocalProcessRunner processes)
    {
        this.store = store;
        this.options = options;
        this.environment = environment;
        this.processes = processes;
    }

    public string Id => "configuration";

    public string DisplayName => "Configuration";

    public IReadOnlyList<Resource> GetResources()
    {
        return store.GetStores()
            .Select(CreateResource)
            .ToArray();
    }

    public IReadOnlyList<LogDescriptor> GetLogs() => store
        .GetStores()
        .Select(configurationStore => new LogDescriptor(
            GetLogId(configurationStore.Id),
            "Configuration service logs",
            DisplayName,
            configurationStore.Name,
            LogSourceKind.Resource,
            ResourceId: configurationStore.Id,
            SupportsStreaming: true,
            Description: "Configuration service stdout, stderr, and lifecycle events."))
        .ToArray();

    public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        var resourceId = GetResourceIdFromLogId(logId);
        return resourceId is null || store.GetStore(resourceId) is null
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
        if (resourceId is null || store.GetStore(resourceId) is null)
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

    public ConfigurationStoreDefinition? GetStore(string id) => store.GetStore(id);

    public IReadOnlyList<ConfigurationStoreDefinition> GetStores() => store.GetStores();

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
        var declaredStore = options.DeclaredStores.FirstOrDefault(store =>
            string.Equals(store.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Configuration service declaration '{declaration.ResourceId}' was not found.");

        if (!declaration.OverwritePersistedState &&
            (registrations.GetRegistration(declaration.ResourceId) is not null ||
             store.GetStore(declaration.ResourceId) is not null))
        {
            await RemoveServiceRegistrationAsync(
                declaration.ResourceId,
                registrations,
                cancellationToken);
            if (registrations.GetRegistration(declaration.ResourceId) is not null)
            {
                await registrations.SetDependenciesAsync(
                    declaration.ResourceId,
                    [],
                    cancellationToken);
            }

            return;
        }

        await SetupStoreAsync(
            declaredStore.Definition,
            declaration.ResourceGroupId,
            registrations,
            cancellationToken);
    }

    public async Task SetupStoreAsync(
        ConfigurationStoreDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDefinition(
            string.IsNullOrWhiteSpace(definition.Id)
                ? definition with { Id = CreateUniqueId(definition.Name) }
                : definition);
        normalized = EnsureServiceEndpoint(normalized);
        store.Save(normalized);
        await RemoveServiceRegistrationAsync(normalized.Id, registrations, cancellationToken);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task UpdateStoreAsync(
        ConfigurationStoreDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = EnsureServiceEndpoint(NormalizeDefinition(definition));
        if (store.GetStore(normalized.Id) is null)
        {
            throw new InvalidOperationException($"Configuration service '{normalized.Id}' is not configured.");
        }

        store.Save(normalized);
        await RemoveServiceRegistrationAsync(normalized.Id, registrations, cancellationToken);

        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            [],
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var configurationStore = store.GetStore(context.Resource.Id)
            ?? throw new InvalidOperationException($"Configuration service '{context.Resource.Id}' is not configured.");
        var process = CreateServiceProcessDefinition(configurationStore);
        switch (action.Kind)
        {
            case ResourceActionKind.Run:
                await processes.StartAsync(process, cancellationToken);
                break;
            case ResourceActionKind.Stop:
                await processes.StopAsync(process, cancellationToken: cancellationToken);
                break;
            case ResourceActionKind.Restart:
                await processes.StopAsync(process, cancellationToken: cancellationToken);
                await processes.StartAsync(process, cancellationToken);
                break;
            default:
                throw new NotSupportedException(
                    $"Configuration services do not support action '{action.DisplayName}'.");
        }

        return ResourceProcedureResult.Completed(CreateActionMessage(action, context.Resource.Name));
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (store.GetStore(context.Resource.Id) is not null)
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
        await RemoveServiceRegistrationAsync(
            context.Resource.Id,
            context.Registrations,
            cancellationToken);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Configuration service removed.");
    }

    public IReadOnlyList<EnvironmentVariableAssignment> GetEnvironmentVariables(string resourceId) =>
        store.GetStore(resourceId) is { } configurationStore
            ?
            [
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Name)}_STORE_ID", configurationStore.Id),
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Name)}_ENDPOINT", GetEntriesEndpoint(configurationStore.Id)),
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Name)}_TOKEN", configurationStore.AccessToken ?? string.Empty),
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Id)}_STORE_ID", configurationStore.Id),
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Id)}_ENDPOINT", GetEntriesEndpoint(configurationStore.Id)),
                new($"CLOUDSHELL_CONFIGURATION_{CreateEnvironmentName(configurationStore.Id)}_TOKEN", configurationStore.AccessToken ?? string.Empty)
            ]
            : [];

    public bool CanExport(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "configuration.store", StringComparison.OrdinalIgnoreCase) &&
        store.GetStore(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var configurationStore = store.GetStore(resource.Id)
            ?? throw new InvalidOperationException($"Configuration service '{resource.Id}' is not configured.");

        var configuration = new ConfigurationStoreTemplateConfiguration(
            configurationStore.Endpoint,
            configurationStore.Entries
                .Select(entry => entry.IsSecret ? entry with { Value = string.Empty } : entry)
                .ToArray());

        return Task.FromResult(new ResourceTemplateDefinition(
            configurationStore.Name,
            Id,
            "configuration.store",
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions),
            configurationStore.Id));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ResourceType, "configuration.store", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The configuration service template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<ConfigurationStoreTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The configuration service template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? CreateUniqueImportId(template.Name)
            : ValidateAvailableImportId(template.ResourceId);
        var definition = new ConfigurationStoreDefinition(
            resourceId,
            template.Name,
            configuration.Entries,
            endpoint: configuration.Endpoint);

        await SetupStoreAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported configuration service '{template.Name}'.");
    }

    public static string CreateId(string name)
    {
        var slug = SlugPattern()
            .Replace(name.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"configuration:{Guid.NewGuid():N}"
            : $"configuration:{slug}";
    }

    public static string CreateServiceResourceId(
        string resourceId,
        string? prefix = null)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? "application:configuration-service"
            : prefix.Trim().TrimEnd('-');
        var slug = SlugPattern()
            .Replace(resourceId.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"{normalizedPrefix}-{Guid.NewGuid():N}"
            : $"{normalizedPrefix}-{slug}";
    }

    private Resource CreateResource(ConfigurationStoreDefinition configurationStore) =>
        new(
            configurationStore.Id,
            configurationStore.Name,
            "Configuration service",
            DisplayName,
            "local",
            GetState(configurationStore),
            [ResourceEndpoint.FromAddress("entries", GetEntriesEndpoint(configurationStore.Id), "http")],
            $"{configurationStore.Entries.Count} entries",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "configuration.store",
            Actions: CreateActions(configurationStore),
            HealthChecks: configurationStore.HealthChecks,
            ResourceClass: ResourceClass.Configuration,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ConfigurationEntryCount] =
                    configurationStore.Entries.Count.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.EndpointCount] = "1"
            });

    private ResourceState GetState(ConfigurationStoreDefinition configurationStore)
    {
        return processes.IsRunning(CreateServiceProcessDefinition(configurationStore))
                ? ResourceState.Running
                : ResourceState.Stopped;
    }

    private IReadOnlyList<ResourceAction> CreateActions(ConfigurationStoreDefinition configurationStore) =>
        GetState(configurationStore) == ResourceState.Running
            ? [ResourceAction.Stop, ResourceAction.Restart]
            : [ResourceAction.Run];

    private static string CreateActionMessage(ResourceAction action, string resourceName) =>
        action.Kind switch
        {
            ResourceActionKind.Run => $"Started {resourceName}.",
            ResourceActionKind.Stop => $"Stopped {resourceName}.",
            ResourceActionKind.Restart => $"Restarted {resourceName}.",
            _ => $"{action.DisplayName} requested for {resourceName}."
        };

    private async Task RemoveServiceRegistrationAsync(
        string resourceId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken)
    {
        await registrations.RemoveAsync(
            GetServiceResourceId(resourceId),
            cancellationToken);
    }

    private LocalProcessDefinition CreateServiceProcessDefinition(ConfigurationStoreDefinition definition)
    {
        var endpoint = GetServiceBaseUrl(definition);
        return new LocalProcessDefinition(
            GetServiceResourceId(definition.Id),
            options.ServiceExecutablePath,
            CreateServiceArguments(endpoint),
            options.ServiceWorkingDirectory,
            [
                new("ASPNETCORE_ENVIRONMENT", environment.EnvironmentName),
                new("CloudShell__ConfigurationService__DefinitionsPath", ResolveDefinitionsPath()),
                new("CloudShell__ConfigurationService__ResourceId", definition.Id)
            ],
            LocalProcessLifetime.Detached);
    }

    private ConfigurationStoreDefinition EnsureServiceEndpoint(ConfigurationStoreDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.Endpoint)
            ? definition with { Endpoint = CreateUniqueServiceEndpoint(definition.Id) }
            : definition;

    private string CreateServiceArguments(string endpoint)
    {
        var project = string.IsNullOrWhiteSpace(options.ServiceProjectPath)
            ? "CloudShell.ConfigurationService/CloudShell.ConfigurationService.csproj"
            : options.ServiceProjectPath;

        return $"run --project {QuoteCommandArgument(project)} --no-launch-profile --urls {QuoteCommandArgument(endpoint)}";
    }

    private string CreateUniqueServiceEndpoint(string resourceId)
    {
        var port = CreateServicePort(resourceId);
        var usedEndpoints = store.GetStores()
            .Select(GetServiceBaseUrl)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var endpoint = CreateServiceEndpoint(port);
        while (usedEndpoints.Contains(endpoint))
        {
            endpoint = CreateServiceEndpoint(++port);
        }

        return endpoint;
    }

    private string GetServiceBaseUrl(ConfigurationStoreDefinition definition) =>
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

        return options.ServiceBasePort + (int)(hash % 1000);
    }

    private string GetServiceResourceId(string resourceId)
        => CreateServiceResourceId(resourceId, options.ServiceProcessIdPrefix);

    private string ResolveDefinitionsPath() =>
        Path.IsPathRooted(options.DefinitionsPath)
            ? options.DefinitionsPath
            : Path.GetFullPath(options.DefinitionsPath, environment.ContentRootPath);

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

    private string CreateUniqueImportId(string name) => CreateUniqueId(name);

    private string ValidateAvailableImportId(string resourceId)
    {
        var normalized = resourceId.Trim();
        if (store.GetStore(normalized) is not null)
        {
            throw new InvalidOperationException($"Resource id '{normalized}' is already in use.");
        }

        return normalized;
    }

    private string CreateUniqueId(string name)
    {
        var candidate = CreateId(name);
        if (store.GetStore(candidate) is null)
        {
            return candidate;
        }

        var suffix = 2;
        while (store.GetStore($"{candidate}-{suffix}") is not null)
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private static ConfigurationStoreDefinition NormalizeDefinition(ConfigurationStoreDefinition definition)
    {
        var id = string.IsNullOrWhiteSpace(definition.Id)
            ? CreateId(definition.Name)
            : definition.Id.Trim();

        return definition with
        {
            Id = id,
            Name = definition.Name.Trim(),
            AccessToken = string.IsNullOrWhiteSpace(definition.AccessToken)
                ? CreateAccessToken()
                : definition.AccessToken,
            Endpoint = string.IsNullOrWhiteSpace(definition.Endpoint)
                ? null
                : definition.Endpoint.TrimEnd('/'),
            HealthChecks = NormalizeHealthChecks(definition.HealthChecks),
            Entries = definition.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => entry with
                {
                    Name = entry.Name.Trim(),
                    Value = entry.Value ?? string.Empty
                })
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

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

    public bool IsAuthorized(string resourceId, string? token)
    {
        var configurationStore = store.GetStore(resourceId);
        if (configurationStore?.AccessToken is null ||
            string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(configurationStore.AccessToken),
            System.Text.Encoding.UTF8.GetBytes(token));
    }

    private string GetEntriesEndpoint(string resourceId)
    {
        var configurationStore = store.GetStore(resourceId);
        var endpoint = configurationStore is null
            ? options.PublicBaseUrl.TrimEnd('/')
            : GetServiceBaseUrl(configurationStore);

        return $"{endpoint}/api/configuration/entries?resourceId={Uri.EscapeDataString(resourceId)}";
    }

    private static string CreateEnvironmentName(string name)
    {
        var normalized = EnvironmentNamePattern()
            .Replace(name.Trim().ToUpperInvariant(), "_")
            .Trim('_');

        return string.IsNullOrWhiteSpace(normalized)
            ? "STORE"
            : normalized;
    }

    private static string GetLogId(string resourceId) => $"{resourceId}:configuration-service-logs";

    private static string? GetResourceIdFromLogId(string logId) =>
        logId.EndsWith(":configuration-service-logs", StringComparison.OrdinalIgnoreCase)
            ? logId[..^":configuration-service-logs".Length]
            : null;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^A-Z0-9]+")]
    private static partial Regex EnvironmentNamePattern();

    private static string CreateAccessToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private sealed record ConfigurationStoreTemplateConfiguration(
        string? Endpoint,
        IReadOnlyList<ConfigurationEntry> Entries);
}
