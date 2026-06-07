using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.DockerCompose;

public static class DockerComposeOrchestratorServiceCollectionExtensions
{
    public static ICloudShellBuilder AddDockerComposeOrchestrator(
        this ICloudShellBuilder builder,
        Action<DockerComposeOrchestratorOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.UserManaged)
    {
        AddDockerComposeOrchestratorCore(builder, configure);
        return builder.AddExtension(new DockerComposeOrchestratorExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddDockerComposeOrchestrator(
        this IControlPlaneBuilder builder,
        Action<DockerComposeOrchestratorOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.UserManaged)
    {
        AddDockerComposeOrchestratorCore(builder, configure);
        return builder.AddExtension(new DockerComposeOrchestratorExtension(), activationPolicy);
    }

    private static void AddDockerComposeOrchestratorCore(
        ICloudShellBuilder builder,
        Action<DockerComposeOrchestratorOptions>? configure)
    {
        var options = builder.Services.GetOrAddDockerComposeOrchestratorOptions();
        configure?.Invoke(options);
    }

    private static DockerComposeOrchestratorOptions GetOrAddDockerComposeOrchestratorOptions(
        this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(DockerComposeOrchestratorOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<DockerComposeOrchestratorOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new DockerComposeOrchestratorOptions();
        services.AddSingleton(options);
        return options;
    }
}
