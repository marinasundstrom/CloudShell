using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Configuration;

public static class ConfigurationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddConfigurationProvider(
        this ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddConfigurationProviderCore(builder, configure);
        builder.AddExtensionIfMissing(new ConfigurationProviderExtension(), activationPolicy);
        builder.AddExtensionIfMissing(new SecretsProviderExtension(), activationPolicy);
        return builder;
    }

    public static IControlPlaneBuilder AddConfigurationProvider(
        this IControlPlaneBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddConfigurationProviderCore(builder, configure);
        builder.AddExtensionIfMissing(new ConfigurationProviderExtension(), activationPolicy);
        builder.AddExtensionIfMissing(new SecretsProviderExtension(), activationPolicy);
        return builder;
    }

    public static ICloudShellBuilder AddSecretsProvider(
        this ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddSecretsProviderCore(builder, configure);
        builder.AddExtensionIfMissing(new SecretsProviderExtension(), activationPolicy);
        return builder;
    }

    public static IControlPlaneBuilder AddSecretsProvider(
        this IControlPlaneBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddSecretsProviderCore(builder, configure);
        builder.AddExtensionIfMissing(new SecretsProviderExtension(), activationPolicy);
        return builder;
    }

    private static void AddConfigurationProviderCore(
        ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddConfigurationProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddLocalProcessRunner();
    }

    private static void AddSecretsProviderCore(
        ICloudShellBuilder builder,
        Action<ConfigurationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddConfigurationProviderOptions();
        configure?.Invoke(options);
        builder.Services.AddLocalProcessRunner();
    }

    public static IConfigurationStoreResourceBuilder AddConfigurationStore(
        this IResourceDeclarationBuilder builder,
        string id,
        IReadOnlyList<ConfigurationEntry>? entries = null)
    {
        var definition = new ConfigurationStoreDefinition(
            id,
            CreateDisplayName(id),
            entries);
        var declared = new DeclaredConfigurationStore(definition);
        var options = builder.Services.GetOrAddConfigurationProviderOptions();

        options.DeclaredStores.Add(declared);

        var resource = builder.Declare(
            "configuration",
            id,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    Name = GetDisplayName(declaration, CreateDisplayName(id))
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ConfigurationStoreResourceBuilder(resource, declared);
    }

    public static IHostConfigurationSourceResourceBuilder AddHostConfigurationSource(
        this IResourceDeclarationBuilder builder,
        string id,
        IReadOnlyList<string>? entries = null)
    {
        var definition = new HostConfigurationSourceDefinition(
            id,
            CreateDisplayName(id),
            entries);
        var declared = new DeclaredHostConfigurationSource(definition);
        var options = builder.Services.GetOrAddConfigurationProviderOptions();

        options.DeclaredHostConfigurationSources.Add(declared);

        var resource = builder.Declare(
            HostConfigurationSourceProvider.ProviderId,
            id,
            resourceClass: ResourceClass.Configuration,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    Name = GetDisplayName(declaration, CreateDisplayName(id))
                };
            });

        return new HostConfigurationSourceResourceBuilder(resource, declared);
    }

    public static ISecretsVaultResourceBuilder AddSecretsVault(
        this IResourceDeclarationBuilder builder,
        string id,
        IReadOnlyList<SecretsVaultSecret>? secrets = null)
    {
        var definition = new SecretsVaultDefinition(
            id,
            CreateDisplayName(id),
            secrets);
        var declared = new DeclaredSecretsVault(definition);
        var options = builder.Services.GetOrAddConfigurationProviderOptions();

        options.DeclaredSecretsVaults.Add(declared);

        var resource = builder.Declare(
            SecretsVaultProvider.ProviderId,
            id,
            resourceClass: ResourceClass.SecretsVault,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    Name = GetDisplayName(declaration, CreateDisplayName(id))
                };
            });

        return new SecretsVaultResourceBuilder(resource, declared);
    }

    private static ConfigurationProviderOptions GetOrAddConfigurationProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(ConfigurationProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ConfigurationProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new ConfigurationProviderOptions();
        services.AddSingleton(options);
        return options;
    }

    private static string CreateDisplayName(string resourceId)
    {
        var name = resourceId.Contains(':', StringComparison.Ordinal)
            ? resourceId[(resourceId.IndexOf(':', StringComparison.Ordinal) + 1)..]
            : resourceId;
        return string.Join(
            " ",
            name.Split(['-', '_', '.', ':', '/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => string.Concat(segment[..1].ToUpperInvariant(), segment[1..])));
    }

    private static string GetDisplayName(ResourceDeclaration declaration, string fallback) =>
        string.IsNullOrWhiteSpace(declaration.DisplayName)
            ? fallback
            : declaration.DisplayName;

    private static void AddExtensionIfMissing<TExtension>(
        this ICloudShellBuilder builder,
        TExtension extension,
        CloudShellExtensionActivationPolicy activationPolicy)
        where TExtension : class, ICloudShellExtension
    {
        if (builder.Services.Any(descriptor =>
                descriptor.ServiceType == typeof(ICloudShellExtension) &&
                descriptor.ImplementationInstance is TExtension))
        {
            return;
        }

        builder.AddExtension(extension, activationPolicy);
    }
}

public interface ISecretsVaultResourceBuilder : IResourceBuilder
{
    ISecretsVaultResourceBuilder WithSecrets(IReadOnlyList<SecretsVaultSecret> secrets);

    ISecretsVaultResourceBuilder WithSecret(
        string name,
        string value,
        string? version = null);

    SecretReference Secret(
        string name,
        string? version = null);

    new ISecretsVaultResourceBuilder DependsOn(string resourceId);

    new ISecretsVaultResourceBuilder DependsOn(IResourceBuilder resource);

    new ISecretsVaultResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new ISecretsVaultResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new ISecretsVaultResourceBuilder WithResourceGroup(string? resourceGroupId);

    new ISecretsVaultResourceBuilder WithParent(string? parentResourceId);

    new ISecretsVaultResourceBuilder WithParent(IResourceBuilder resource);

    new ISecretsVaultResourceBuilder WithReference(string resourceId);

    new ISecretsVaultResourceBuilder WithReference(IResourceBuilder resource);

    new ISecretsVaultResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new ISecretsVaultResourceBuilder Persist(bool overwrite = false);
}

internal sealed class SecretsVaultResourceBuilder(
    IResourceBuilder inner,
    DeclaredSecretsVault declared) : ISecretsVaultResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public ISecretsVaultResourceBuilder WithSecrets(IReadOnlyList<SecretsVaultSecret> secrets)
    {
        declared.Definition = declared.Definition with { Secrets = secrets };
        return this;
    }

    public ISecretsVaultResourceBuilder WithSecret(
        string name,
        string value,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        declared.Definition = declared.Definition with
        {
            Secrets = declared.Definition.Secrets
                .Append(new SecretsVaultSecret(name, value, version))
                .ToArray()
        };
        return this;
    }

    public SecretReference Secret(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new SecretReference(
            ResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }

    public ISecretsVaultResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public ISecretsVaultResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public ISecretsVaultResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public ISecretsVaultResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public ISecretsVaultResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public ISecretsVaultResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public ISecretsVaultResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public ISecretsVaultResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public ISecretsVaultResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public ISecretsVaultResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public ISecretsVaultResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    IResourceBuilder IResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    IResourceBuilder IResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    IResourceBuilder IResourceBuilder.WithParent(IResourceBuilder resource) =>
        WithParent(resource);

    IResourceBuilder IResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    IResourceBuilder IResourceBuilder.DependsOn(IResourceBuilder resource) =>
        DependsOn(resource);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources) =>
        DependsOn(resources);

    IResourceBuilder IResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}

public interface IHostConfigurationSourceResourceBuilder : IResourceBuilder
{
    IHostConfigurationSourceResourceBuilder WithEntries(IReadOnlyList<string> entries);

    IHostConfigurationSourceResourceBuilder WithEntry(string name);

    ConfigurationEntryReference Entry(
        string name,
        string? version = null);

    new IHostConfigurationSourceResourceBuilder DependsOn(string resourceId);

    new IHostConfigurationSourceResourceBuilder DependsOn(IResourceBuilder resource);

    new IHostConfigurationSourceResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IHostConfigurationSourceResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IHostConfigurationSourceResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IHostConfigurationSourceResourceBuilder WithParent(string? parentResourceId);

    new IHostConfigurationSourceResourceBuilder WithParent(IResourceBuilder resource);

    new IHostConfigurationSourceResourceBuilder WithReference(string resourceId);

    new IHostConfigurationSourceResourceBuilder WithReference(IResourceBuilder resource);

    new IHostConfigurationSourceResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IHostConfigurationSourceResourceBuilder Persist(bool overwrite = false);
}

internal sealed class HostConfigurationSourceResourceBuilder(
    IResourceBuilder inner,
    DeclaredHostConfigurationSource declared) : IHostConfigurationSourceResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IHostConfigurationSourceResourceBuilder WithEntries(IReadOnlyList<string> entries)
    {
        declared.Definition = declared.Definition with { Entries = entries };
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithEntry(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        declared.Definition = declared.Definition with
        {
            Entries = declared.Definition.Entries
                .Append(name)
                .ToArray()
        };
        return this;
    }

    public ConfigurationEntryReference Entry(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfigurationEntryReference(
            ResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }

    public IHostConfigurationSourceResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IHostConfigurationSourceResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    IResourceBuilder IResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    IResourceBuilder IResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    IResourceBuilder IResourceBuilder.WithParent(IResourceBuilder resource) =>
        WithParent(resource);

    IResourceBuilder IResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    IResourceBuilder IResourceBuilder.DependsOn(IResourceBuilder resource) =>
        DependsOn(resource);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources) =>
        DependsOn(resources);

    IResourceBuilder IResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}

public interface IConfigurationStoreResourceBuilder : IResourceBuilder
{
    IConfigurationStoreResourceBuilder WithEntries(IReadOnlyList<ConfigurationEntry> entries);

    IConfigurationStoreResourceBuilder WithEntry(
        string name,
        string value,
        bool isSecret = false);

    ConfigurationEntryReference Entry(
        string name,
        string? version = null);

    new IConfigurationStoreResourceBuilder DependsOn(string resourceId);

    new IConfigurationStoreResourceBuilder DependsOn(IResourceBuilder resource);

    new IConfigurationStoreResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IConfigurationStoreResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources);

    new IConfigurationStoreResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IConfigurationStoreResourceBuilder WithParent(string? parentResourceId);

    new IConfigurationStoreResourceBuilder WithParent(IResourceBuilder resource);

    new IConfigurationStoreResourceBuilder WithReference(string resourceId);

    new IConfigurationStoreResourceBuilder WithReference(IResourceBuilder resource);

    new IConfigurationStoreResourceBuilder WithReferences(IEnumerable<string> resourceIds);

    new IConfigurationStoreResourceBuilder Persist(bool overwrite = false);
}

