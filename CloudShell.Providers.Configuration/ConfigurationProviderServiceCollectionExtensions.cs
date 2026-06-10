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
        return builder.AddExtension(new ConfigurationProviderExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddConfigurationProvider(
        this IControlPlaneBuilder builder,
        Action<ConfigurationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddConfigurationProviderCore(builder, configure);
        return builder.AddExtension(new ConfigurationProviderExtension(), activationPolicy);
    }

    private static void AddConfigurationProviderCore(
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
        string name,
        IReadOnlyList<ConfigurationEntry>? entries = null,
        string? accessToken = null)
    {
        var definition = new ConfigurationStoreDefinition(
            id,
            name,
            entries,
            accessToken);
        var declared = new DeclaredConfigurationStore(definition);
        var options = builder.Services.GetOrAddConfigurationProviderOptions();

        options.DeclaredStores.Add(declared);

        var resource = builder.Declare(
            "configuration",
            id,
            onChanged: declaration =>
            {
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ConfigurationStoreResourceBuilder(resource, declared);
    }

    public static IHostConfigurationSourceResourceBuilder AddHostConfigurationSource(
        this IResourceDeclarationBuilder builder,
        string id,
        string name,
        IReadOnlyList<string>? entries = null)
    {
        var definition = new HostConfigurationSourceDefinition(
            id,
            name,
            entries);
        var declared = new DeclaredHostConfigurationSource(definition);
        var options = builder.Services.GetOrAddConfigurationProviderOptions();

        options.DeclaredHostConfigurationSources.Add(declared);

        var resource = builder.Declare(
            HostConfigurationSourceProvider.ProviderId,
            id,
            resourceClass: ResourceClass.Configuration);

        return new HostConfigurationSourceResourceBuilder(resource, declared);
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

    IConfigurationStoreResourceBuilder WithAccessToken(string? accessToken);

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

    public IConfigurationStoreResourceBuilder WithAccessToken(string? accessToken)
    {
        declared.Definition = declared.Definition with { AccessToken = accessToken };
        return this;
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
