using System.Globalization;
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
            var normalizedSecrets = NormalizeSecrets(secrets);
            _options.Secrets.Clear();
            foreach (var secret in normalizedSecrets)
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
            var normalizedCertificates = NormalizeCertificates(certificates);
            _options.Certificates.Clear();
            foreach (var certificate in normalizedCertificates)
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

    private IReadOnlyList<SecretsVaultRuntimeSecret> NormalizeSecrets(
        IReadOnlyList<SecretsVaultRuntimeSecret> secrets)
    {
        var normalized = new List<SecretsVaultRuntimeSecret>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var secret in secrets)
        {
            var version = NormalizeVersion(secret.Version);
            var existing = version is null
                ? null
                : _options.Secrets.FirstOrDefault(candidate =>
                    MatchesVersionedEntry(candidate.Name, candidate.Version, secret.Name, version));
            if (version is null)
            {
                version = CreateUniqueVersion(keys);
            }
            else if (existing is not null &&
                !string.Equals(existing.Value, secret.Value, StringComparison.Ordinal))
            {
                AddSecretVersion(normalized, keys, existing);
                version = CreateUniqueVersion(keys);
            }

            AddSecretVersion(normalized, keys, secret with { Version = version });
        }

        return normalized;
    }

    private IReadOnlyList<SecretsVaultRuntimeCertificate> NormalizeCertificates(
        IReadOnlyList<SecretsVaultRuntimeCertificate> certificates)
    {
        var normalized = new List<SecretsVaultRuntimeCertificate>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in certificates)
        {
            var version = NormalizeVersion(certificate.Version);
            var existing = version is null
                ? null
                : _options.Certificates.FirstOrDefault(candidate =>
                    MatchesVersionedEntry(candidate.Name, candidate.Version, certificate.Name, version));
            if (version is null)
            {
                version = CreateUniqueVersion(keys);
            }
            else if (existing is not null &&
                !string.Equals(existing.Value, certificate.Value, StringComparison.Ordinal))
            {
                AddCertificateVersion(normalized, keys, existing);
                version = CreateUniqueVersion(keys);
            }

            AddCertificateVersion(normalized, keys, certificate with { Version = version });
        }

        return normalized;
    }

    private static void AddSecretVersion(
        List<SecretsVaultRuntimeSecret> secrets,
        HashSet<string> keys,
        SecretsVaultRuntimeSecret secret)
    {
        if (!keys.Add(CreateKey(secret.Name, secret.Version)))
        {
            throw new InvalidOperationException(
                CreateDuplicateMessage("Secret", secret.Name, secret.Version));
        }

        secrets.Add(secret);
    }

    private static void AddCertificateVersion(
        List<SecretsVaultRuntimeCertificate> certificates,
        HashSet<string> keys,
        SecretsVaultRuntimeCertificate certificate)
    {
        if (!keys.Add(CreateKey(certificate.Name, certificate.Version)))
        {
            throw new InvalidOperationException(
                CreateDuplicateMessage("Certificate", certificate.Name, certificate.Version));
        }

        certificates.Add(certificate);
    }

    private static bool MatchesVersionedEntry(
        string candidateName,
        string? candidateVersion,
        string name,
        string version) =>
        string.Equals(candidateName, name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(candidateVersion, version, StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeVersion(string? version) =>
        string.IsNullOrWhiteSpace(version) ? null : version.Trim();

    private static string CreateUniqueVersion(HashSet<string> existingKeys)
    {
        string version;
        do
        {
            version = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture) +
                "-" +
                Guid.NewGuid().ToString("N")[..8];
        }
        while (existingKeys.Any(key => key.EndsWith($"\u001f{version}", StringComparison.OrdinalIgnoreCase)));

        return version;
    }

    private static string CreateKey(string name, string? version) =>
        $"{name}\u001f{version}";

    private static string CreateDuplicateMessage(
        string artifactKind,
        string name,
        string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? $"{artifactKind} '{name}' is defined more than once."
            : $"{artifactKind} '{name}' version '{version}' is defined more than once.";

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
