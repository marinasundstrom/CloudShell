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
    public void ContainerApplicationStorage_IsAnOperationsPane()
    {
        var resourceType = CreateCatalog()
            .ResourceTypes
            .Single(type => type.Id == "application.container-app");

        var storageTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id.Identifier == ResourcePredefinedViewIds.Storage.Identifier);

        Assert.Equal(ResourcePredefinedViewIds.Storage, storageTab.Id);
        Assert.True(storageTab.IsOperational);
        Assert.True(storageTab.OwnsLayout);
        Assert.Equal(ResourceTabGroupTitles.Storage, storageTab.GroupTitle);
    }

    [Theory]
    [InlineData("application.rabbitmq", "broker")]
    [InlineData("application.rabbitmq", "broker-status")]
    [InlineData("application.rabbitmq", "topology")]
    [InlineData("application.sql-server", "databases")]
    public void ProviderInspectionViewsAreOperationsPanes(string typeId, string paneId)
    {
        var type = CreateCatalog().ResourceTypes.Single(type => type.Id == typeId);
        var pane = Assert.Single(type.ResourceTabs, tab => tab.ViewId == paneId);
        Assert.True(pane.IsOperational);
        Assert.False(pane.ShowsApplyButton);
        Assert.NotNull(pane.Icon);
        Assert.Contains(type.ResourceTabs, tab => tab.Id == ResourcePredefinedViewIds.Configuration && !tab.IsOperational);
    }

    [Theory]
    [InlineData("cloudshell.storage", "storage-status", "configuration")]
    [InlineData("cloudshell.storage", "volumes", "configuration")]
    [InlineData("cloudshell.volume", "volume-status", "configuration")]
    [InlineData("cloudshell.dnsZone", "name-mappings", null)]
    [InlineData("application.sql-server", "storage", "configuration")]
    [InlineData("application.rabbitmq", "storage", "configuration")]
    [InlineData("iot.device-registry", "enrolled-devices", "enrollment-profiles")]
    [InlineData("secrets.vault", "secrets", null)]
    [InlineData("secrets.vault", "certificates", null)]
    [InlineData("configuration.store", "values", null)]
    [InlineData("event.broker", "streams", null)]
    [InlineData("application.container-app", "deployment", "configuration")]
    [InlineData("application.container-app", "revisions", "configuration")]
    [InlineData("application.container-app", "scale-replicas", "configuration")]
    [InlineData("application.container-app", "monitoring", "configuration")]
    public void OperationalProviderViewsLeaveConfigurationSettingsAvailable(string typeId, string paneId, string? configurationId)
    {
        var type = CreateCatalog().ResourceTypes.Single(type => type.Id == typeId);
        var pane = Assert.Single(type.ResourceTabs, tab => tab.ViewId == paneId);
        Assert.True(pane.IsOperational);
        Assert.True(pane.OwnsLayout);
        Assert.False(pane.ShowsApplyButton);
        Assert.NotNull(pane.Icon);
        if (configurationId is not null)
            Assert.Contains(type.ResourceTabs, tab => tab.ViewId == configurationId && !tab.IsOperational);
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
