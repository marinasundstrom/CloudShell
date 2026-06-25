namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SecretsVaultRuntimeOptions
{
    public string ServiceProjectPath { get; set; } =
        "CloudShell.SecretsVaultService/CloudShell.SecretsVaultService.csproj";

    public string? ServiceWorkingDirectory { get; set; }

    public string DefinitionsDirectory { get; set; } = Path.Combine(
        Path.GetTempPath(),
        "CloudShell.ResourceDefinitions",
        "SecretsVault");

    public TimeSpan StartupTimeout { get; set; } = TimeSpan.FromSeconds(60);
}

public interface ISecretsVaultRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public sealed class SecretsVaultProcessRuntimeController(
    SecretsVaultRuntimeOptions? options = null) :
    ISecretsVaultRuntimeController,
    IDisposable,
    IAsyncDisposable
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();
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
            SecretsVaultResourceTypeProvider.Attributes.Endpoint,
            new ResourceWebAppProcessOptions(
                _options.ServiceProjectPath,
                "CloudShell__SecretsVaultService__DefinitionsPath",
                "CloudShell__SecretsVaultService__ResourceId",
                "secrets-vaults.json",
                _options.StartupTimeout)
            {
                ServiceWorkingDirectory = _options.ServiceWorkingDirectory,
                DefinitionsDirectory = _options.DefinitionsDirectory
            },
            CreateDefinition,
            "secrets.vault",
            "Secrets Vault",
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
            secrets = Array.Empty<object>(),
            healthChecks = Array.Empty<object>()
        };
}

public sealed class NoopSecretsVaultRuntimeController :
    ISecretsVaultRuntimeController
{
    public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
        ResourceWebAppRuntimeStatus.Unknown;

    public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
}
