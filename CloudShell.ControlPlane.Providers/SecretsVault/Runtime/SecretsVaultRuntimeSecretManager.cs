using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface ISecretsVaultRuntimeSecretManager
{
    ValueTask<IReadOnlyList<SecretsVaultRuntimeSecret>> ListSecretsAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<SecretsVaultRuntimeCertificate>> ListCertificatesAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    ValueTask UpdateSecretsAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets,
        CancellationToken cancellationToken = default);

    ValueTask UpdateCertificatesAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeCertificate> certificates,
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

    public ValueTask<IReadOnlyList<SecretsVaultRuntimeCertificate>> ListCertificatesAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<SecretsVaultRuntimeCertificate>>(
                _options.Certificates
                    .OrderBy(certificate => certificate.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(certificate => certificate.Version, StringComparer.OrdinalIgnoreCase)
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

            WriteDefinition(
                resource,
                _options.Secrets.ToArray(),
                _options.Certificates.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask UpdateCertificatesAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeCertificate> certificates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(certificates);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _options.Certificates.Clear();
            foreach (var certificate in certificates)
            {
                _options.Certificates.Add(certificate);
            }

            WriteDefinition(
                resource,
                _options.Secrets.ToArray(),
                _options.Certificates.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    private void WriteDefinition(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets,
        IReadOnlyList<SecretsVaultRuntimeCertificate> certificates)
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
                certificates = certificates.Select(certificate => new
                {
                    certificate.Name,
                    certificate.Value,
                    certificate.Version,
                    certificate.ContentType,
                    certificate.Thumbprint,
                    certificate.Subject,
                    certificate.NotBefore,
                    certificate.Expires,
                    certificate.HasPrivateKey
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
