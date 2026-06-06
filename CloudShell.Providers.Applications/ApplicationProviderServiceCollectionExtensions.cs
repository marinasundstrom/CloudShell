using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Applications;

public static class ApplicationProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddApplicationProvider(
        this ICloudShellBuilder builder,
        Action<ApplicationProviderOptions>? configure = null)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension<ApplicationProviderExtension>();
    }

    public static IControlPlaneBuilder AddApplicationProvider(
        this IControlPlaneBuilder builder,
        Action<ApplicationProviderOptions>? configure = null)
    {
        AddApplicationProviderCore(builder, configure);
        return builder.AddExtension<ApplicationProviderExtension>();
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
        ApplicationLifetime lifetime = ApplicationLifetime.Detached)
    {
        var definition = new ApplicationResourceDefinition(
            id,
            name,
            executablePath,
            arguments,
            workingDirectory,
            endpoint,
            environmentVariables,
            lifetime);
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

    new IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId);

    new IExecutableApplicationResourceBuilder WithReference(string resourceId);

    new IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource);

    new IExecutableApplicationResourceBuilder WithReferences(IEnumerable<string> resourceIds);

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

    public IExecutableApplicationResourceBuilder WithResourceGroup(string? resourceGroupId)
    {
        inner.WithResourceGroup(resourceGroupId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReference(string resourceId)
    {
        inner.WithReference(resourceId);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReference(ICloudShellResourceBuilder resource)
    {
        inner.WithReference(resource);
        return this;
    }

    public IExecutableApplicationResourceBuilder WithReferences(IEnumerable<string> resourceIds)
    {
        inner.WithReferences(resourceIds);
        return this;
    }

    public IExecutableApplicationResourceBuilder Persist(bool overwrite = false)
    {
        inner.Persist(overwrite);
        return this;
    }

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithResourceGroup(string? resourceGroupId) =>
        WithResourceGroup(resourceGroupId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(string resourceId) =>
        WithReference(resourceId);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReference(ICloudShellResourceBuilder resource) =>
        WithReference(resource);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.WithReferences(IEnumerable<string> resourceIds) =>
        WithReferences(resourceIds);

    ICloudShellResourceBuilder ICloudShellResourceBuilder.Persist(bool overwrite) =>
        Persist(overwrite);
}
