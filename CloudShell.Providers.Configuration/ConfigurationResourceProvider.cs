using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Configuration;

public sealed partial class ConfigurationResourceProvider :
    IResourceProvider,
    IResourceProcedureProvider,
    IResourceTemplateProvider,
    IResourceEnvironmentVariableProvider,
    IProgrammaticResourceDeclarationProvider
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ConfigurationStore store;
    private readonly ConfigurationProviderOptions options;
    private readonly IHostEnvironment environment;
    private readonly ApplicationResourceStore? _applications;

    public ConfigurationResourceProvider(
        ConfigurationStore store,
        ConfigurationProviderOptions options,
        IServiceProvider serviceProvider,
        IHostEnvironment environment)
    {
        this.store = store;
        this.options = options;
        this.environment = environment;
        _applications = serviceProvider.GetService<ApplicationResourceStore>();

        foreach (var configurationStore in store.GetStores())
        {
            EnsureServiceApplication(EnsureServiceEndpoint(configurationStore), persist: false);
        }
    }

    public string Id => "configuration";

    public string DisplayName => "Configuration";

    public IReadOnlyList<CloudResource> GetResources()
    {
        var stores = store.GetStores();
        foreach (var configurationStore in stores)
        {
            EnsureServiceApplication(configurationStore, persist: false);
        }

        return stores
            .Select(CreateResource)
            .ToArray();
    }

    public ConfigurationStoreDefinition? GetStore(string id) => store.GetStore(id);

    public IReadOnlyList<ConfigurationStoreDefinition> GetStores() => store.GetStores();

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
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
            return Task.CompletedTask;
        }

        return SetupStoreAsync(
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
        EnsureServiceApplication(normalized, persist: true);

        await registrations.RegisterAsync(
            "applications",
            GetServiceResourceId(normalized.Id),
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            GetServiceDependencies(normalized.Id),
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
        EnsureServiceApplication(normalized, persist: true);
        if (registrations.GetRegistration(GetServiceResourceId(normalized.Id)) is null)
        {
            await registrations.RegisterAsync(
                "applications",
                GetServiceResourceId(normalized.Id),
                NormalizeGroupId(resourceGroupId),
                cancellationToken: cancellationToken);
        }
        else
        {
            await registrations.AssignToGroupAsync(
                GetServiceResourceId(normalized.Id),
                NormalizeGroupId(resourceGroupId),
                cancellationToken: cancellationToken);
        }

        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            GetServiceDependencies(normalized.Id),
            cancellationToken: cancellationToken);
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        store.Remove(context.Resource.Id);
        _applications?.Remove(GetServiceResourceId(context.Resource.Id));
        await context.Registrations.RemoveAsync(GetServiceResourceId(context.Resource.Id), cancellationToken);
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

    public bool CanExport(CloudResource resource) =>
        string.Equals(resource.EffectiveTypeId, "configuration.store", StringComparison.OrdinalIgnoreCase) &&
        store.GetStore(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        CloudResource resource,
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

    private CloudResource CreateResource(ConfigurationStoreDefinition configurationStore) =>
        new(
            configurationStore.Id,
            configurationStore.Name,
            "Configuration service",
            DisplayName,
            "local",
            ResourceState.Running,
            [new ResourceEndpoint("entries", GetEntriesEndpoint(configurationStore.Id), "http", false)],
            $"{configurationStore.Entries.Count} entries",
            DateTimeOffset.UtcNow,
            GetServiceDependencies(configurationStore.Id),
            TypeId: "configuration.store");

    private IReadOnlyList<string> GetServiceDependencies(string resourceId) =>
        _applications is null ? [] : [GetServiceResourceId(resourceId)];

    private void EnsureServiceApplication(ConfigurationStoreDefinition definition, bool persist)
    {
        if (_applications is null)
        {
            return;
        }

        var endpoint = GetServiceBaseUrl(definition);
        _applications.Save(new ApplicationResourceDefinition(
            GetServiceResourceId(definition.Id),
            $"{definition.Name} Configuration Service",
            options.ServiceExecutablePath,
            CreateServiceArguments(endpoint),
            options.ServiceWorkingDirectory,
            endpoint,
            [
                new("ASPNETCORE_ENVIRONMENT", environment.EnvironmentName),
                new("CloudShell__ConfigurationService__DefinitionsPath", ResolveDefinitionsPath()),
                new("CloudShell__ConfigurationService__ResourceId", definition.Id)
            ],
            ApplicationLifetime.Detached),
            persist);
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
        => CreateServiceResourceId(resourceId, options.ServiceResourceIdPrefix);

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
