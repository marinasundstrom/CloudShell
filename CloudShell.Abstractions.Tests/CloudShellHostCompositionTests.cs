using System.Reflection;
using System.Runtime.CompilerServices;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.Hosting;
using CloudShell.Hosting.ResourceManager;
using CloudShell.ResourceModel;
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
    public void AddCloudShellControlPlaneApplication_RegistersControlPlaneDefaultsWithoutUi()
    {
        var builder = CreateBuilder();

        builder.AddCloudShellControlPlaneApplication(options =>
        {
            options.IncludeDefaultEnvironmentResources = false;
        });

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IControlPlane));
        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IResourceManagerStore));
        Assert.Contains(
            builder.Services,
            descriptor =>
                descriptor.ServiceType == typeof(IResourceTypeProvider) &&
                descriptor.ImplementationType == typeof(ContainerApplicationResourceTypeProvider));

        var registry = GetExtensionRegistry(builder.Services);
        Assert.DoesNotContain(registry.Extensions, extension => extension.Id == "cloudshell.core");
    }

    [Fact]
    public void AddCloudShellUi_CallbackRegistersUiExtensions()
    {
        var builder = CreateBuilder();

        builder.AddCloudShellUi(ui =>
        {
            ui.AddExtension<ResourceManagerExtension>();
        });

        var registry = GetExtensionRegistry(builder.Services);

        Assert.Contains(registry.Extensions, extension => extension.Id == "cloudshell.core");
        Assert.Contains(registry.Extensions, extension => extension.Id == "cloudshell.resource-manager");
    }

    [Fact]
    public void HostRegistrationMethods_KeepControlPlaneAndUiBoundariesExplicit()
    {
        Assert.DoesNotContain(
            GetPublicStaticMethodNames(typeof(CloudShellHostApplicationBuilderExtensions)),
            name => name == "AddCloudShell");
        Assert.DoesNotContain(
            GetPublicStaticMethodNames(typeof(CloudShellHostApplicationExtensions)),
            name => name is "UseCloudShellAsync" or "MapCloudShell");

        Assert.Contains(
            GetPublicStaticMethodNames(typeof(CloudShellCombinedHostApplicationBuilderExtensions)),
            name => name == "AddCloudShellControlPlaneApplication");
        Assert.DoesNotContain(
            GetPublicStaticMethodNames(typeof(CloudShellCombinedHostApplicationBuilderExtensions)),
            name => name == "AddCloudShell");
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
