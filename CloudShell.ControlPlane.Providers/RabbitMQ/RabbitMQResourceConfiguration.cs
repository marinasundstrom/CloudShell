using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

public sealed record RabbitMQBootstrapCredentials(
    string UserName,
    string Password,
    bool IsCloudShellManaged);

public sealed record RabbitMQStartupConfiguration(
    RabbitMQBootstrapCredentials? Credentials,
    string? VirtualHost);

public interface IRabbitMQBootstrapCredentialProvider
{
    RabbitMQBootstrapCredentials? ResolveStartupCredentials(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration);

    RabbitMQBootstrapCredentials ResolveManagementCredentials(
        ResourceModelResource resource,
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options);

    RabbitMQBootstrapCredentials ResolveManagementCredentials(
        ResourceManagerResource resource,
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options);

    void Forget(string resourceId);
}

public abstract class RabbitMQBootstrapCredentialProviderBase :
    IRabbitMQBootstrapCredentialProvider
{
    public RabbitMQBootstrapCredentials? ResolveStartupCredentials(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(configuration);

        var declared = ResolveDeclaredCredentials(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserName),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserPassword),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserManaged));
        if (declared is not null)
        {
            return declared;
        }

        var configuredUserName = ResolveConfiguredValue(
            configuration,
            definition.UsernameConfigurationKey);
        var configuredPassword = ResolveConfiguredValue(
            configuration,
            definition.PasswordConfigurationKey);
        var hasRuntimeOverride =
            !string.IsNullOrWhiteSpace(configuredUserName) ||
            !string.IsNullOrWhiteSpace(configuredPassword) ||
            !string.Equals(definition.Username, RabbitMQResourceDefaults.DefaultUsername, StringComparison.Ordinal) ||
            !string.Equals(definition.Password, RabbitMQResourceDefaults.DefaultPassword, StringComparison.Ordinal);
        if (!hasRuntimeOverride)
        {
            return null;
        }

        return new(
            configuredUserName ?? definition.Username,
            configuredPassword ?? definition.Password,
            IsCloudShellManaged: false);
    }

    public RabbitMQBootstrapCredentials ResolveManagementCredentials(
        ResourceModelResource resource,
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var declared = ResolveDeclaredCredentials(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserName),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserPassword),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserManaged));
        if (declared is not null)
        {
            return declared;
        }

        return new(
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementUserName(configuration, options),
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementPassword(configuration, options),
            IsCloudShellManaged: false);
    }

    public RabbitMQBootstrapCredentials ResolveManagementCredentials(
        ResourceManagerResource resource,
        IConfiguration configuration,
        RabbitMQManagementAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var attributes = resource.ResourceAttributes;
        var declared = ResolveDeclaredCredentials(
            resource.Id,
            resource.Name,
            attributes.GetValueOrDefault(RabbitMQResourceTypeProvider.Attributes.UserName.ToString()),
            attributes.GetValueOrDefault(RabbitMQResourceTypeProvider.Attributes.UserPassword.ToString()),
            attributes.GetValueOrDefault(RabbitMQResourceTypeProvider.Attributes.UserManaged.ToString()));
        if (declared is not null)
        {
            return declared;
        }

        return new(
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementUserName(configuration, options),
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementPassword(configuration, options),
            IsCloudShellManaged: false);
    }

    public abstract void Forget(string resourceId);

    protected abstract RabbitMQBootstrapCredentials ResolveManagedBootstrapCredentials(
        string resourceId,
        string resourceName);

    protected static RabbitMQBootstrapCredentials CreateManagedBootstrapCredentials(
        string resourceName)
    {
        var suffix = Convert
            .ToHexString(RandomNumberGenerator.GetBytes(4))
            .ToLowerInvariant();
        var userName = $"cloudshell-{SanitizeUserName(resourceName)}-{suffix}";
        var password = Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return new(userName, password, IsCloudShellManaged: true);
    }

    private RabbitMQBootstrapCredentials? ResolveDeclaredCredentials(
        string resourceId,
        string resourceName,
        string? userName,
        string? password,
        string? managed)
    {
        if (bool.TryParse(managed, out var managedValue) && managedValue)
        {
            return ResolveManagedBootstrapCredentials(resourceId, resourceName);
        }

        return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password)
            ? new(userName.Trim(), password, IsCloudShellManaged: false)
            : null;
    }

    private static string? ResolveConfiguredValue(
        IConfiguration configuration,
        string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]
            : null;

    private static string SanitizeUserName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_' or '.'
                    ? character
                    : '-');
        }

        var sanitized = builder
            .ToString()
            .Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "rabbitmq" : sanitized;
    }
}

public sealed class InMemoryRabbitMQBootstrapCredentialProvider :
    RabbitMQBootstrapCredentialProviderBase
{
    private readonly Dictionary<string, RabbitMQBootstrapCredentials> managedCredentials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public override void Forget(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return;
        }

        lock (sync)
        {
            managedCredentials.Remove(resourceId.Trim());
        }
    }

    protected override RabbitMQBootstrapCredentials ResolveManagedBootstrapCredentials(
        string resourceId,
        string resourceName)
    {
        lock (sync)
        {
            if (managedCredentials.TryGetValue(resourceId, out var credentials))
            {
                return credentials;
            }

            credentials = CreateManagedBootstrapCredentials(resourceName);
            managedCredentials[resourceId] = credentials;
            return credentials;
        }
    }
}

