using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface ISecretsVaultRuntimeSecretManager
{
    ValueTask<IReadOnlyList<SecretsVaultRuntimeSecret>> ListSecretsAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    ValueTask UpdateSecretsAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets,
        CancellationToken cancellationToken = default);
}

public sealed class SecretsVaultRuntimeSecretManager(
    SecretsVaultRuntimeOptions options) : ISecretsVaultRuntimeSecretManager
{
    private readonly SecretsVaultRuntimeOptions _options = options;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<IReadOnlyList<SecretsVaultRuntimeSecret>> ListSecretsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<SecretsVaultRuntimeSecret>>(
                _options.Secrets
                    .OrderBy(secret => secret.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(secret => secret.Version, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask UpdateSecretsAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(secrets);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _options.Secrets.Clear();
            foreach (var secret in secrets)
            {
                _options.Secrets.Add(secret);
            }

            WriteDefinition(resource, _options.Secrets.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    private void WriteDefinition(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets)
    {
        var directory = Path.Combine(_options.DefinitionsDirectory, SanitizeFileName(resource.ResourceId));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "secrets-vaults.json");
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, new[]
        {
            new
            {
                id = resource.ResourceId,
                name = resource.Name,
                displayName = resource.DisplayName,
                endpoint = resource.Endpoint,
                secrets = secrets.Select(secret => new
                {
                    secret.Name,
                    secret.Value,
                    secret.Version
                }).ToArray(),
                healthChecks = Array.Empty<object>()
            }
        }, SerializerOptions);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) || character is ':' or '/' or '\\'
                ? '_'
                : character));
    }
}
