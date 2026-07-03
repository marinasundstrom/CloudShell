using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Providers;

internal sealed record RabbitMQBootstrapCredentials(
    string UserName,
    string Password,
    bool IsCloudShellManaged);

internal sealed record RabbitMQStartupConfiguration(
    RabbitMQBootstrapCredentials? Credentials,
    string? VirtualHost);

internal static class RabbitMQResourceConfiguration
{
    private const string ManagedBootstrapPasswordSaltConfigurationKey =
        "CloudShell:RabbitMQ:ManagedBootstrapPasswordSalt";

    public static RabbitMQStartupConfiguration ResolveStartupConfiguration(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(configuration);

        return new(
            ResolveRuntimeCredentials(resource, definition, configuration),
            ResolveRuntimeVirtualHost(resource, definition, configuration));
    }

    public static RabbitMQBootstrapCredentials ResolveManagementCredentials(
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
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserManaged),
            configuration);
        if (declared is not null)
        {
            return declared;
        }

        return new(
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementUserName(configuration, options),
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementPassword(configuration, options),
            IsCloudShellManaged: false);
    }

    public static RabbitMQBootstrapCredentials ResolveManagementCredentials(
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
            attributes.GetValueOrDefault(RabbitMQResourceTypeProvider.Attributes.UserManaged.ToString()),
            configuration);
        if (declared is not null)
        {
            return declared;
        }

        return new(
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementUserName(configuration, options),
            DefaultRabbitMQPrincipalCredentialProvider.ResolveManagementPassword(configuration, options),
            IsCloudShellManaged: false);
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

    private static RabbitMQBootstrapCredentials? ResolveRuntimeCredentials(
        ResourceModelResource resource,
        LocalRabbitMQDockerDefinition definition,
        IConfiguration configuration)
    {
        var declared = ResolveDeclaredCredentials(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserName),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserPassword),
            resource.Attributes.GetString(RabbitMQResourceTypeProvider.Attributes.UserManaged),
            configuration);
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

    private static RabbitMQBootstrapCredentials? ResolveDeclaredCredentials(
        string resourceId,
        string resourceName,
        string? userName,
        string? password,
        string? managed,
        IConfiguration configuration)
    {
        if (bool.TryParse(managed, out var managedValue) && managedValue)
        {
            return CreateManagedBootstrapCredentials(resourceId, resourceName, configuration);
        }

        return !string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password)
            ? new(userName.Trim(), password, IsCloudShellManaged: false)
            : null;
    }

    private static RabbitMQBootstrapCredentials CreateManagedBootstrapCredentials(
        string resourceId,
        string resourceName,
        IConfiguration configuration)
    {
        var userName = $"cloudshell-{SanitizeUserName(resourceName)}";
        var configuredSalt = configuration[ManagedBootstrapPasswordSaltConfigurationKey];
        var salt = string.IsNullOrWhiteSpace(configuredSalt)
            ? "CloudShell.RabbitMQ.ManagedBootstrap"
            : configuredSalt;
        var input = $"{salt}|{resourceId.Trim()}";
        var password = Convert
            .ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(input)))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return new(userName, password, IsCloudShellManaged: true);
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
