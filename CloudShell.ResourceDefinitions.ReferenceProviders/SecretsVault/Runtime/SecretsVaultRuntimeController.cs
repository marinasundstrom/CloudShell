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

    public IList<SecretsVaultRuntimeSecret> Secrets { get; } = [];

    public string? ServiceAuthenticationIssuer { get; set; }

    public string? ServiceAuthenticationAudience { get; set; }

    public string? ServiceAuthenticationSigningKeyPem { get; set; }
}

public sealed record SecretsVaultRuntimeSecret(
    string Name,
    string Value,
    string? Version = null);

public interface ISecretsVaultRuntimeController
{
    ResourceWebAppRuntimeStatus GetStatus(Resource resource);

    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
        Resource resource,
        ResourceOperationId operationId,
        CancellationToken cancellationToken = default);
}

public interface ISecretsVaultRuntimeMonitor
{
    ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
        string resourceId,
        CancellationToken cancellationToken = default);
}

public sealed class SecretsVaultProcessRuntimeController(
    SecretsVaultRuntimeOptions? options = null) :
    ISecretsVaultRuntimeController,
    ISecretsVaultRuntimeMonitor,
    IDisposable,
    IAsyncDisposable
{
    private readonly SecretsVaultRuntimeOptions _options =
        options ?? new SecretsVaultRuntimeOptions();
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
            SecretsVaultResourceTypeProvider.Attributes.Endpoint,
            new ResourceWebAppProcessOptions(
                _options.ServiceProjectPath,
                "CloudShell__SecretsVaultService__DefinitionsPath",
                "CloudShell__SecretsVaultService__ResourceId",
                "secrets-vaults.json",
                _options.StartupTimeout)
            {
                ServiceWorkingDirectory = _options.ServiceWorkingDirectory,
                DefinitionsDirectory = _options.DefinitionsDirectory,
                EnvironmentVariables = CreateEnvironmentVariables(_options)
            },
            CreateDefinition,
            "secrets.vault",
            "Secrets Vault",
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
            secrets = _options.Secrets.Select(secret => new
            {
                secret.Name,
                secret.Value,
                secret.Version
            }).ToArray(),
            healthChecks = Array.Empty<object>()
        };

    private static IReadOnlyDictionary<string, string> CreateEnvironmentVariables(
        SecretsVaultRuntimeOptions options)
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

        return variables;
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

public sealed class NoopSecretsVaultRuntimeController :
    ISecretsVaultRuntimeController,
    ISecretsVaultRuntimeMonitor
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
