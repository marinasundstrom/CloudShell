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
}

public interface IConfigurationStoreRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationStoreProcessRuntimeController(
    ConfigurationStoreRuntimeOptions? options = null) :
    IConfigurationStoreRuntimeController,
    IDisposable,
    IAsyncDisposable
{
    private readonly ConfigurationStoreRuntimeOptions _options =
        options ?? new ConfigurationStoreRuntimeOptions();
    private readonly ResourceWebAppProcessRuntime _runtime = new();

    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        _runtime.GetStatus(resource);

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
                DefinitionsDirectory = _options.DefinitionsDirectory
            },
            CreateDefinition,
            "configuration.store",
            "Configuration Store",
            cancellationToken);

    public async ValueTask DisposeAsync() =>
        await _runtime.DisposeAsync();

    public void Dispose() =>
        _runtime.Dispose();

    private static object CreateDefinition(
        Resource resource,
        string? endpoint) =>
        new
        {
            id = resource.EffectiveResourceId,
            name = resource.Name,
            displayName = resource.State.DisplayName,
            endpoint,
            entries = Array.Empty<object>(),
            healthChecks = Array.Empty<object>()
        };
}

public sealed class NoopConfigurationStoreRuntimeController :
    IConfigurationStoreRuntimeController
{
    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        ResourceWebAppRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
