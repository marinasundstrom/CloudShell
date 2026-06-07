using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Applications;

public static class ApplicationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddApplicationProvider(
        this ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension(new ApplicationProviderExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddApplicationProvider(
        this IControlPlaneBuilder builder,
        Action<ApplicationProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension(new ApplicationProviderExtension(), activationPolicy);
    }

    private static void AddApplicationProviderCore(
        ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure)
    {
        var options = builder.Services.GetOrAddApplicationProviderOptions();
        configure?.Invoke(options);
    }

    public static IExecutableApplicationResourceBuilder AddExecutable(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name) =>
        builder.AddExecutableApplication(id, name, executablePath: string.Empty);

    public static IExecutableApplicationResourceBuilder AddExecutableApplication(
        this ICloudShellResourceDeclarationBuilder builder,
        string id,
        string name,
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null,
        string? endpoint = null,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        ApplicationLifetime lifetime = ApplicationLifetime.Detached,
        bool useServiceDiscovery = false)
    {
        var definition = new ApplicationResourceDefinition(
            id,
            name,
            executablePath,
            arguments,
            workingDirectory,
            endpoint,
            environmentVariables,
            lifetime,
            useServiceDiscovery: useServiceDiscovery);
        var declared = new DeclaredApplicationResource(definition);

        builder.Services
            .GetOrAddApplicationProviderOptions()
            .DeclaredApplications
            .Add(declared);

        var resource = builder.Declare(
            "applications",
            id,
            onChanged: declaration =>
            {
                declared.Definition = declared.Definition with
                {
                    DependsOn = declaration.DependsOn
                };
                declared.Persist = declaration.Persistence == ResourceDeclarationPersistence.Persisted;
                declared.OverwritePersistedState = declaration.OverwritePersistedState;
            });

        return new ExecutableApplicationResourceBuilder(resource, declared);
    }

    private static ApplicationProviderOptions GetOrAddApplicationProviderOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(ApplicationProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ApplicationProviderOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new ApplicationProviderOptions();
        services.AddSingleton(options);
        return options;
    }
}

public interface IExecutableApplicationResourceBuilder : ICloudShellResourceBuilder
{
    IExecutableApplicationResourceBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null);

    IExecutableApplicationResourceBuilder WithEndpoint(string? endpoint);

    IExecutableApplicationResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables);

    IExecutableApplicationResourceBuilder WithEnvironment(
        string name,
        string value);

    IExecutableApplicationResourceBuilder WithLifetime(ApplicationLifetime lifetime);

    IExecutableApplicationResourceBuilder WithServiceDiscovery(bool enabled = true);

    IExecutableApplicationResourceBuilder WithContainerImage(string? image);

    IExecutableApplicationResourceBuilder WithDockerBuild(
        string? buildContext,
        string? dockerfile = null);

    IExecutableApplicationResourceBuilder WithReplicas(int replicas);

    IExecutableApplicationResourceBuilder WaitFor(ICloudShellResourceBuilder resource);

    IExecutableApplicationResourceBuilder WaitFor(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder DependsOn(string resourceId);

    new IExecutableApplicationResourceBuilder DependsOn(ICloudShellResourceBuilder resource);

    new IExecutableApplicationResourceBuilder DependsOn(IEnumerable<string> resourceIds);

    new IExecutableApplicationResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IExecutableApplicationResourceBuilder WithParent(string? parentResourceId);

    new IExecutableApplicationResourceBuilder WithParent(ICloudShellResourceBuilder resource);

    new IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    IExecutableApplicationResourceBuilder WithReferences(IEnumerable<ICloudShellResourceBuilder> resources);

    new IExecutableApplicationResourceBuilder Persist(bool overwrite = false);
}

