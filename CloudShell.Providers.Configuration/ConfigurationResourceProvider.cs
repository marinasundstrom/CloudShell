using CloudShell.Abstractions.ResourceManager;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloudShell.Providers.Configuration;

public sealed partial class ConfigurationResourceProvider(
    ConfigurationStore store,
    ConfigurationProviderOptions options) :
    IResourceProvider,
    IResourceProcedureProvider,
    IResourceTemplateProvider,
    IResourceEnvironmentVariableProvider
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);

    public string Id => "configuration";

    public string DisplayName => "Configuration";

    public IReadOnlyList<CloudResource> GetResources() => store
        .GetStores()
        .Select(CreateResource)
        .ToArray();

    public ConfigurationStoreDefinition? GetStore(string id) => store.GetStore(id);

    public IReadOnlyList<ConfigurationStoreDefinition> GetStores() => store.GetStores();

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
        store.Save(normalized);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public async Task UpdateStoreAsync(
        ConfigurationStoreDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDefinition(definition);
        if (store.GetStore(normalized.Id) is null)
        {
            throw new InvalidOperationException($"Configuration service '{normalized.Id}' is not configured.");
        }

        store.Save(normalized);
        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            cancellationToken: cancellationToken);
    }

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        store.Remove(context.Resource.Id);
        return RemoveRegistrationAsync(context, cancellationToken);
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
            configuration.Entries);

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

    private async Task<ResourceProcedureResult> RemoveRegistrationAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken)
    {
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Configuration service removed.");
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
            [],
            TypeId: "configuration.store");

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

    private string GetEntriesEndpoint(string resourceId) =>
        $"{options.PublicBaseUrl.TrimEnd('/')}/api/configuration/entries?resourceId={Uri.EscapeDataString(resourceId)}";

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
        IReadOnlyList<ConfigurationEntry> Entries);
}
