using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Hosting;

public sealed class ControlPlaneBuilder(
    IServiceCollection services,
    CloudShellExtensionRegistry extensionRegistry) : IControlPlaneBuilder
{
    public IServiceCollection Services { get; } = services;

    internal CloudShellExtensionRegistry ExtensionRegistry { get; } = extensionRegistry;
}

public static class CloudShellBuilderExtensions
{
    public static IControlPlaneBuilder AddCloudShellControlPlane(this IServiceCollection services)
    {
        var registry = services
            .Where(descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<CloudShellExtensionRegistry>()
            .SingleOrDefault();

        if (registry is null)
        {
            registry = new CloudShellExtensionRegistry();
            services.AddSingleton(registry);
        }

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ResourceDeclarationStore)))
        {
            services.AddSingleton(new ResourceDeclarationStore());
        }

        return new ControlPlaneBuilder(services, registry);
    }

    public static IControlPlaneBuilder AddCloudShell(this IServiceCollection services) =>
        services.AddCloudShellControlPlane();

    public static ICloudShellBuilder AddExtension<TExtension>(this ICloudShellBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension());

    public static IControlPlaneBuilder AddExtension<TExtension>(this IControlPlaneBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension());

    public static ICloudShellBuilder AddExtension(
        this ICloudShellBuilder builder,
        ICloudShellExtension extension)
    {
        AddExtensionCore(builder, extension);
        return builder;
    }

    public static IControlPlaneBuilder AddExtension(
        this IControlPlaneBuilder builder,
        ICloudShellExtension extension)
    {
        AddExtensionCore(builder, extension);
        return builder;
    }

    private static void AddExtensionCore(
        ICloudShellBuilder builder,
        ICloudShellExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        if (builder is not ControlPlaneBuilder controlPlaneBuilder)
        {
            throw new InvalidOperationException("Extensions must be registered on the Control Plane builder returned by AddCloudShellControlPlane().");
        }

        var extensionBuilder = new CloudShellExtensionBuilder(builder.Services, extension.Manifest);
        extension.Configure(extensionBuilder);

        controlPlaneBuilder.ExtensionRegistry.Add(extensionBuilder.Build());
        builder.Services.AddSingleton(typeof(ICloudShellExtension), extension);
    }
}
