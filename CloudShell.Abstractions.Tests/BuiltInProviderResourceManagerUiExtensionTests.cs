using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class BuiltInProviderResourceManagerUiExtensionTests
{
    [Theory]
    [InlineData("application.dotnet-app")]
    [InlineData("application.javascript-app")]
    [InlineData("application.java-app")]
    [InlineData("application.go-app")]
    [InlineData("application.container-app")]
    public void ApplicationResourcesWithEnvironmentVariablesCapability_DoNotContributeReadOnlyEnvironmentTab(
        string resourceTypeId)
    {
        var resourceType = CreateCatalog()
            .ResourceTypes
            .Single(type => type.Id == resourceTypeId);

        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => tab.Id == ResourcePredefinedViewIds.Environment);
    }

    [Theory]
    [InlineData("application.executable")]
    [InlineData("application.sql-server")]
    [InlineData("application.rabbitmq")]
    public void ResourcesWithoutGenericEnvironmentEditor_KeepReadOnlyEnvironmentTab(
        string resourceTypeId)
    {
        var resourceType = CreateCatalog()
            .ResourceTypes
            .Single(type => type.Id == resourceTypeId);

        Assert.Contains(
            resourceType.ResourceTabs,
            tab => tab.Id == ResourcePredefinedViewIds.Environment);
    }

    [Fact]
    public void ContainerApplicationStorage_ReplacesPredefinedStorageView()
    {
        var resourceType = CreateCatalog()
            .ResourceTypes
            .Single(type => type.Id == "application.container-app");

        var storageTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id.Identifier == ResourcePredefinedViewIds.Storage.Identifier);

        Assert.Equal(ResourcePredefinedViewIds.Storage, storageTab.Id);
        Assert.Equal(ResourceTabGroupTitles.Storage, storageTab.GroupTitle);
    }

    private static ShellCatalog CreateCatalog()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellUi()
            .AddExtension<CoreShellExtension>()
            .AddExtension(new ResourceManagerExtension(includeSettings: false))
            .AddBuiltInProviderResourceManagerUi();

        var registry = Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);
        registry.Validate();

        return new ShellCatalog(registry, new InMemoryCloudShellExtensionActivationStore());
    }
}