internal sealed class ExecutableApplicationResourceBuilder(
    ICloudShellResourceBuilder inner,
    DeclaredApplicationResource declared) : IExecutableApplicationResourceBuilder
{
    public ICloudShellBuilder CloudShellBuilder => inner.CloudShellBuilder;

    public string ResourceId => inner.ResourceId;

    public IExecutableApplicationResourceBuilder WithCommand(
        string executablePath,
        string? arguments = null,
        string? workingDirectory = null)
    {
        declared.Definition = declared.Definition with
        {
            ExecutablePath = executablePath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithEndpoint(string? endpoint)
    {
        declared.Definition = declared.Definition with { Endpoint = endpoint };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithEnvironment(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = environmentVariables
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithEnvironment(
        string name,
        string value)
    {
        declared.Definition = declared.Definition with
        {
            EnvironmentVariables = declared.Definition.EnvironmentVariables
                .Append(new EnvironmentVariableAssignment(name, value))
                .ToArray()
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithLifetime(ApplicationLifetime lifetime)
    {
        declared.Definition = declared.Definition with { Lifetime = lifetime };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithServiceDiscovery(bool enabled = true)
    {
        declared.Definition = declared.Definition with
        {
            UseServiceDiscovery = enabled
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithContainerImage(string? image)
    {
        declared.Definition = declared.Definition with
        {
            ContainerImage = image,
            ContainerBuildContext = null,
            ContainerDockerfile = null
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithDockerBuild(
        string? buildContext,
        string? dockerfile = null)
    {
        declared.Definition = declared.Definition with
        {
            ContainerBuildContext = buildContext,
            ContainerDockerfile = dockerfile,
            ContainerImage = null
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReplicas(int replicas)
    {
        declared.Definition = declared.Definition with
        {
            Replicas = Math.Max(1, replicas)
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithParent(string? parentResourceId)
    {
        inner.WithParent(parentResourceId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithParent(ICloudShellResourceBuilder resource)
    {
        inner.WithParent(resource);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Append(resource.ResourceId)
                .ToArray()
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReferences(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        var resourceIds = resources
            .Select(resource =>
            {
                ArgumentNullException.ThrowIfNull(resource);
                return resource.ResourceId;
            })
            .ToArray();

        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Concat(resourceIds)
                .ToArray()
        };
        return this;
    }

    public IExecutableApplicationResourceBuilder WaitFor(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        return DependsOn(resource);
    }

    public IExecutableApplicationResourceBuilder WaitFor(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        return DependsOn(resources);
    }

    public IExecutableApplicationResourceBuilder DependsOn(string resourceId)
    {
        inner.DependsOn(resourceId);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(ICloudShellResourceBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        inner.DependsOn(resource);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(IEnumerable<string> resourceIds)
    {
        inner.DependsOn(resourceIds);
        return this;
    }

    public IExecutableApplicationResourceBuilder DependsOn(IEnumerable<ICloudShellResourceBuilder> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        inner.DependsOn(resources.Select(resource =>
        {
            ArgumentNullException.ThrowIfNull(resource);
            return resource.ResourceId;
        }));
        return this;
    }

    public IExecutableApplicationResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(string? parentResourceId) =>
        WithParent(parentResourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithParent(ICloudShellResourceBuilder resource) =>
        WithParent(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(string resourceId) =>
        DependsOn(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(ICloudShellResourceBuilder resource) =>
        DependsOn(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<string> resourceIds) =>
        DependsOn(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.DependsOn(IEnumerable<ICloudShellResourceBuilder> resources) =>
        DependsOn(resources);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        AddReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        AddReferences(resourceIds);

    private IExecutableApplicationResourceBuilder AddReference(string resourceId)
    {
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Append(resourceId)
                .ToArray()
        };
        return this;
    }

    private IExecutableApplicationResourceBuilder AddReferences(IEnumerable<string> resourceIds)
    {
        ArgumentNullException.ThrowIfNull(resourceIds);
        declared.Definition = declared.Definition with
        {
            References = declared.Definition.References
                .Concat(resourceIds)
                .ToArray()
        };
        return this;
    }

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
