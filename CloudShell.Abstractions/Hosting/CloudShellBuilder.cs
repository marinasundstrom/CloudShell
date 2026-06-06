using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Hosting;

public sealed class CloudShellBuilder(
    IServiceCollection services,
    CloudShellExtensionRegistry extensionRegistry) : ICloudShellBuilder
{
    public IServiceCollection Services { get; } = services;

    internal CloudShellExtensionRegistry ExtensionRegistry { get; } = extensionRegistry;
}

public static class CloudShellBuilderExtensions
{
    public static ICloudShellBuilder AddCloudShell(this IServiceCollection services)
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

        return new CloudShellBuilder(services, registry);
    }

    public static ICloudShellBuilder AddExtension<TExtension>(this ICloudShellBuilder builder)
        where TExtension : class, ICloudShellExtension, new() =>
        builder.AddExtension(new TExtension());

    public static ICloudShellBuilder AddExtension(
        this ICloudShellBuilder builder,
        ICloudShellExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        if (builder is not CloudShellBuilder cloudShellBuilder)
        {
            throw new InvalidOperationException("Extensions must be registered on the CloudShell builder returned by AddCloudShell().");
        }

        var extensionBuilder = new CloudShellExtensionBuilder(builder.Services, extension.Manifest);
        extension.Configure(extensionBuilder);

        cloudShellBuilder.ExtensionRegistry.Add(extensionBuilder.Build());
        builder.Services.AddSingleton(typeof(ICloudShellExtension), extension);

        return builder;
    }
}
