namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ConfigurationStoreRuntimeOptions
{
    public string ServiceProjectPath { get; set; } =
        "CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj";

    public string? ServiceWorkingDirectory { get; set; }

    public string DefinitionsDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceDefinitions",
        "ConfigurationStore");

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);

    public IList<ConfigurationStoreRuntimeEntry> Entries { get; } = [];

    public string? ServiceAuthenticationIssuer { get; set; }

    public string? ServiceAuthenticationAudience { get; set; }

    public string? ServiceAuthenticationSigningKeyPem { get; set; }

    public string? ServiceBearerAuthority { get; set; }

    public string? ServiceBearerMetadataAddress { get; set; }

    public string? ServiceBearerIssuer { get; set; }

    public string? ServiceBearerAudience { get; set; }

    public bool ServiceBearerRequireHttpsMetadata { get; set; } = true;

    public string? ServiceBearerSigningKeyPem { get; set; }
}

public sealed record ConfigurationStoreRuntimeEntry(
    string Name,
    string Value,
    bool IsSecret = false);

public interface IConfigurationStoreRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface IConfigurationStoreRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationStoreProcessRuntimeController(
    ConfigurationStoreRuntimeOptions? options = null) :
    IConfigurationStoreRuntimeController,
    IConfigurationStoreRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConfigurationStoreRuntimeOptions _options =
        options ?? new ConfigurationStoreRuntimeOptions();
    private readonly ResourceWebAppProcessRuntime _runtime = new();

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        _runtime.GetStatus(resource);

    public async ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        await _runtime.GetMonitoringSnapshotAsync(resourceId, cancellationToken);

    public async ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        await _runtime.ExecuteAsync(
            resource,
            operationId,
            ConfigurationStoreResourceTypeProvider.Attributes.Endpoint,
            new ResourceWebAppProcessOptions(
                _options.ServiceProjectPath,
                "CloudShell__ConfigurationStoreService__DefinitionsPath",
                "CloudShell__ConfigurationStoreService__ResourceId",
                "configuration-stores.json",
                _options.StartupTimeout)
            {
                ServiceWorkingDirectory = _options.ServiceWorkingDirectory,
                DefinitionsDirectory = _options.DefinitionsDirectory,
                EnvironmentVariables = CreateEnvironmentVariables(_options)
            },
            CreateDefinition,
            "configuration.store",
            "Configuration Store",
            cancellationToken);

    public async ValueTask DisposeAsync() =>
        await _runtime.DisposeAsync();

    public void Dispose() =>
        _runtime.Dispose();

    private object CreateDefinition(
        Resource resource,
        string? endpoint) =>
        new
        {
            id = resource.EffectiveResourceId,
            name = resource.Name,
            displayName = resource.State.DisplayName,
            endpoint,
            entries = _options.Entries.Select(entry => new
            {
                entry.Name,
                entry.Value,
                entry.IsSecret
            }).ToArray(),
            healthChecks = Array.Empty<object>()
        };

    private static IReadOnlyDictionary<string, string> CreateEnvironmentVariables(
        ConfigurationStoreRuntimeOptions options)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authentication__BuiltInAuthority__Enabled"] = "true"
        };

        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Issuer",
            options.ServiceAuthenticationIssuer);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__Audience",
            options.ServiceAuthenticationAudience);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__BuiltInAuthority__SigningKeyPem",
            options.ServiceAuthenticationSigningKeyPem);
        AddServiceBearerEnvironment(variables, options);

        return variables;
    }

    private static void AddServiceBearerEnvironment(
        IDictionary<string, string> variables,
        ConfigurationStoreRuntimeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceBearerAuthority) &&
            string.IsNullOrWhiteSpace(options.ServiceBearerMetadataAddress) &&
            string.IsNullOrWhiteSpace(options.ServiceBearerSigningKeyPem))
        {
            return;
        }

        variables["Authentication__ServiceBearer__Enabled"] = "true";
        AddIfNotWhiteSpace(
            variables,
            "Authentication__ServiceBearer__Authority",
            options.ServiceBearerAuthority);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__ServiceBearer__MetadataAddress",
            options.ServiceBearerMetadataAddress);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__ServiceBearer__Issuer",
            options.ServiceBearerIssuer);
        AddIfNotWhiteSpace(
            variables,
            "Authentication__ServiceBearer__Audience",
            options.ServiceBearerAudience);
        variables["Authentication__ServiceBearer__RequireHttpsMetadata"] =
            options.ServiceBearerRequireHttpsMetadata ? "true" : "false";
        AddIfNotWhiteSpace(
            variables,
            "Authentication__ServiceBearer__SigningKeyPem",
            options.ServiceBearerSigningKeyPem);
    }

    private static void AddIfNotWhiteSpace(
        IDictionary<string, string> variables,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            variables[name] = value;
        }
    }
}

public sealed class NoopConfigurationStoreRuntimeController :
    IConfigurationStoreRuntimeController,
    IConfigurationStoreRuntimeMonitor
{
    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        ResourceWebAppRuntimeStatus.Unknown;

    public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ResourceProcessMonitoringSnapshot?>(null);

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