public sealed class LocalRabbitMQBootstrapCredentialProvider(
    IHostEnvironment hostEnvironment) : RabbitMQBootstrapCredentialProviderBase
{
    private readonly Dictionary<string, RabbitMQBootstrapCredentials> managedCredentials =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public override void Forget(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return;
        }

        var normalizedResourceId = resourceId.Trim();
        lock (sync)
        {
            managedCredentials.Remove(normalizedResourceId);
            var path = GetCredentialFilePath(normalizedResourceId);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    protected override RabbitMQBootstrapCredentials ResolveManagedBootstrapCredentials(
        string resourceId,
        string resourceName)
    {
        lock (sync)
        {
            if (managedCredentials.TryGetValue(resourceId, out var credentials))
            {
                return credentials;
            }

            credentials =
                TryReadManagedBootstrapCredentials(resourceId) ??
                CreateManagedBootstrapCredentials(resourceName);
            managedCredentials[resourceId] = credentials;
            WriteManagedBootstrapCredentials(resourceId, credentials);
            return credentials;
        }
    }

    private RabbitMQBootstrapCredentials? TryReadManagedBootstrapCredentials(
        string resourceId)
    {
        var path = GetCredentialFilePath(resourceId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize<StoredRabbitMQBootstrapCredentials>(
                File.ReadAllText(path),
                RabbitMQBootstrapCredentialJson.Options);
            if (stored is null ||
                !string.Equals(stored.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(stored.UserName) ||
                string.IsNullOrWhiteSpace(stored.Password))
            {
                throw new InvalidOperationException(
                    "The credential file did not contain a valid RabbitMQ bootstrap credential.");
            }

            return new(stored.UserName, stored.Password, IsCloudShellManaged: true);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"RabbitMQ managed bootstrap credentials for resource '{resourceId}' could not be read from '{path}'.",
                exception);
        }
    }

    private void WriteManagedBootstrapCredentials(
        string resourceId,
        RabbitMQBootstrapCredentials credentials)
    {
        var path = GetCredentialFilePath(resourceId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                new StoredRabbitMQBootstrapCredentials(
                    resourceId,
                    credentials.UserName,
                    credentials.Password),
                RabbitMQBootstrapCredentialJson.Options));
        TryRestrictFileAccess(path);
    }

    private string GetCredentialFilePath(string resourceId) =>
        Path.Combine(
            hostEnvironment.ContentRootPath,
            "Data",
            "cloudshell",
            "rabbitmq",
            "bootstrap-credentials",
            $"{HashResourceId(resourceId)}.json");

    private static string HashResourceId(string resourceId) =>
        Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(resourceId.Trim())), 0, 12)
            .ToLowerInvariant();

    private static void TryRestrictFileAccess(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(
                    path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed record StoredRabbitMQBootstrapCredentials(
        string ResourceId,
        string UserName,
        string Password);
}

file static class RabbitMQBootstrapCredentialJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        WriteIndented = true
    };
}

internal static class RabbitMQResourceConfiguration
{
    public static RabbitMQStartupConfiguration ResolveStartupConfiguration(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration,
        IRabbitMQBootstrapCredentialProvider credentials)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(credentials);

        return new(
            credentials.ResolveStartupCredentials(resource, definition, configuration),
            ResolveRuntimeVirtualHost(resource, definition, configuration));
    }

    public static string ResolveVirtualHost(
        ResourceModelResource resource,
        RabbitMQManagementAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(options);

        return ResolveVirtualHost(
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.VirtualHost),
            options.VirtualHost);
    }

    public static string ResolveVirtualHost(
        ResourceManagerResource resource,
        RabbitMQManagementAccessOptions options)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(options);

        return ResolveVirtualHost(
            resource.ResourceAttributes.GetValueOrDefault(
                RabbitMQResourceTypeProvider.Attributes.VirtualHost.ToString()),
            options.VirtualHost);
    }

    private static string? ResolveRuntimeVirtualHost(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration)
    {
        var declared = resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.VirtualHost);
        if (!string.IsNullOrWhiteSpace(declared))
        {
            return declared.Trim();
        }

        var configured = ResolveConfiguredValue(configuration, definition.VirtualHostConfigurationKey);
        var virtualHost = configured ?? definition.VirtualHost;
        return string.IsNullOrWhiteSpace(virtualHost) ||
            string.Equals(virtualHost, RabbitMQResourceDefaults.DefaultVirtualHost, StringComparison.Ordinal)
                ? null
                : virtualHost.Trim();
    }

    private static string ResolveVirtualHost(
        string? resourceVirtualHost,
        string? configuredVirtualHost)
    {
        if (!string.IsNullOrWhiteSpace(resourceVirtualHost))
        {
            return resourceVirtualHost.Trim();
        }

        return string.IsNullOrWhiteSpace(configuredVirtualHost)
            ? RabbitMQResourceDefaults.DefaultVirtualHost
            : configuredVirtualHost.Trim();
    }

    private static string? ResolveConfiguredValue(
        IConfiguration configuration,
        string? key) =>
        !string.IsNullOrWhiteSpace(key) &&
        !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]
            : null;
}
