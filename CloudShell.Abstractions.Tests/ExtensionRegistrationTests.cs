using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.Providers.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Tests;

public sealed class ExtensionRegistrationTests
{
    [Fact]
    public void AddExtension_RecordsManifestAndNormalizesViewRoute()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var view = Assert.Single(extension.Views);

        Assert.Equal("sample.provider", extension.Id);
        Assert.Equal("1.2.3", extension.Version);
        Assert.Equal(typeof(SamplePage).FullName, view.Id);
        Assert.Equal("/sample", view.Route);
    }

    [Fact]
    public void ShellViewKeys_MapsComponentTypesToDefaultViewIds()
    {
        Assert.Equal(typeof(SamplePage).FullName, ShellViewKeys.For<SamplePage>());
        Assert.Equal(typeof(SamplePage).FullName, ShellViewKeys.For(typeof(SamplePage)));
        Assert.Equal(
            $"sample.provider.{typeof(SamplePage).FullName}",
            ShellViewKeys.For<SamplePage>("sample.provider"));
        Assert.Equal(
            "sample.provider.workspace",
            ShellViewKeys.For("sample.provider", "workspace"));
    }

    [Fact]
    public void AddExtension_RegistersResourceProvidersInTheServiceCollection()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SampleResourceProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IResourceProvider) &&
            descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void AddExtension_RegistersLogProvidersInTheServiceCollection()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<LogProviderExtension>();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SampleLogProvider));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILogProvider) &&
            descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public void AddExtension_RegistersLogSourceContributorsInTheServiceCollection()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<LogSourceContributorExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions, extension => extension.Id == "sample.log-sources");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SampleLogSourceContributor));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILogSourceContributor) &&
            descriptor.ImplementationFactory is not null);
        Assert.Contains(extension.LogSourceContributorTypes, type => type == typeof(SampleLogSourceContributor));
    }

    [Fact]
    public void AddResourceType_RecordsSeparateRegistrationAndUpdateComponents()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceProcedureExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);

        Assert.Equal("sample.resource", resourceType.Id);
        Assert.Equal(typeof(SampleRegistrationPage), resourceType.RegistrationComponentType);
        Assert.Equal(typeof(SampleUpdatePage), resourceType.UpdateComponentType);
    }

    [Fact]
    public void AddResourceTypeEndpoint_RecordsEndpointDescriptorsForResourceType()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceEndpointDescriptorsExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var descriptor = Assert.Single(resourceType.ResourceEndpointDescriptors);

        Assert.Equal("http", descriptor.Name);
        Assert.Equal(8080, descriptor.TargetPort);
        Assert.Equal("http", descriptor.Protocol);
        Assert.Equal(ResourceExposureScope.Local, descriptor.Exposure);
        Assert.Equal(ResourceEndpointAssignment.ProviderDefault, descriptor.DefaultAssignment);
        Assert.True(descriptor.SupportsPortRemapping);
    }

    [Fact]
    public void AddResourceTab_RecordsTabsForResourceType()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceTabsExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var tabs = resourceType.ResourceTabs;

        Assert.Collection(
            tabs,
            tab =>
            {
                Assert.Equal(new ResourceViewId(ResourceTabGroupIds.General, "overview"), tab.Id);
                Assert.Equal(typeof(SampleOverviewPage), tab.ComponentType);
                Assert.False(tab.ShowsApplyButton);
                Assert.Equal("overview", tab.Icon);
            },
            tab =>
            {
                Assert.Equal(new ResourceViewId(ResourceTabGroupIds.General, "configuration"), tab.Id);
                Assert.Equal(typeof(SampleUpdatePage), tab.ComponentType);
                Assert.True(tab.ShowsApplyButton);
                Assert.Equal("configuration", tab.Icon);
            });
    }

    [Fact]
    public void ResourceViewId_ParseRejectsFlatViewIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => ResourceViewId.Parse("overview"));

        Assert.Contains("Expected '<group-id>:<identifier>'", exception.Message);
    }

    [Fact]
    public void AddResourceTab_UsesPredefinedMonitoringViewIcon()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceMonitoringTabExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var tab = Assert.Single(resourceType.ResourceTabs);

        Assert.Equal(ResourcePredefinedViewIds.Monitoring, tab.Id);
        Assert.Equal("Monitoring", tab.Title);
        Assert.Equal(ResourceTabGroupIds.Management, tab.GroupId);
        Assert.Equal("monitoring", tab.Icon);
    }

    [Fact]
    public void AddResourceTab_UsesPredefinedMetricsViewIcon()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceMetricsTabExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var tab = Assert.Single(resourceType.ResourceTabs);

        Assert.Equal(ResourcePredefinedViewIds.Metrics, tab.Id);
        Assert.Equal("Metrics", tab.Title);
        Assert.Equal(ResourceTabGroupIds.Telemetry, tab.GroupId);
        Assert.Equal("metrics", tab.Icon);
    }

    [Fact]
    public void AddResourceTab_UsesPredefinedEnvironmentViewManagementGroup()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ResourceEnvironmentTabExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var tab = Assert.Single(resourceType.ResourceTabs);

        Assert.Equal(ResourcePredefinedViewIds.Environment, tab.Id);
        Assert.Equal("Environment", tab.Title);
        Assert.Equal(ResourceTabGroupIds.Management, tab.GroupId);
        Assert.Equal("environment", tab.Icon);
    }

    [Fact]
    public void ConfigurationProviderExtension_RegistersEntriesUnderGeneralWithoutSettingsTab()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ConfigurationProviderExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var resourceType = Assert.Single(extension.ResourceTypes);

        Assert.Equal("configuration.store", resourceType.Id);
        Assert.Equal("configuration-store", resourceType.Icon);
        Assert.Equal(
            typeof(CloudShell.Providers.Configuration.Pages.UpdateConfigurationStore),
            resourceType.UpdateComponentType);
        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => string.Equals(tab.Title, "Settings", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => tab.Id == ResourcePredefinedViewIds.Overview);

        var entries = Assert.Single(resourceType.ResourceTabs, tab => tab.Title == "Entries");
        Assert.Equal(new ResourceViewId(ResourceTabGroupIds.General, "entries"), entries.Id);
        Assert.Equal(ResourceTabGroupTitles.General, entries.GroupTitle);
        Assert.Equal("entries", entries.Icon);
        Assert.True(entries.ShowsApplyButton);

        var overviewSection = Assert.Single(resourceType.ResourcePredefinedViewSections);
        Assert.Equal(ResourcePredefinedViewIds.Overview, overviewSection.ViewId);
        Assert.Equal("configuration.store.summary", overviewSection.Id);
        Assert.Equal("Configuration Store", overviewSection.Title);
        Assert.Equal(typeof(CloudShell.Providers.Configuration.Pages.ConfigurationStoreOverviewSection), overviewSection.ComponentType);
    }

    [Fact]
    public void SecretsProviderExtension_RegistersSecretsUnderGeneralWithoutSettingsTab()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<SecretsProviderExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var resourceType = Assert.Single(extension.ResourceTypes);

        Assert.Equal(SecretsVaultProvider.ResourceType, resourceType.Id);
        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => string.Equals(tab.Title, "Settings", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => tab.Id == ResourcePredefinedViewIds.Overview);

        var secrets = Assert.Single(resourceType.ResourceTabs, tab => tab.Title == "Secrets");
        Assert.Equal(new ResourceViewId(ResourceTabGroupIds.General, "secrets"), secrets.Id);
        Assert.Equal(ResourceTabGroupTitles.General, secrets.GroupTitle);
        Assert.Equal("secrets", secrets.Icon);
        Assert.True(secrets.ShowsApplyButton);

        var overviewSection = Assert.Single(resourceType.ResourcePredefinedViewSections);
        Assert.Equal(ResourcePredefinedViewIds.Overview, overviewSection.ViewId);
        Assert.Equal("secrets.vault.summary", overviewSection.Id);
        Assert.Equal("Secrets Vault", overviewSection.Title);
        Assert.Equal(typeof(CloudShell.Providers.Configuration.Pages.SecretsVaultOverviewSection), overviewSection.ComponentType);
    }

    [Fact]
    public void AddResourcePredefinedViewSection_RecordsSectionsForResourceType()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<PredefinedViewSectionsExtension>();

        var registry = GetRegistry(services);
        var resourceType = Assert.Single(
            Assert.Single(registry.Extensions).ResourceTypes);
        var sections = resourceType.ResourcePredefinedViewSections;

        Assert.Collection(
            sections,
            section =>
            {
                Assert.Equal(ResourcePredefinedViewIds.Endpoints, section.ViewId);
                Assert.Equal("sample.endpoint-policy", section.Id);
                Assert.Equal("Endpoint policy", section.Title);
                Assert.Equal(typeof(SampleOverviewPage), section.ComponentType);
            });
    }

    [Fact]
    public void AddResourcePredefinedViewSection_RejectsUnknownPredefinedView()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services
                .AddCloudShell()
                .AddExtension<UnknownPredefinedViewSectionsExtension>());

        Assert.Contains("missing-predefined-view", exception.Message);
    }

    [Fact]
    public void AddResourcePredefinedViewSection_RejectsViewsThatDoNotSupportSections()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services
                .AddCloudShell()
                .AddExtension<NonExtensiblePredefinedViewSectionsExtension>());

        Assert.Contains(ResourcePredefinedViewIds.Configuration.Value, exception.Message);
    }

    [Fact]
    public void AddCustomView_RecordsMenuItemsAndStartRoute()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<CustomShellViewExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var customView = Assert.Single(extension.CustomViews);

        Assert.Equal("sample.workspace", customView.Id);
        Assert.Equal("/sample/workspace", customView.Route);
        Assert.Equal("/sample/workspace", extension.StartRoute);
        Assert.Collection(
            customView.ViewMenuItems,
            item =>
            {
                Assert.Equal("overview", item.Id);
                Assert.Equal(typeof(SampleOverviewPage), item.ComponentType);
            },
            item =>
            {
                Assert.Equal("settings", item.Id);
                Assert.Equal(typeof(SampleUpdatePage), item.ComponentType);
            });
    }

    [Fact]
    public void AddNavigationItem_RecordsComponentBackedNavigationItem()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<NavigationItemExtension>();

        var registry = GetRegistry(services);
        var item = Assert.Single(Assert.Single(registry.Extensions).NavigationItems);

        Assert.Equal("sample.nav", item.Id);
        Assert.Equal("/sample-overview", item.Href);
        Assert.Equal(typeof(SampleOverviewPage), item.Target.ViewType);
        Assert.Equal("sample.parent", item.ParentId);
        Assert.False(item.ReplacesExisting);
    }

    [Fact]
    public void AddNavigationItem_RecordsHrefTargetWithoutViewRegistration()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<HrefNavigationItemExtension>();

        var registry = GetRegistry(services);
        var extension = Assert.Single(registry.Extensions);
        var item = Assert.Single(extension.NavigationItems);

        Assert.Empty(extension.Views);
        Assert.Equal("sample.docs", item.Id);
        Assert.Equal("https://example.com/docs", item.Href);
        Assert.Equal("https://example.com/docs", item.Target.Href);
    }

    [Fact]
    public void Validate_AllowsExplicitNavigationItemReplacement()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<NavigationItemExtension>()
            .AddExtension<ReplacementNavigationItemExtension>();

        GetRegistry(services).Validate();
    }

    [Fact]
    public void Validate_RejectsDuplicateNavigationItemIdsWithoutReplacement()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<NavigationItemExtension>()
            .AddExtension<DuplicateNavigationItemExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("sample.nav", exception.Message);
    }

    [Fact]
    public void Validate_RejectsNavigationItemReplacementWithoutTarget()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ReplacementNavigationItemExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("cannot be replaced", exception.Message);
    }

    [Fact]
    public void Validate_RejectsMultipleNavigationItemReplacements()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<NavigationItemExtension>()
            .AddExtension<ReplacementNavigationItemExtension>()
            .AddExtension<SecondReplacementNavigationItemExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("replaced by multiple extensions", exception.Message);
    }

    [Fact]
    public void Validate_RejectsDuplicateRoutes()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>()
            .AddExtension<DuplicateRouteExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("/sample", exception.Message);
    }

    [Fact]
    public void Validate_RejectsDuplicateViewIds()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ExplicitViewIdExtension>()
            .AddExtension<DuplicateExplicitViewIdExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("sample.view", exception.Message);
    }

    [Fact]
    public void Validate_RejectsDuplicateCustomViewIds()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<CustomShellViewExtension>()
            .AddExtension<DuplicateCustomShellViewExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("sample.workspace", exception.Message);
    }

    [Fact]
    public void Validate_RejectsUnknownStartRoute()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<UnknownStartRouteExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("/missing", exception.Message);
    }

    [Fact]
    public void Validate_RejectsMultipleStartRoutes()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>()
            .AddExtension<CustomShellViewExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("Multiple extensions configure the shell start route", exception.Message);
    }

    [Fact]
    public void Validate_RejectsMissingCapabilities()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<MissingDependencyExtension>();

        var exception = Assert.Throws<InvalidOperationException>(() => GetRegistry(services).Validate());

        Assert.Contains("sample.missing", exception.Message);
    }

    [Fact]
    public void GetStatuses_DoesNotUseCapabilitiesFromBlockedExtensions()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<TransitiveDependencyExtension>()
            .AddExtension<DownstreamDependencyExtension>();

        var statuses = GetRegistry(services)
            .GetStatuses(new InMemoryCloudShellExtensionActivationStore());

        Assert.All(statuses, status =>
            Assert.Equal(CloudShellExtensionStatusKind.Blocked, status.Kind));
    }

    [Fact]
    public void GetActiveExtensions_ExcludesUserManagedExtensionsWithoutStoredState()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddSupportedExtension<ProviderExtension>();

        var registry = GetRegistry(services);
        var activationStore = new InMemoryCloudShellExtensionActivationStore();
        var status = Assert.Single(registry.GetStatuses(activationStore));

        Assert.Empty(registry.GetActiveExtensions(activationStore));
        Assert.Equal(CloudShellExtensionStatusKind.Disabled, status.Kind);
    }

    [Fact]
    public async Task GetActiveExtensions_ExcludesUserDisabledExtensions()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddSupportedExtension<ProviderExtension>();
        var activationStore = new InMemoryCloudShellExtensionActivationStore();
        await activationStore.SetActivationStateAsync(
            "sample.provider",
            CloudShellExtensionActivationState.Disabled);

        var registry = GetRegistry(services);

        Assert.Empty(registry.GetActiveExtensions(activationStore));
    }

    [Fact]
    public async Task GetActiveExtensions_HostDisabledOverridesUserEnabledState()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .DisableExtension<ProviderExtension>();
        var activationStore = new InMemoryCloudShellExtensionActivationStore();
        await activationStore.SetActivationStateAsync(
            "sample.provider",
            CloudShellExtensionActivationState.Enabled);

        var registry = GetRegistry(services);
        var status = Assert.Single(registry.GetStatuses(activationStore));

        Assert.Empty(registry.GetActiveExtensions(activationStore));
        Assert.Equal(CloudShellExtensionStatusKind.DisabledByHost, status.Kind);
    }

    [Fact]
    public async Task Validate_IgnoresContributionsFromDisabledExtensions()
    {
        var services = new ServiceCollection();

        services
            .AddCloudShell()
            .AddExtension<ProviderExtension>()
            .AddSupportedExtension<DuplicateRouteExtension>();
        var activationStore = new InMemoryCloudShellExtensionActivationStore();
        await activationStore.SetActivationStateAsync(
            "sample.duplicate-route",
            CloudShellExtensionActivationState.Disabled);

        GetRegistry(services).Validate(activationStore);
    }

    [Fact]
    public void AddExtension_RejectsDuplicateExtensionIds()
    {
        var services = new ServiceCollection();
        var builder = services.AddCloudShell().AddExtension<ProviderExtension>();

        var exception = Assert.Throws<InvalidOperationException>(
            () => builder.AddExtension<ProviderExtension>());

        Assert.Contains("sample.provider", exception.Message);
    }

    private static CloudShellExtensionRegistry GetRegistry(IServiceCollection services) =>
        Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(CloudShellExtensionRegistry))
                .ImplementationInstance);

    private sealed class ProviderExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.provider",
            "Sample provider",
            "A test extension.",
            "1.2.3",
            ["sample.resources"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<SamplePage>()
                .AddNavigationItem<SamplePage>("Sample", "sample", 10)
                .UseStartView<SamplePage>()
                .AddResourceProvider<SampleResourceProvider>();
        }
    }

    private sealed class DuplicateRouteExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.duplicate-route",
            "Duplicate route",
            "Contributes a conflicting route.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.RegisterView<DuplicatePage>();
        }
    }

    private sealed class NavigationItemExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.navigation",
            "Sample navigation",
            "Contributes a component-backed navigation item.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<SampleOverviewPage>()
                .AddNavigationItem<SampleOverviewPage>(
                "sample.nav",
                "Sample nav",
                "sample",
                10,
                parentId: "sample.parent");
        }
    }

    private sealed class DuplicateNavigationItemExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.navigation.duplicate",
            "Duplicate navigation",
            "Contributes a duplicate navigation item.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<SampleUpdatePage>()
                .AddNavigationItem<SampleUpdatePage>(
                "sample.nav",
                "Duplicate nav",
                "sample",
                20);
        }
    }

    private sealed class HrefNavigationItemExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.href-navigation",
            "Href navigation",
            "Contributes an href navigation item.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddNavigationItem(
                "sample.docs",
                "Docs",
                NavItemTarget.ForHref("https://example.com/docs"),
                "document",
                10);
        }
    }

    private sealed class ExplicitViewIdExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.explicit-view",
            "Explicit view",
            "Contributes an explicit view id.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.RegisterView<SampleOverviewPage>("sample.view");
        }
    }

    private sealed class DuplicateExplicitViewIdExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.explicit-view-duplicate",
            "Duplicate explicit view",
            "Contributes a duplicate explicit view id.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.RegisterView<SampleUpdatePage>("sample.view");
        }
    }

    private sealed class ReplacementNavigationItemExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.navigation.replacement",
            "Replacement navigation",
            "Replaces a navigation item.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<SampleUpdatePage>()
                .ReplaceNavigationItem<SampleUpdatePage>(
                "sample.nav",
                "Replacement nav",
                "sample",
                10);
        }
    }

    private sealed class SecondReplacementNavigationItemExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.navigation.second-replacement",
            "Second replacement navigation",
            "Also replaces a navigation item.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .RegisterView<SamplePage>()
                .ReplaceNavigationItem<SamplePage>(
                "sample.nav",
                "Second replacement nav",
                "sample",
                15);
        }
    }

    private sealed class MissingDependencyExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.consumer",
            "Sample consumer",
            "Requires a capability that is not installed.",
            "1.0.0",
            [],
            ["sample.missing"]);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
        }
    }

    private sealed class TransitiveDependencyExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.transitive",
            "Sample transitive",
            "Requires a missing capability and provides another capability.",
            "1.0.0",
            ["sample.transitive"],
            ["sample.missing"]);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
        }
    }

    private sealed class DownstreamDependencyExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.downstream",
            "Sample downstream",
            "Requires a capability from a blocked extension.",
            "1.0.0",
            [],
            ["sample.transitive"]);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
        }
    }

    private sealed class LogProviderExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.logs",
            "Sample logs",
            "Contributes log sources.",
            "1.0.0",
            ["sample.logs"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddLogProvider<SampleLogProvider>();
        }
    }

    private sealed class LogSourceContributorExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.log-sources",
            "Sample log sources",
            "Contributes log source metadata.",
            "1.0.0",
            ["sample.log-sources"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddLogSourceContributor<SampleLogSourceContributor>();
        }
    }

    private sealed class ResourceProcedureExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.procedures",
            "Sample procedures",
            "Contributes resource procedures.",
            "1.0.0",
            ["sample.resource"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddResourceType<SampleRegistrationPage, SampleUpdatePage>(
                "sample.resource",
                "Sample resource",
                "A resource with provider-owned procedures.",
                "sample",
                10);
        }
    }

    private sealed class ResourceTabsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.tabs",
            "Sample tabs",
            "Contributes resource tabs.",
            "1.0.0",
            ["sample.tabs"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.tabs",
                    "Sample tabs",
                    "A resource with tabs.",
                    "sample",
                    10)
                .AddResourceTab<SampleUpdatePage>(
                    "sample.tabs",
                    new ResourceViewId(ResourceTabGroupIds.General, "configuration"),
                    "Configuration",
                    20,
                    showsApplyButton: true)
                .AddResourceTab<SampleOverviewPage>(
                    "sample.tabs",
                    new ResourceViewId(ResourceTabGroupIds.General, "overview"),
                    "Overview",
                    10);
        }
    }

    private sealed class ResourceEndpointDescriptorsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.endpoint-descriptors",
            "Sample endpoint descriptors",
            "Contributes resource endpoint descriptors.",
            "1.0.0",
            ["sample.endpoint-descriptors"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.endpoint-descriptors",
                    "Sample endpoint descriptors",
                    "A resource with endpoint descriptors.",
                    "sample",
                    10)
                .AddResourceTypeEndpoint(
                    "sample.endpoint-descriptors",
                    ResourceEndpointDescriptor.Http(targetPort: 8080));
        }
    }

    private sealed class ResourceMonitoringTabExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.monitoring-tab",
            "Sample monitoring tab",
            "Contributes a standard resource monitoring tab.",
            "1.0.0",
            ["sample.monitoring-tab"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.monitoring-tab",
                    "Sample monitoring tab",
                    "A resource with a provider-owned monitoring tab.",
                    "sample",
                    10)
                .AddResourceTab<SampleOverviewPage>(
                    "sample.monitoring-tab",
                    ResourcePredefinedViewIds.Monitoring,
                    "Monitoring",
                    30);
        }
    }

    private sealed class ResourceMetricsTabExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.metrics-tab",
            "Sample metrics tab",
            "Contributes a standard resource telemetry metrics tab.",
            "1.0.0",
            ["sample.metrics-tab"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.metrics-tab",
                    "Sample metrics tab",
                    "A resource with a provider-owned telemetry metrics tab.",
                    "sample",
                    10)
                .AddResourceTab<SampleOverviewPage>(
                    "sample.metrics-tab",
                    ResourcePredefinedViewIds.Metrics,
                    "Metrics",
                    40);
        }
    }

    private sealed class ResourceEnvironmentTabExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.environment-tab",
            "Sample environment tab",
            "Contributes a standard resource environment tab.",
            "1.0.0",
            ["sample.environment-tab"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.environment-tab",
                    "Sample environment tab",
                    "A resource with a provider-owned environment tab.",
                    "sample",
                    10)
                .AddResourceTab<SampleOverviewPage>(
                    "sample.environment-tab",
                    ResourcePredefinedViewIds.Environment,
                    "Environment",
                    40);
        }
    }

    private sealed class PredefinedViewSectionsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.predefined-view-sections",
            "Sample predefined view sections",
            "Contributes sections to generated predefined views.",
            "1.0.0",
            ["sample.predefined-view-sections"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.predefined-view-sections",
                    "Sample predefined view sections",
                    "A resource with predefined view sections.",
                    "sample",
                    10)
                .AddResourcePredefinedViewSection<SampleOverviewPage>(
                    "sample.predefined-view-sections",
                    ResourcePredefinedViewIds.Endpoints,
                    "sample.endpoint-policy",
                    "Endpoint policy",
                    10);
        }
    }

    private sealed class UnknownPredefinedViewSectionsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.unknown-predefined-view-sections",
            "Unknown predefined view sections",
            "Contributes sections to an unknown predefined view.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.unknown-predefined-view-sections",
                    "Sample unknown predefined view sections",
                    "A resource with invalid predefined view sections.",
                    "sample",
                    10)
                .AddResourcePredefinedViewSection<SampleOverviewPage>(
                    "sample.unknown-predefined-view-sections",
                    new ResourceViewId(ResourceTabGroupIds.General, "missing-predefined-view"),
                    "sample.endpoint-policy",
                    "Endpoint policy",
                    10);
        }
    }

    private sealed class NonExtensiblePredefinedViewSectionsExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.non-extensible-predefined-view-sections",
            "Non-extensible predefined view sections",
            "Contributes sections to a non-extensible predefined view.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddResourceType<SampleRegistrationPage>(
                    "sample.non-extensible-predefined-view-sections",
                    "Sample non-extensible predefined view sections",
                    "A resource with invalid predefined view sections.",
                    "sample",
                    10)
                .AddResourcePredefinedViewSection<SampleOverviewPage>(
                    "sample.non-extensible-predefined-view-sections",
                    ResourcePredefinedViewIds.Configuration,
                    "sample.summary",
                    "Summary",
                    10);
        }
    }

    private sealed class CustomShellViewExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.workspace",
            "Sample workspace",
            "Contributes a hosted shell workspace.",
            "1.0.0",
            ["sample.workspace"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder
                .AddCustomView(
                    "sample.workspace",
                    "Sample workspace",
                    "/sample/workspace",
                    "sample",
                    10,
                    description: "A hosted workspace with extension-owned menu items.")
                .AddCustomViewMenuItem<SampleOverviewPage>(
                    "sample.workspace",
                    "overview",
                    "Overview",
                    10)
                .AddCustomViewMenuItem<SampleUpdatePage>(
                    "sample.workspace",
                    "settings",
                    "Settings",
                    20)
                .UseStartRoute("/sample/workspace");
        }
    }

    private sealed class DuplicateCustomShellViewExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.workspace.duplicate",
            "Duplicate workspace",
            "Contributes a duplicate shell-hosted view.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddCustomView(
                "sample.workspace",
                "Duplicate workspace",
                "/sample/workspace-duplicate",
                "sample",
                20);
        }
    }

    private sealed class UnknownStartRouteExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.unknown-start",
            "Unknown start",
            "Configures an unknown start route.",
            "1.0.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.UseStartRoute("/missing");
        }
    }

    private sealed class SampleResourceProvider : IResourceProvider
    {
        public string Id => "sample";

        public string DisplayName => "Sample";

        public IReadOnlyList<Resource> GetResources() => [];
    }

    private sealed class SampleLogProvider : ILogProvider
    {
        public string Id => "sample";

        public string DisplayName => "Sample";

        public IReadOnlyList<LogDescriptor> GetLogs() => [];

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    private sealed class SampleLogSourceContributor : ILogSourceContributor
    {
        public IReadOnlyList<LogSource> GetLogSources() => [];
    }

    [Route("/sample")]
    private sealed class SamplePage;

    [Route("/sample")]
    private sealed class DuplicatePage;

    private sealed class SampleRegistrationPage;

    [Route("/sample-update")]
    private sealed class SampleUpdatePage;

    [Route("/sample-overview")]
    private sealed class SampleOverviewPage;
}