internal sealed class ConfigurationStoreResourceBuilder(
    IResourceBuilder inner,
    DeclaredConfigurationStore declared) : IConfigurationStoreResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public ResourceIdentityReference Identity => inner.Identity;

    public IConfigurationStoreResourceBuilder WithEntries(IReadOnlyList<ConfigurationEntry> entries)
    {
        declared.Definition = declared.Definition with { Entries = entries };
        return this;
    }

    public IConfigurationStoreResourceBuilder WithEntry(
        string name,
        string value,
        bool isSecret = false)
    {
        declared.Definition = declared.Definition with
        {
            Entries = declared.Definition.Entries
                .Append(new ConfigurationEntry(name, value, isSecret))
                .ToArray()
        };
        return this;
    }

    public ConfigurationEntryReference Entry(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new ConfigurationEntryReference(
            ResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }

    public IConfigurationStoreResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IConfigurationStoreResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IConfigurationStoreResourceBuilder WithParent(IResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IConfigurationStoreResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IConfigurationStoreResourceBuilder DependsOn(IResourceBuilder resource)
    {
        inner.DependsOn(resource);
        return this;
    }

    public IConfigurationStoreResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IConfigurationStoreResourceBuilder DependsOn(IEnumerable<IResourceBuilder> resources)
    {
        inner.DependsOn(resources);
        return this;
    }

    public IConfigurationStoreResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IConfigurationStoreResourceBuilder WithReference(IResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IConfigurationStoreResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IConfigurationStoreResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    IResourceBuilder IResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    IResourceBuilder IResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    IResourceBuilder IResourceBuilder.WithParent(IResourceBuilder resource) =>
        WithParent(resource);

    IResourceBuilder IResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    IResourceBuilder IResourceBuilder.DependsOn(IResourceBuilder resource) =>
        DependsOn(resource);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    IResourceBuilder IResourceBuilder.DependsOn(IEnumerable<IResourceBuilder> resources) =>
        DependsOn(resources);

    IResourceBuilder IResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    IResourceBuilder IResourceBuilder.WithReference(IResourceBuilder resource) =>
        WithReference(resource);

    IResourceBuilder IResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    IResourceBuilder IResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
