using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceGraphBuilderTests
{
    [Fact]
    public void ResourceGraphBuilder_DefineResourcesGroupsResourceDeclarations()
    {
        var graph = new ResourceGraphBuilder()
            .DefineResources(resources =>
            {
                resources
                    .AddNetwork("app")
                    .WithDisplayName("App Network");
                resources
                    .AddConfigurationStore("settings")
                    .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries");
            });

        var graphDefinition = graph.BuildGraph();

        Assert.Equal(2, graphDefinition.Resources.Count);
        Assert.Contains(graphDefinition.Resources, resource =>
            resource.TypeId == NetworkResourceTypeProvider.ResourceTypeId &&
            resource.DisplayName == "App Network");
        Assert.Contains(graphDefinition.Resources, resource =>
            resource.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId &&
            resource.EffectiveResourceId == "configuration.store:settings");
    }

    [Fact]
    public void ResourceGraphBuilder_BuildTemplateProjectsGraphIntoResourceTemplate()
    {
        var graph = new ResourceGraphBuilder()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });

        var template = graph.BuildTemplate(
            "grouped",
            environmentId: "local",
            metadata: new Dictionary<string, string>
            {
                ["source"] = "test"
            });
        var definition = Assert.Single(template.Resources);

        Assert.Equal("grouped", template.Name);
        Assert.Equal("local", template.EnvironmentId);
        Assert.NotNull(template.Metadata);
        Assert.Equal("test", template.Metadata["source"]);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, definition.TypeId);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
        Assert.Equal("cloudshell.network:app", definition.ResourceId);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildGraphAssignsResourceIdsByConvention()
    {
        var graph = new ResourceGraphBuilder()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });

        var definition = Assert.Single(graph.BuildGraph().Resources);

        Assert.Equal("cloudshell.network:app", definition.ResourceId);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
    }

    [Fact]
    public void ResourceGraphBuilder_LazilyCreatesDefaultNetworkAndContainerHost()
    {
        var graph = new ResourceGraphBuilder();

        var network = graph.GetDefaultNetwork();
        var sameNetwork = graph.GetDefaultNetwork();
        var containerHost = graph.GetContainerHost();
        var sameContainerHost = graph.GetContainerHost();

        Assert.Same(network, sameNetwork);
        Assert.Same(containerHost, sameContainerHost);

        var definitions = graph.BuildGraph().Resources;
        var networkDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == NetworkResourceTypeProvider.ResourceTypeId);
        var containerHostDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == ContainerHostResourceTypeProvider.ResourceTypeId);

        Assert.Equal(NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId, networkDefinition.ResourceId);
        Assert.Equal("Host", networkDefinition.ResourceAttributeValues[
            NetworkResourceTypeProvider.Attributes.NetworkKind].StringValue);
        Assert.Equal(ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId, containerHostDefinition.ResourceId);
        Assert.Equal("Docker", containerHostDefinition.ResourceAttributeValues[
            ContainerHostResourceTypeProvider.Attributes.HostKind].StringValue);
        Assert.True(containerHostDefinition.ResourceAttributeValues[
            ContainerHostResourceTypeProvider.Attributes.IsDefault].BooleanValue);
    }

    [Fact]
    public void ResourceGraphBuilder_AddRejectsDuplicateResourceIds()
    {
        var graph = new ResourceGraphBuilder();
        graph.GetDefaultNetwork();

        var duplicate = new NetworkResourceDefinitionBuilder("host-copy")
            .WithResourceId(NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);

        var exception = Assert.Throws<InvalidOperationException>(() => graph.Add(duplicate));
        Assert.Contains(
            NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId,
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildGraphRejectsDuplicatesCreatedByDecorators()
    {
        var graph = new ResourceGraphBuilder();
        graph.AddContainerApplication("frontend");
        graph
            .AddJavaScriptApp("frontend", "src/frontend")
            .AsContainer(dockerfile: "Dockerfile");

        var exception = Assert.Throws<InvalidOperationException>(() => graph.BuildGraph());

        Assert.Contains(
            "application.container-app:frontend",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildsConfigurationPayloadFromNativeBuilderApi()
    {
        var graph = new ResourceGraphBuilder()
            .DefineResources(resources =>
            {
                resources
                    .AddNetwork("app")
                    .WithConfiguration(
                        "network",
                        new TestConfiguration("10.0.0.0/24", Enabled: true));
            });

        var definition = Assert.Single(graph.BuildGraph().Resources);
        var configuration = definition.GetConfiguration<TestConfiguration>("network");

        Assert.NotNull(configuration);
        Assert.Equal("10.0.0.0/24", configuration!.AddressPrefix);
        Assert.True(configuration.Enabled);
    }

    [Fact]
    public void ResourceGraphBuilder_UsesConfiguredResourceIdConventionForReferences()
    {
        var graph = new ResourceGraphBuilder(new TestResourceIdConvention("host"))
            .DefineResources(resources =>
            {
                var docker = resources.AddDockerHost("sample");
                resources
                    .AddContainerApplication("api")
                    .UseDockerHost(docker);
            });

        var definitions = graph.BuildGraph().Resources;
        var dockerDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == DockerHostResourceTypeProvider.ResourceTypeId);
        var appDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId);

        Assert.Equal("host/docker.host/sample", dockerDefinition.ResourceId);
        Assert.Equal("host/application.container-app/api", appDefinition.ResourceId);

        var dependency = Assert.Single(appDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyResourceId));
        Assert.Equal(dockerDefinition.ResourceId, dependencyResourceId);
    }

    [Fact]
    public async Task Host_DefineResourcesRegistersImplicitInitialGraph()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, resource.TypeId);
        Assert.Equal("cloudshell.network:app", resource.EffectiveResourceId);
    }

    [Fact]
    public void Host_DefineResourcesRegistersResourceManagerDeclarations()
    {
        var identityProvider = new ResourceIdentityProviderDefinition(
            "identity:development",
            "Development identity",
            ResourceIdentityProviderKind.BuiltIn,
            new Dictionary<string, string>());
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .AddIdentityProvider(identityProvider, useAsDefault: true)
            .DefineResources(resources =>
            {
                var group = resources.AddResourceGroup("group:sample", "Sample");

                IResourceDefinitionBuilder app = null!;
                resources
                    .AddNetwork("app")
                    .WithResourceGroup(group)
                    .WithAutoStart(false)
                    .WithDependencyAutoStart(false)
                    .WithIdentity(identityProvider, identity =>
                    {
                        identity.Name = "app";
                        identity.Subject = "client:app";
                        identity.Scopes.Add("openid");
                    })
                    .ProvisionIdentityOnStartup();
                app = resources.AddNetwork("api");
                resources
                    .AddConfigurationStore("settings")
                    .Allow(app, "configuration.entries.read");
            });
        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            declarations.GetDeclarations(),
            declaration => declaration.ResourceId == "cloudshell.network:app");
        var group = Assert.Single(declarations.GetResourceGroups());
        var provider = Assert.Single(declarations.GetIdentityProviders());
        var grant = Assert.Single(declarations.GetPermissionGrants());

        Assert.Equal("group:sample", group.Id);
        Assert.Equal("identity:development", provider.Id);
        Assert.Equal(ResourceModelResourceProvider.DefaultProviderId, declaration.ProviderId);
        Assert.Equal("cloudshell.network:app", declaration.ResourceId);
        Assert.Equal("group:sample", declaration.ResourceGroupId);
        Assert.Equal("identity:development", declaration.IdentityBinding?.ProviderId);
        Assert.Equal("app", declaration.IdentityBinding?.Name);
        Assert.Equal("client:app", declaration.IdentityBinding?.Subject);
        Assert.Equal(["openid"], declaration.IdentityBinding?.IdentityScopes);
        Assert.True(declaration.ProvisionIdentityOnStartup);
        Assert.False(declarations.ShouldAutoStart(declaration.ResourceId));
        Assert.False(declarations.ShouldAutoStartAsDependency(declaration.ResourceId));
        Assert.Equal("configuration.store:settings", grant.TargetResourceId);
        Assert.Equal("configuration.entries.read", grant.Permission);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("cloudshell.network:api", grant.Principal.SourceResourceId);
    }

    [Fact]
    public async Task Host_DefineResourcesRegistersLazyDefaultResourceDeclarations()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineResources(resources =>
            {
                resources
                    .AddContainerApplication("api")
                    .WithImage("api:latest");
            });
        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();

        var defaultHostDeclaration = Assert.Single(
            declarations.GetDeclarations(),
            declaration => string.Equals(
                declaration.ResourceId,
                ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId,
                StringComparison.OrdinalIgnoreCase));
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.Equal(ResourceModelResourceProvider.DefaultProviderId, defaultHostDeclaration.ProviderId);
        Assert.Contains(snapshot.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
    }

    [Fact]
    public void ControlPlaneResourceGraphBuilder_RequiresHostIdentityProviderContext()
    {
        var graph = new ControlPlaneResourceGraphBuilder();

        var exception = Assert.Throws<InvalidOperationException>(() => graph.GetIdentityProvider());

        Assert.Contains("No default identity provider", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_DefineResourcesConsumesHostIdentityProviderContext()
    {
        var identityProvider = new ResourceIdentityProviderDefinition(
            "identity:development",
            "Development identity",
            ResourceIdentityProviderKind.BuiltIn,
            new Dictionary<string, string>());
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .AddIdentityProvider(identityProvider, useAsDefault: true)
            .DefineResources(resources =>
            {
                var configuredIdentityProvider = resources.GetIdentityProvider();

                resources
                    .AddNetwork("api")
                    .RequireIdentity(name: "api");

                Assert.Equal(identityProvider.Id, configuredIdentityProvider.Id);
                var alice = configuredIdentityProvider.GetUser("alice", displayName: "Alice");
                Assert.Equal(ResourcePrincipalKind.User, alice.Kind);
                Assert.Equal("alice", alice.Id);
                Assert.Equal("Alice", alice.DisplayName);
                Assert.Equal("identity:development", alice.ProviderId);
            });
        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var catalog = declarations.CreateIdentityProviderCatalog(new ResourceIdentityProviderCatalog());
        var provider = Assert.Single(declarations.GetIdentityProviders());
        var declaration = Assert.Single(
            declarations.GetDeclarations(),
            declaration => declaration.ResourceId == "cloudshell.network:api");

        Assert.Equal("identity:development", provider.Id);
        Assert.Equal("identity:development", declarations.DefaultIdentityProviderId);
        Assert.Equal("identity:development", catalog.DefaultProviderId);
        Assert.Equal(ResourceIdentityBindingKind.Required, declaration.IdentityBinding?.Kind);
        Assert.Equal("api", declaration.IdentityBinding?.Name);
    }

    [Fact]
    public void Host_DefineResourcesCanReadConfiguredDefaultIdentityProvider()
    {
        var identityProvider = new ResourceIdentityProviderDefinition(
            "identity:development",
            "Development identity",
            ResourceIdentityProviderKind.BuiltIn,
            new Dictionary<string, string>());
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .AddIdentityProvider(identityProvider, useAsDefault: true)
            .DefineResources(resources =>
            {
                var configuredIdentityProvider = resources.GetIdentityProvider();

                resources
                    .AddNetwork("api")
                    .WithIdentity(configuredIdentityProvider, name: "api");
            });
        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = Assert.Single(declarations.GetIdentityProviders());
        var declaration = Assert.Single(
            declarations.GetDeclarations(),
            declaration => declaration.ResourceId == "cloudshell.network:api");

        Assert.Equal("identity:development", provider.Id);
        Assert.Equal("identity:development", declarations.DefaultIdentityProviderId);
        Assert.Equal("identity:development", declaration.IdentityBinding?.ProviderId);
        Assert.Equal("api", declaration.IdentityBinding?.Name);
    }

    [Fact]
    public async Task Host_DefineResourcesUsesConfiguredResourceIdConvention()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineResources(
                resources =>
                {
                    resources.AddNetwork("app");
                },
                resourceIdConvention: new TestResourceIdConvention("host"));
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal("host/cloudshell.network/app", resource.EffectiveResourceId);
    }

    [Fact]
    public async Task Host_DefineInitialTemplateRegistersTemplateInitialGraph()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineInitialTemplate(
                "grouped",
                resources =>
                {
                    resources.AddNetwork("app");
                },
                environmentId: "local",
                metadata: new Dictionary<string, string>
                {
                    ["source"] = "test"
                });
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, resource.TypeId);
        Assert.Equal("cloudshell.network:app", resource.EffectiveResourceId);
    }

    [Fact]
    public void ResourceDefinitionBuilder_ProjectsIdentityAuthoringReferences()
    {
        var graph = new ResourceGraphBuilder();
        var api = graph.AddAspNetCoreProject("api", "src/Api/Api.csproj");

        var identity = api.Identity("api-service");
        var principal = api.Principal(
            "api-service",
            displayName: "API service",
            providerId: "identity:development");

        Assert.Equal("application.aspnet-core-project:api", identity.ResourceId);
        Assert.Equal("api-service", identity.Name);
        Assert.Equal("application.aspnet-core-project:api/api-service", api.IdentityClientId("api-service"));
        Assert.Equal("application.aspnet-core-project:api", api.IdentityClientId());
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, principal.Kind);
        Assert.Equal("application.aspnet-core-project:api/identities/api-service", principal.Id);
        Assert.Equal("application.aspnet-core-project:api", principal.SourceResourceId);
        Assert.Equal("api-service", principal.SourceIdentityName);
        Assert.Equal("API service", principal.DisplayName);
        Assert.Equal("identity:development", principal.ProviderId);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildsManualNetworkDefinition()
    {
        var graph = new ResourceGraphBuilder();

        graph
            .AddNetwork("app")
            .WithDisplayName("App Network")
            .WithHostReadiness("hostReady")
            .WithMappingProviders("local-host", "dns");

        var template = graph.BuildTemplate("app-network", environmentId: "local");

        var definition = Assert.Single(template.Resources);
        Assert.Equal("app-network", template.Name);
        Assert.Equal("local", template.EnvironmentId);
        Assert.Equal("app", definition.Name);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, definition.TypeId);
        Assert.Equal(NetworkResourceTypeProvider.ProviderId, definition.ProviderId);
        Assert.Equal("App Network", definition.DisplayName);
        Assert.Equal(
            "hostReady",
            definition.ResourceAttributeValues[
                NetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal(
            "local-host,dns",
            definition.ResourceAttributeValues[
                NetworkResourceTypeProvider.Attributes.MappingProviders].StringValue);
    }

    [Fact]
    public async Task ResourceGraphBuilder_FeedsGraphApplyPipeline()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        graph
            .AddNetwork("app")
            .WithDisplayName("App Network")
            .WithNetworkKind("Logical")
            .WithHostReadiness("logicalOnly");

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                graph.BuildTemplate("app-network", environmentId: "local"),
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var state = Assert.Single(snapshot.Resources);

        Assert.Equal("cloudshell.network:app", state.EffectiveResourceId);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, state.TypeId);
        Assert.Equal("App Network", state.DisplayName);
        Assert.NotNull(state.Attributes);
        Assert.Equal(
            "Logical",
            state.Attributes[NetworkResourceTypeProvider.Attributes.NetworkKind].StringValue);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsServiceDefinitionsWithDependencies()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var network = graph
            .AddNetwork("app")
            .WithDisplayName("App Network");

        graph
            .AddConfigurationStore("settings")
            .WithDisplayName("Settings")
            .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries")
            .DependsOn(network);
        graph
            .AddSecretsVault("secrets")
            .WithDisplayName("Secrets")
            .WithEndpoint("http://localhost:5102/api/secrets/vaults/secrets/secrets")
            .DependsOn(network);

        var template = graph.BuildTemplate("settings-and-secrets", environmentId: "local");

        Assert.Equal(3, template.Resources.Count);
        var settings = Assert.Single(template.Resources, resource =>
            resource.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId);
        var secrets = Assert.Single(template.Resources, resource =>
            resource.TypeId == SecretsVaultResourceTypeProvider.ResourceTypeId);

        Assert.Equal("configuration.store:settings", settings.EffectiveResourceId);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ProviderId, settings.ProviderId);
        Assert.Equal("Settings", settings.DisplayName);
        Assert.Equal(
            "http://localhost:5101/api/configuration/stores/settings/entries",
            settings.ResourceAttributeValues[
                ConfigurationStoreResourceTypeProvider.Attributes.Endpoint].StringValue);
        var settingsDependency = Assert.Single(settings.StartupDependencies);
        Assert.True(settingsDependency.TryGetDependsOnResourceId(out var settingsDependencyId));
        Assert.Equal("cloudshell.network:app", settingsDependencyId);

        Assert.Equal("secrets.vault:secrets", secrets.EffectiveResourceId);
        Assert.Equal(SecretsVaultResourceTypeProvider.ProviderId, secrets.ProviderId);
        Assert.Equal("Secrets", secrets.DisplayName);
        Assert.Equal(
            "http://localhost:5102/api/secrets/vaults/secrets/secrets",
            secrets.ResourceAttributeValues[
                SecretsVaultResourceTypeProvider.Attributes.Endpoint].StringValue);
        var secretsDependency = Assert.Single(secrets.StartupDependencies);
        Assert.True(secretsDependency.TryGetDependsOnResourceId(out var secretsDependencyId));
        Assert.Equal("cloudshell.network:app", secretsDependencyId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 13, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.Equal(3, snapshot.Resources.Count);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsStorageAndVolumeDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var storage = graph.AddLocalStorage(
            "local",
            "Data/storage/local");

        storage.AddVolume(
            "data",
            subPath: "data",
            maxSizeBytes: 10 * 1024 * 1024);

        var template = graph.BuildTemplate("storage-volume", environmentId: "local");

        Assert.Equal(2, template.Resources.Count);
        var storageDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == StorageResourceTypeProvider.ResourceTypeId);
        var volumeDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.storage:local", storageDefinition.EffectiveResourceId);
        Assert.Equal("local", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal("FileSystem", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Medium].StringValue);
        Assert.Equal("Data/storage/local", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Location].StringValue);

        Assert.Equal("cloudshell.volume:data", volumeDefinition.EffectiveResourceId);
        Assert.Equal("local", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal("FileSystem", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium].StringValue);
        Assert.Equal("data", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.SubPath].StringValue);
        Assert.Equal(true, volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Persistent].BooleanValue);
        Assert.Equal(10 * 1024 * 1024, volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeBytes].IntegerValue);
        Assert.Equal(VolumeMaxSizeEnforcementModes.Advisory, volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeEnforcement].StringValue);
        var dependency = Assert.Single(volumeDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(storage.EffectiveResourceId, dependencyId);
        Assert.Equal(StorageResourceTypeProvider.ResourceTypeId, dependency.TypeId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 14, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsDirectLocalVolumeDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();

        graph.AddVolume(
            "data",
            path: "./Data/storage/data",
            accessMode: StorageVolumeAccessMode.ReadWriteMany,
            maxSizeBytes: 5 * 1024 * 1024);
        graph.AddVolume("current");

        var template = graph.BuildTemplate("direct-volume", environmentId: "local");

        Assert.Equal(2, template.Resources.Count);
        var volumeDefinition = Assert.Single(template.Resources, resource =>
            resource.Name == "data");
        var currentDirectoryVolume = Assert.Single(template.Resources, resource =>
            resource.Name == "current");
        Assert.Equal("cloudshell.volume:data", volumeDefinition.EffectiveResourceId);
        Assert.Equal("local", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal("FileSystem", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium].StringValue);
        Assert.Equal("./Data/storage/data", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Location].StringValue);
        Assert.Equal("ReadWriteMany", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.AccessMode].StringValue);
        Assert.Equal(5 * 1024 * 1024, volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.MaxSizeBytes].IntegerValue);
        Assert.Empty(volumeDefinition.StartupDependencies);
        Assert.Equal(".", currentDirectoryVolume.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Location].StringValue);
        Assert.Empty(currentDirectoryVolume.StartupDependencies);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 14, 30, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsSqlServerAndDatabaseDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddNetworkResourceType();
        services.AddContainerHostResourceType();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var volume = graph.AddVolume(
            "sql-data",
            path: "./Data/storage/sql-server");
        var server = graph
            .AddSqlServer("sql")
            .WithVersion("2022")
            .WithEdition("Developer")
            .AddEndpointRequest(
                "tds",
                "tcp",
                targetPort: 1433,
                host: "localhost",
                port: 14334,
                exposure: "Local")
            .MountVolume(volume, "/var/opt/mssql")
            .DeclareDatabase("appdb", "Application DB", ensureCreated: true);

        graph
            .AddSqlDatabase("appdb")
            .BelongsToServer(server)
            .EnsureCreated();

        var template = graph.BuildTemplate("sql-app", environmentId: "local");

        Assert.Equal(5, template.Resources.Count);
        var volumeDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId);
        var hostNetwork = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);
        var containerHost = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
        var sqlServer = Assert.Single(template.Resources, resource =>
            resource.TypeId == SqlServerResourceTypeProvider.ResourceTypeId);
        var sqlDatabase = Assert.Single(template.Resources, resource =>
            resource.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId);
        Assert.Equal("./Data/storage/sql-server", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Location].StringValue);
        Assert.Equal("application.sql-server:sql", sqlServer.EffectiveResourceId);
        Assert.Equal("2022", sqlServer.ResourceAttributeValues[
            SqlServerResourceTypeProvider.Attributes.Version].StringValue);
        Assert.Equal("Host", hostNetwork.ResourceAttributeValues[
            NetworkResourceTypeProvider.Attributes.NetworkKind].StringValue);
        Assert.Equal("Docker", containerHost.ResourceAttributeValues[
            ContainerHostResourceTypeProvider.Attributes.HostKind].StringValue);
        var endpoint = Assert.Single(sqlServer.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            SqlServerResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("tds", endpoint.Name);
        Assert.Equal(1433, endpoint.TargetPort);
        Assert.Equal(14334, endpoint.Port);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var endpointNetworkId));
        Assert.Equal(hostNetwork.EffectiveResourceId, endpointNetworkId);
        var sqlConfiguration = sqlServer.GetConfiguration<SqlServerConfiguration>(
            SqlServerResourceTypeProvider.ConfigurationSection);
        var databaseConfiguration = Assert.Single(sqlConfiguration!.Databases);
        Assert.Equal("appdb", databaseConfiguration.Name);
        Assert.Equal("Application DB", databaseConfiguration.DisplayName);
        Assert.True(databaseConfiguration.EnsureCreated);
        var volumeConsumer = sqlServer.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        Assert.True(sqlServer.ResourceAttributeValues.ContainsKey(
            ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString())));
        Assert.Null(sqlServer.Capabilities);
        var mount = Assert.Single(volumeConsumer!.Mounts);
        Assert.Equal(volume.EffectiveResourceId, mount.Volume);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);

        Assert.Equal("application.sql-database:appdb", sqlDatabase.EffectiveResourceId);
        Assert.Equal("appdb", sqlDatabase.ResourceAttributeValues[
            SqlDatabaseResourceTypeProvider.Attributes.DatabaseName].StringValue);
        Assert.Equal(true, sqlDatabase.ResourceAttributeValues[
            SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated].BooleanValue);
        var dependency = Assert.Single(sqlDatabase.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(server.EffectiveResourceId, dependencyId);
        Assert.Equal(SqlServerResourceTypeProvider.ResourceTypeId, dependency.TypeId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 15, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsContainerHostAndApplicationDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddNetworkResourceType();
        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var volume = graph.AddVolume(
            "data",
            path: "./Data/storage/api");
        var host = graph
            .AddDockerHost("engine")
            .UseLocalDocker();

        graph
            .AddContainerApplication("api")
            .UseDockerHost(host)
            .WithImage("example/api:1.0")
            .WithRegistry("registry.local")
            .WithReplicas(2)
            .WithCookieSessionAffinity("CloudShellReplica", durationSeconds: 3600)
            .AddEndpointRequest(
                "http",
                "http",
                targetPort: 8080,
                host: "localhost",
                port: 5092,
                exposure: "Local")
            .AddHealthCheck(ResourceHealthCheckDefinition.Http(
                "/health",
                endpointName: "http"))
            .MountVolume(volume, "/data");

        var template = graph.BuildTemplate("container-app", environmentId: "local");

        Assert.Equal(4, template.Resources.Count);
        var hostDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == DockerHostResourceTypeProvider.ResourceTypeId);
        var hostNetwork = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);
        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId);
        Assert.DoesNotContain(template.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
        Assert.Equal("docker.host:engine", hostDefinition.EffectiveResourceId);
        Assert.Equal("local", hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.HostKind].StringValue);
        Assert.Equal("unix:///var/run/docker.sock", hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.Endpoint].StringValue);
        Assert.Equal(true, hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.IsDefault].BooleanValue);

        Assert.Equal("application.container-app:api", appDefinition.EffectiveResourceId);
        Assert.Equal("example/api:1.0", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage].StringValue);
        Assert.Equal("registry.local", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry].StringValue);
        Assert.Equal(2, appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
        Assert.Equal("Cookie", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityMode].StringValue);
        Assert.Equal("CloudShellReplica", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityCookieName].StringValue);
        Assert.Equal(3600, appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.RoutingSessionAffinityDurationSeconds].IntegerValue);
        var endpoint = Assert.Single(appDefinition.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Equal(5092, endpoint.Port);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var endpointNetworkId));
        Assert.Equal(hostNetwork.EffectiveResourceId, endpointNetworkId);
        var healthChecks = appDefinition.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        Assert.True(appDefinition.ResourceAttributeValues.ContainsKey(
            ResourceHealthCheckAttributeIds.HealthChecks));
        var healthCheck = Assert.Single(healthChecks?.Checks ?? []);
        Assert.Equal("/health", healthCheck.Source.Http?.Path);
        Assert.True(appDefinition.ResourceAttributeValues.ContainsKey(
            ResourceLogSourceAttributeIds.LogSources));
        Assert.NotNull(appDefinition.GetCapability<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources));
        var dependency = Assert.Single(appDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(host.EffectiveResourceId, dependencyId);
        Assert.Equal(DockerHostResourceTypeProvider.ResourceTypeId, dependency.TypeId);
        var volumeConsumer = appDefinition.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        Assert.True(appDefinition.ResourceAttributeValues.ContainsKey(
            ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString())));
        var mount = Assert.Single(volumeConsumer!.Mounts);
        Assert.Equal(volume.EffectiveResourceId, mount.Volume);
        Assert.Equal("/data", mount.TargetPath);
        Assert.NotEqual("api", mount.TargetPath);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 16, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildsContainerApplicationVirtualNetworkEndpointRequest()
    {
        var services = new ServiceCollection();
        services.AddContainerApplicationResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var network = graph
            .AddVirtualNetwork("apps")
            .WithDisplayName("Apps network");

        graph
            .AddContainerApplication("api")
            .WithImage("ghcr.io/example/api:latest")
            .WithHttpEndpoint(
                name: "vnet-http",
                targetPort: 8080,
                port: 80,
                exposure: "Network",
                ipAddress: "10.42.0.20",
                network: network,
                assignment: "Manual");

        var template = graph.BuildTemplate("container-app-vnet", environmentId: "local");
        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId);
        var endpoint = Assert.Single(appDefinition.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? []);

        Assert.Equal("vnet-http", endpoint.Name);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Equal(80, endpoint.Port);
        Assert.Equal("Network", endpoint.Exposure);
        Assert.Equal("Manual", endpoint.Assignment);
        Assert.Equal("10.42.0.20", endpoint.IpAddress);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var networkResourceId));
        Assert.Equal(network.EffectiveResourceId, networkResourceId);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsExecutableAndProjectDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var volume = graph.AddVolume(
            "app-data",
            path: "./Data/storage/app");
        var settings = graph
            .AddConfigurationStore("settings")
            .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries");

        graph
            .AddExecutableApplication("worker")
            .WithCommand("dotnet", "run --project src/Worker/Worker.csproj", "src/Worker")
            .MountVolume(volume, "App_Data");
        graph
            .AddAspNetCoreProject("api", "src/Api/Api.csproj")
            .WithHotReload()
            .UseLaunchSettings(false)
            .WithServiceDiscovery()
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5010,
                exposure: "Local")
            .WithEnvironmentVariable(
                "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                "http://localhost:5104/api/control-plane/v1/traces/ingest")
            .WithReference(settings)
            .MountVolume(volume, "App_Data")
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                interval: TimeSpan.FromSeconds(10));

        var template = graph.BuildTemplate("project-app", environmentId: "local");

        Assert.Equal(5, template.Resources.Count);
        var executable = Assert.Single(template.Resources, resource =>
            resource.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId);
        var hostNetwork = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);
        var project = Assert.Single(template.Resources, resource =>
            resource.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId);
        Assert.Equal("application.executable:worker", executable.EffectiveResourceId);
        Assert.Equal("dotnet", executable.ResourceAttributeValues[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath].StringValue);
        var executableConfiguration = executable.GetConfiguration<ExecutableApplicationConfiguration>(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection);
        Assert.Equal("dotnet", executableConfiguration!.Path);
        Assert.Equal("run --project src/Worker/Worker.csproj", executableConfiguration.Arguments);
        Assert.Equal("src/Worker", executableConfiguration.WorkingDirectory);
        Assert.Null(executable.Capabilities);

        Assert.Equal("application.aspnet-core-project:api", project.EffectiveResourceId);
        Assert.Equal("src/Api/Api.csproj", project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath].StringValue);
        Assert.Equal(false, project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings].BooleanValue);
        Assert.Equal("api", project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName].StringValue);
        var endpoint = Assert.Single(project.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var endpointNetworkId));
        Assert.Equal(hostNetwork.EffectiveResourceId, endpointNetworkId);
        Assert.Equal(5010, endpoint.Port);
        var environmentVariables = project.ResourceAttributeValues
            .GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables) ?? [];
        Assert.True(environmentVariables.ContainsKey("CLOUDSHELL_TRACE_INGEST_ENDPOINT"));
        var reference = Assert.Single(project.ResourceAttributeValues.GetObject<ResourceReference[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.References) ?? []);
        Assert.Equal(ResourceReferenceRelationships.Reference, reference.Relationship);
        Assert.Equal(settings.EffectiveResourceId, reference.Value);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ResourceTypeId, reference.TypeId);
        var healthChecks = project.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        Assert.True(project.ResourceAttributeValues.ContainsKey(
            ResourceHealthCheckAttributeIds.HealthChecks));
        var healthCheck = Assert.Single(healthChecks!.Checks ?? []);
        Assert.Equal("alive", healthCheck.Name);
        Assert.True(project.ResourceAttributeValues.ContainsKey(
            ResourceLogSourceAttributeIds.LogSources));
        Assert.NotNull(project.GetCapability<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources));
        var projectVolumeConsumer = project.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        Assert.True(project.ResourceAttributeValues.ContainsKey(
            ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString())));
        Assert.Null(project.Capabilities);
        var projectMount = Assert.Single(projectVolumeConsumer!.Mounts);
        Assert.Equal(volume.EffectiveResourceId, projectMount.Volume);
        Assert.Equal("App_Data", projectMount.TargetPath);
        Assert.NotEqual("app", projectMount.TargetPath);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 17, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public void AspNetCoreProjectResourceDefinitionBuilder_CanDeclareVirtualNetworkPrivateEndpointRequest()
    {
        var graph = new ResourceGraphBuilder();
        var network = graph
            .AddVirtualNetwork("app", isDefault: true)
            .WithDisplayName("App Network");

        graph
            .AddAspNetCoreProject("api", "src/Api/Api.csproj")
            .WithHttpEndpoint(
                name: "vnet-http",
                port: 80,
                targetPort: 80,
                exposure: "Network",
                ipAddress: "10.42.0.10",
                network: network,
                assignment: "Manual");

        var template = graph.BuildTemplate("app-network");
        var api = Assert.Single(template.Resources, resource =>
            resource.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId);
        var endpoint = Assert.Single(api.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? []);

        Assert.Equal("vnet-http", endpoint.Name);
        Assert.Equal(80, endpoint.TargetPort);
        Assert.Equal(80, endpoint.Port);
        Assert.Equal("10.42.0.10", endpoint.IpAddress);
        Assert.Equal("Network", endpoint.Exposure);
        Assert.Equal("Manual", endpoint.Assignment);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var networkResourceId));
        Assert.Equal(network.EffectiveResourceId, networkResourceId);
        Assert.Equal(VirtualNetworkResourceTypeProvider.ResourceTypeId, endpoint.Network.TypeId);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsJavaScriptAppDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddJavaScriptAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var settings = graph
            .AddConfigurationStore("settings")
            .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries");

        graph
            .AddJavaScriptApp("frontend", "src/frontend")
            .WithPackageManager("pnpm")
            .WithScript("dev")
            .WithArguments("-- --host 127.0.0.1")
            .WithServiceDiscovery()
            .WithHttpEndpoint(
                port: 5173,
                targetPort: 5173,
                host: "localhost")
            .WithEnvironmentVariable("NODE_ENV", "development")
            .WithReference(settings)
            .WithHttpLivenessCheck("/alive", endpointName: "http");

        var template = graph.BuildTemplate("javascript-app", environmentId: "local");

        var app = Assert.Single(template.Resources, resource =>
            resource.TypeId == JavaScriptAppResourceTypeProvider.ResourceTypeId);
        var hostNetwork = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);
        Assert.Equal("application.javascript-app:frontend", app.EffectiveResourceId);
        Assert.Equal(JavaScriptAppResourceTypeProvider.ProviderId, app.ProviderId);
        Assert.Equal("src/frontend", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.ProjectPath].StringValue);
        Assert.Equal("node", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.Engine].StringValue);
        Assert.Equal("pnpm", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.PackageManager].StringValue);
        Assert.Equal("dev", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.Script].StringValue);
        Assert.Equal("-- --host 127.0.0.1", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.Arguments].StringValue);
        Assert.Equal("frontend", app.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.ServiceDiscoveryName].StringValue);

        var endpoint = Assert.Single(app.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5173, endpoint.Port);
        Assert.Equal(5173, endpoint.TargetPort);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var endpointNetworkId));
        Assert.Equal(hostNetwork.EffectiveResourceId, endpointNetworkId);

        var environmentVariables = app.ResourceAttributeValues
            .GetObject<Dictionary<string, JavaScriptAppEnvironmentVariableValue>>(
                JavaScriptAppResourceTypeProvider.Attributes.EnvironmentVariables) ?? [];
        Assert.Equal("development", environmentVariables["NODE_ENV"].Value);
        var reference = Assert.Single(app.ResourceAttributeValues.GetObject<ResourceReference[]>(
            JavaScriptAppResourceTypeProvider.Attributes.References) ?? []);
        Assert.Equal(ResourceReferenceRelationships.Reference, reference.Relationship);
        Assert.Equal(settings.EffectiveResourceId, reference.Value);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ResourceTypeId, reference.TypeId);
        Assert.NotNull(app.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks));
        Assert.NotNull(app.GetCapability<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources));

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public void ResourceGraphBuilder_BuildsJavaScriptAppAsContainerDefinition()
    {
        var graph = new ResourceGraphBuilder();

        var app = graph
            .AddJavaScriptApp("frontend", "src/frontend")
            .WithHttpEndpoint(
                port: 5173,
                targetPort: 8080,
                host: "localhost")
            .AsContainer(tag: "dev", dockerfile: "Dockerfile")
            .WithReplicas(3);

        var template = graph.BuildTemplate("javascript-container-app", environmentId: "local");

        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == app.EffectiveResourceId);
        var hostDefinition = Assert.Single(template.Resources, resource =>
            resource.TypeId == ContainerHostResourceTypeProvider.ResourceTypeId);
        var hostNetwork = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);

        Assert.Equal(ContainerApplicationResourceTypeProvider.ResourceTypeId, appDefinition.TypeId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.ProviderId, appDefinition.ProviderId);
        Assert.Equal("application.container-app:frontend", appDefinition.EffectiveResourceId);
        Assert.Equal("src/frontend", appDefinition.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.ProjectPath].StringValue);
        Assert.Equal("node", appDefinition.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.Engine].StringValue);
        Assert.Equal("npm", appDefinition.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.PackageManager].StringValue);
        Assert.Equal("dev", appDefinition.ResourceAttributeValues[
            JavaScriptAppResourceTypeProvider.Attributes.Script].StringValue);
        Assert.Equal("cloudshell-javascript-frontend:dev", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage].StringValue);
        Assert.Equal("src/frontend", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerBuildContext].StringValue);
        Assert.Equal("Dockerfile", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerDockerfile].StringValue);
        Assert.Equal(3, appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
        Assert.False(appDefinition.ResourceAttributeValues.ContainsKey(
            JavaScriptAppResourceTypeProvider.Attributes.EndpointRequests));

        var endpoint = Assert.Single(appDefinition.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5173, endpoint.Port);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.NotNull(endpoint.Network);
        Assert.True(endpoint.Network!.TryGetResourceId(out var endpointNetworkId));
        Assert.Equal(hostNetwork.EffectiveResourceId, endpointNetworkId);

        var dependency = Assert.Single(appDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(hostDefinition.EffectiveResourceId, dependencyId);
        Assert.Equal(ContainerHostResourceTypeProvider.ResourceTypeId, dependency.TypeId);
    }

    [Fact]
    public void ResourceGraphBuilder_PreservesContainerReplicasConfiguredBeforeContainerProjection()
    {
        var graph = new ResourceGraphBuilder();

        var app = graph
            .AddJavaScriptApp("frontend", "src/frontend")
            .WithReplicas(3)
            .AsContainer(tag: "dev", dockerfile: "Dockerfile");

        var template = graph.BuildTemplate("javascript-container-app", environmentId: "local");

        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == app.EffectiveResourceId);
        Assert.Equal(3, appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsIdentityProvisioningDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddIdentityProvisioningResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();

        graph
            .AddIdentityProvisioning("built-in")
            .WithIdentityProvider("Built-in Identity")
            .WithIdentityProviderId("built-in")
            .WithProviderKind("built-in");

        var template = graph.BuildTemplate("identity", environmentId: "local");

        var identity = Assert.Single(template.Resources);
        Assert.Equal("cloudshell.identity-provisioning:built-in", identity.EffectiveResourceId);
        Assert.Equal(IdentityProvisioningResourceTypeProvider.ProviderId, identity.ProviderId);
        Assert.Equal("Built-in Identity", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider].StringValue);
        Assert.Equal("built-in", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId].StringValue);
        Assert.Equal("built-in", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsExposureDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerApplicationResourceType();
        services.AddContainerHostResourceType();
        services.AddNetworkResourceType();
        services.AddServiceResourceType();
        services.AddDnsZoneResourceType();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var api = graph
            .AddContainerApplication("application-topology-api")
            .WithImage("example/application-topology-api:1.0");
        var network = graph
            .AddNetwork("application-topology-local");
        var apiService = graph
            .AddService("application-topology-api-service")
            .DependsOnTarget(api)
            .DependsOnNetwork(network)
            .WithRoutingMode("logical");
        var zone = graph
            .AddDnsZone("application-topology-local")
            .WithZoneName("application-topology.cloudshell.local")
            .WithProvider("hosts-file");

        zone.MapHost(
            "api.application-topology.cloudshell.local",
            apiService,
            endpointName: "http",
            name: "application-topology-api-local");

        var template = graph.BuildTemplate("application-exposure", environmentId: "local");

        Assert.Equal(6, template.Resources.Count);
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
        var service = Assert.Single(template.Resources, resource =>
            resource.TypeId == ServiceResourceTypeProvider.ResourceTypeId);
        var nameMapping = Assert.Single(template.Resources, resource =>
            resource.TypeId == NameMappingResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.service:application-topology-api-service", service.EffectiveResourceId);
        Assert.Equal("logical", service.ResourceAttributeValues[
            ServiceResourceTypeProvider.Attributes.RoutingMode].StringValue);
        Assert.Equal(
            [api.EffectiveResourceId, network.EffectiveResourceId],
            service.StartupDependencies.Select(reference => reference.Value));
        Assert.Equal("cloudshell.nameMapping:application-topology-api-local", nameMapping.EffectiveResourceId);
        Assert.Equal("api.application-topology.cloudshell.local", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.HostName].StringValue);
        Assert.Equal("http", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.TargetEndpointName].StringValue);
        Assert.Equal("Public", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.Exposure].StringValue);
        Assert.Empty(nameMapping.StartupDependencyIds);
        Assert.Contains(nameMapping.ResourceDependencies, reference =>
            reference.Relationship == ResourceReferenceRelationships.BelongsTo &&
            reference.TypeId == DnsZoneResourceTypeProvider.ResourceTypeId &&
            reference.Value == zone.EffectiveResourceId);
        Assert.Contains(nameMapping.ResourceDependencies, reference =>
            reference.Relationship == ResourceReferenceRelationships.Reference &&
            reference.Value == apiService.EffectiveResourceId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 19, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsDockerContainerAndLocalVolumeDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerContainerResourceType();
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();

        graph
            .AddDockerContainer("api")
            .WithImage("example/api:1.0")
            .WithRegistry("registry.local")
            .WithReplicas(2);
        graph
            .AddLocalVolume("data")
            .WithStorageMedium("local");

        var template = graph.BuildTemplate("docker-container", environmentId: "local");

        Assert.Equal(2, template.Resources.Count);
        var container = Assert.Single(template.Resources, resource =>
            resource.TypeId == DockerContainerResourceTypeProvider.ResourceTypeId);
        var volume = Assert.Single(template.Resources, resource =>
            resource.TypeId == LocalVolumeResourceTypeProvider.ResourceTypeId);
        Assert.Equal("docker.container:api", container.EffectiveResourceId);
        Assert.Equal("example/api:1.0", container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerImage].StringValue);
        Assert.Equal("registry.local", container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry].StringValue);
        Assert.Equal(2, container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
        Assert.Equal("storage.volume:data", volume.EffectiveResourceId);
        Assert.Equal("local", volume.ResourceAttributeValues[
            LocalVolumeResourceTypeProvider.Attributes.StorageMedium].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 20, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsLoadBalancerAndHostConfigurationDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerHostResourceType();
        services.AddContainerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddLoadBalancerResourceType();
        services.AddHostConfigurationSourceResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var host = graph.AddDockerHost("engine");
        var target = graph
            .AddContainerApplication("api")
            .WithImage("example/api:1.0");

        graph
            .AddLoadBalancer("edge")
            .UseHost(host)
            .AddBackendTarget(target, ContainerApplicationResourceTypeProvider.ResourceTypeId)
            .WithProvider("traefik");
        graph
            .AddHostConfigurationSource("host-settings")
            .WithSource("host");

        var template = graph.BuildTemplate("infrastructure", environmentId: "local");

        Assert.Equal(5, template.Resources.Count);
        Assert.Contains(template.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
        var loadBalancer = Assert.Single(template.Resources, resource =>
            resource.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId);
        var hostConfiguration = Assert.Single(template.Resources, resource =>
            resource.TypeId == HostConfigurationSourceResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.loadBalancer:edge", loadBalancer.EffectiveResourceId);
        Assert.Equal("traefik", loadBalancer.ResourceAttributeValues[
            LoadBalancerResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal(host.EffectiveResourceId, loadBalancer.ResourceAttributeValues[
            LoadBalancerResourceTypeProvider.Attributes.HostResourceId].StringValue);
        Assert.Equal(
            [host.EffectiveResourceId, target.EffectiveResourceId],
            loadBalancer.StartupDependencies.Select(reference => reference.Value));
        Assert.Equal("configuration.host:host-settings", hostConfiguration.EffectiveResourceId);
        Assert.Equal("host", hostConfiguration.ResourceAttributeValues[
            HostConfigurationSourceResourceTypeProvider.Attributes.Source].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 21, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceGraphBuilder_BuildsHostNetworkingDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithHostReadiness("ready")
            .WithNetworkingMode("localProxy");
        var api = graph
            .AddAspNetCoreProject("api", "src/Api/Api.csproj")
            .UseLaunchSettings(false);

        var networkBuilder = graph
            .AddVirtualNetwork("app", isDefault: true)
            .DependsOn(hostNetwork)
            .DependsOn(api)
            .WithHostReadiness("providerRequired")
            .WithMappingProviders(hostNetwork);
        var publicEndpoint = networkBuilder.AddHttpEndpoint(
            "localhost",
            port: 5292,
            name: "api-public",
            exposure: "Public");
        networkBuilder.MapEndpoint(
            publicEndpoint,
            api,
            "http",
            hostNetwork,
            id: "mapping:api-public",
            name: "API public ingress");

        var template = graph.BuildTemplate("host-network", environmentId: "local");

        Assert.Equal(3, template.Resources.Count);
        var localHost = Assert.Single(template.Resources, resource =>
            resource.TypeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId);
        var network = Assert.Single(template.Resources, resource =>
            resource.TypeId == VirtualNetworkResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.hostNetworking.local:host-local", localHost.EffectiveResourceId);
        Assert.Equal("ready", localHost.ResourceAttributeValues[
            LocalHostNetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal("localProxy", localHost.ResourceAttributeValues[
            LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode].StringValue);
        Assert.Equal("cloudshell.virtualNetwork:app", network.EffectiveResourceId);
        Assert.True(network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.IsDefault].BooleanValue);
        Assert.Equal("providerRequired", network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal(hostNetwork.EffectiveResourceId, network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.MappingProviders].StringValue);
        var endpoint = Assert.Single(network.ResourceAttributeValues.GetObject<NetworkingEndpointValue[]>(
            VirtualNetworkResourceTypeProvider.Attributes.Endpoints) ?? []);
        Assert.Equal("api-public", endpoint.Name);
        Assert.Equal("Http", endpoint.Protocol);
        Assert.Equal(5292, endpoint.TargetPort);
        Assert.Equal("Public", endpoint.Exposure);
        var endpointNetworkMapping = Assert.Single(
            network.ResourceAttributeValues.GetObject<NetworkingEndpointNetworkMappingValue[]>(
                VirtualNetworkResourceTypeProvider.Attributes.EndpointNetworkMappings) ?? []);
        Assert.Equal("http://localhost:5292", endpointNetworkMapping.Address);
        Assert.Equal("api-public", endpointNetworkMapping.SourceEndpointName);
        Assert.Equal("api-public", endpointNetworkMapping.Target.EndpointName);
        var endpointMapping = Assert.Single(
            network.ResourceAttributeValues.GetObject<NetworkingEndpointMappingValue[]>(
                VirtualNetworkResourceTypeProvider.Attributes.EndpointMappings) ?? []);
        Assert.Equal("mapping:api-public", endpointMapping.Id);
        Assert.Equal("api-public", endpointMapping.Source.EndpointName);
        Assert.Equal("http", endpointMapping.Target.EndpointName);
        Assert.Equal(
            [hostNetwork.EffectiveResourceId, api.EffectiveResourceId],
            network.StartupDependencies.Select(reference => reference.Value));

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    private sealed class TestResourceIdConvention(string prefix) : IResourceIdConvention
    {
        public string CreateResourceId(ResourceIdConventionContext context) =>
            $"{prefix}/{context.TypeId}/{context.Name}";
    }

    private sealed record TestConfiguration(
        string AddressPrefix,
        bool Enabled);
}
