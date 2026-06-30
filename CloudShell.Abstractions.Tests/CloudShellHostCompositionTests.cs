using System.Reflection;
using System.Runtime.CompilerServices;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class CloudShellHostCompositionTests
{
    [Fact]
    public void AddCloudShellUi_DoesNotRegisterControlPlaneServices()
    {
        var builder = CreateBuilder();

        builder.AddCloudShellUi();

        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IControlPlane));
        Assert.DoesNotContain(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IResourceManagerStore));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry));

        var registry = GetExtensionRegistry(builder.Services);
        Assert.Contains(registry.Extensions, extension => extension.Id == "cloudshell.core");
    }

    [Fact]
    public void AddCloudShell_ComposesControlPlaneAndUiServices()
    {
        var builder = CreateBuilder();

        builder.AddCloudShell();

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IControlPlane));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IResourceManagerStore));

        var registry = GetExtensionRegistry(builder.Services);
        Assert.Contains(registry.Extensions, extension => extension.Id == "cloudshell.core");
    }

    [Fact]
    public void CombinedHostMethodNames_AreOwnedByAppHost()
    {
        Assert.DoesNotContain(
            GetPublicStaticMethodNames(typeof(CloudShellHostApplicationBuilderExtensions)),
            name => name == "AddCloudShell");
        Assert.DoesNotContain(
            GetPublicStaticMethodNames(typeof(CloudShellHostApplicationExtensions)),
            name => name is "UseCloudShellAsync" or "MapCloudShell");

        Assert.Contains(
            GetPublicStaticMethodNames(typeof(CloudShellCombinedHostApplicationBuilderExtensions)),
            name => name == "AddCloudShell");
        Assert.Contains(
            GetPublicStaticMethodNames(typeof(CloudShellCombinedHostApplicationExtensions)),
            name => name == "UseCloudShellAsync");
        Assert.Contains(
            GetPublicStaticMethodNames(typeof(CloudShellCombinedHostApplicationExtensions)),
            name => name == "MapCloudShell");
    }

    private static WebApplicationBuilder CreateBuilder() =>
        WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(CloudShellHostCompositionTests).Assembly.GetName().Name,
            EnvironmentName = "Development"
        });

    private static CloudShellExtensionRegistry GetExtensionRegistry(IServiceCollection services) =>
        Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);

    private static string[] GetPublicStaticMethodNames(Type type) =>
        type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.GetCustomAttribute<ExtensionAttribute>() is not null)
            .Select(method => method.Name)
            .ToArray();
}
