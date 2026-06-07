using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
    public static IControlPlaneBuilder AddControlPlane(this IServiceCollection services) =>
        services.AddCloudShellControlPlane();

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

        services.TryAddSingleton<ICloudShellExtensionActivationStore, InMemoryCloudShellExtensionActivationStore>();

        return new ControlPlaneBuilder(services, registry);
    }

    public static IControlPlaneBuilder AddCloudShell(this IServiceCollection services) =>
        services.AddCloudShellControlPlane();

    public static ICloudShellBuilder AddExtension<TExtension>(this ICloudShellBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.Enabled);

    public static IControlPlaneBuilder AddExtension<TExtension>(this IControlPlaneBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.Enabled);

    public static ICloudShellBuilder AddSupportedExtension<TExtension>(this ICloudShellBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.UserManaged);

    public static IControlPlaneBuilder AddSupportedExtension<TExtension>(this IControlPlaneBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.UserManaged);

    public static ICloudShellBuilder DisableExtension<TExtension>(this ICloudShellBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.Disabled);

    public static IControlPlaneBuilder DisableExtension<TExtension>(this IControlPlaneBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension(), CloudShellExtensionActivationPolicy.Disabled);

    public static ICloudShellBuilder AddExtension(
        this ICloudShellBuilder builder,
        ICloudShellExtension extension)
    {
        AddExtensionCore(builder, extension, CloudShellExtensionActivationPolicy.Enabled);
        return builder;
    }

    public static IControlPlaneBuilder AddExtension(
        this IControlPlaneBuilder builder,
        ICloudShellExtension extension)
    {
        AddExtensionCore(builder, extension, CloudShellExtensionActivationPolicy.Enabled);
        return builder;
    }

    public static ICloudShellBuilder AddExtension(
        this ICloudShellBuilder builder,
        ICloudShellExtension extension,
        CloudShellExtensionActivationPolicy activationPolicy)
    {
        AddExtensionCore(builder, extension, activationPolicy);
        return builder;
    }

    public static IControlPlaneBuilder AddExtension(
        this IControlPlaneBuilder builder,
        ICloudShellExtension extension,
        CloudShellExtensionActivationPolicy activationPolicy)
    {
        AddExtensionCore(builder, extension, activationPolicy);
        return builder;
    }

    private static void AddExtensionCore(
        ICloudShellBuilder builder,
        ICloudShellExtension extension,
        CloudShellExtensionActivationPolicy activationPolicy)
    {
        ArgumentNullException.ThrowIfNull(extension);

        if (builder is not ControlPlaneBuilder controlPlaneBuilder)
        {
            throw new InvalidOperationException("Extensions must be registered on the Control Plane builder returned by AddCloudShellControlPlane().");
        }

        var extensionBuilder = new CloudShellExtensionBuilder(
            builder.Services,
            extension.Manifest,
            activationPolicy);
        extension.Configure(extensionBuilder);

        controlPlaneBuilder.ExtensionRegistry.Add(extensionBuilder.Build());
        builder.Services.AddSingleton(typeof(ICloudShellExtension), extension);
    }
}
