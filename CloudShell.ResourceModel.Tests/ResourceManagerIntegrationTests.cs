using System.Globalization;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerResourceState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceManagerIntegrationTests
{
    [Fact]
    public void ResourceModelResourceProvider_ProjectsResolvedResourceIntoResourceManagerShape()
    {
        var resolved = CreateResolver().Resolve(CreateExecutableState());
        var provider = new ResourceModelResourceProvider(
            "resource-model",
            "Resource model",
            () => [resolved],
            new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal("application.executable:api", projected.Id);
        Assert.Equal("api", projected.Name);
        Assert.Equal("API", projected.DisplayName);
        Assert.Equal("application.executable", projected.Kind);
        Assert.Equal("application.executable", projected.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, projected.Provider);
        Assert.Equal(ResourceManagerResourceState.Unknown, projected.State);
        Assert.Equal(ResourceManagerClass.Executable, projected.ResourceClass);
        Assert.Equal(ResourceSource.User, projected.Source);
        Assert.Equal(ResourceManagementMode.UserManaged, projected.ManagementMode);
        Assert.Equal(["storage.volume:data"], projected.DependsOn);
        Assert.Equal("dotnet", projected.ResourceAttributes["path"]);
        Assert.Equal(ResourceGraphMembershipKinds.Declared, projected.ResourceGraphMembership);
        Assert.Equal(
            "resource-model",
            projected.ResourceAttributes[ResourceModelResourceManagerAttributeNames.BridgeProviderId]);
        Assert.Contains(projected.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projected.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        var logSource = Assert.Single(projected.ResourceLogSources);
        Assert.Equal("console", logSource.Id);
        Assert.Equal("Console logs", logSource.Name);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, logSource.Kind);
        Assert.Equal(LogFormat.PlainText, logSource.Format);
        Assert.Equal(
            LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
            logSource.Capabilities);
        Assert.Equal(ResourceLogSourceOrigin.ProviderDefault, logSource.Origin);
        Assert.Equal(ResourceLogSourcePurpose.Default, logSource.Purpose);
        Assert.Equal(LogSourceAvailability.ResourceRunning, logSource.Availability);
        Assert.True(projected.SupportsLogSources);
        Assert.Contains(projected.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void ResourceModelResourceProvider_UsesProjectionStateResolver()
    {
        var resolved = CreateResolver().Resolve(CreateExecutableState());
        var provider = new ResourceModelResourceProvider(
            "resource-model",
            "Resource model",
            () => [resolved],
            new ResourceModelResourceManagerProjectionOptions(
                StateResolver: resource =>
                    resource.EffectiveResourceId == resolved.EffectiveResourceId
                        ? ResourceManagerResourceState.Running
                        : null));

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal(ResourceManagerResourceState.Running, projected.State);
    }

    [Fact]
    public void ResourceModelResourceProvider_UsesProjectionEndpointResolver()
    {
        var resolved = CreateResolver().Resolve(CreateExecutableState());
        var provider = new ResourceModelResourceProvider(
            "resource-model",
            "Resource model",
            () => [resolved],
            new ResourceModelResourceManagerProjectionOptions(
                EndpointProjectionResolver: resource =>
                    resource.EffectiveResourceId == resolved.EffectiveResourceId
                        ? new ResourceModelResourceManagerEndpointProjection(
                            Endpoints:
                            [
                                ResourceEndpoint.Contract(
                                    "http",
                                    "http",
                                    ResourceExposureScope.Local,
                                    5010)
                            ],
                            EndpointNetworkMappings:
                            [
                                ResourceEndpointNetworkMapping.ForEndpoint(
                                    resource.EffectiveResourceId,
                                    "http",
                                    "http://localhost:5010")
                            ])
                        : null));

        var projected = Assert.Single(provider.GetResources());

        var endpoint = Assert.Single(projected.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(5010, endpoint.TargetPort);
        var endpointNetworkMapping = Assert.Single(projected.ResourceEndpointNetworkMappings);
        Assert.Equal("http://localhost:5010", endpointNetworkMapping.Address);
        Assert.Equal(resolved.EffectiveResourceId, endpointNetworkMapping.Target.ResourceId);
        Assert.Equal("http", endpointNetworkMapping.Target.EndpointName);
        Assert.Equal("http://localhost:5010", projected.PrimaryEndpoint);
    }

    [Fact]
    public void ResourceModelResourceProvider_UsesProjectionObservabilityResolver()
    {
        var resolved = CreateResolver().Resolve(CreateExecutableState());
        var provider = new ResourceModelResourceProvider(
            "resource-model",
            "Resource model",
            () => [resolved],
            new ResourceModelResourceManagerProjectionOptions(
                ObservabilityResolver: resource =>
                    resource.EffectiveResourceId == resolved.EffectiveResourceId
                        ? new ResourceObservability(
                            Logs: true,
                            Traces: true,
                            Metrics: true,
                            ServiceName: "api")
                        : null));

        var projected = Assert.Single(provider.GetResources());

        Assert.True(projected.EffectiveObservability.Logs);
        Assert.True(projected.EffectiveObservability.Traces);
        Assert.True(projected.EffectiveObservability.Metrics);
        Assert.Equal("api", projected.EffectiveObservability.ServiceName);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_ResolvesSnapshotIntoResourceManagerShape()
    {
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [CreateExecutableState()]),
            CreateResolver(),
            projectionOptions: new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal("application.executable:api", projected.Id);
        Assert.Equal("api", projected.Name);
        Assert.Equal("API", projected.DisplayName);
        Assert.Equal("application.executable", projected.Kind);
        Assert.Equal(ResourceManagerClass.Executable, projected.ResourceClass);
        Assert.Equal(["storage.volume:data"], projected.DependsOn);
        Assert.Equal("dotnet", projected.ResourceAttributes["path"]);
        Assert.Equal(
            "resource-model",
            projected.ResourceAttributes[ResourceModelResourceManagerAttributeNames.BridgeProviderId]);
        Assert.Contains(projected.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projected.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_ProjectsCapabilityProvidedDependencies()
    {
        var api = CreateExecutableState(dependsOn: []);
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api]),
            CreateResolver(),
            [new VolumeConsumerGraphDependencyProvider()]);

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal(["storage.volume:data"], projected.DependsOn);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_DoesNotProjectBelongsToReferenceAsDependency()
    {
        var server = CreateExecutableState(
            "server",
            dependsOn: [],
            includeVolumeConsumer: false);
        var database = CreateExecutableState(
            "appdb",
            dependsOn: [],
            includeVolumeConsumer: false) with
        {
            DependsOn =
                [
                    ResourceReference.BelongsToResourceId(
                        server.EffectiveResourceId)
                ]
        };
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [database, server]),
            CreateResolver());

        var projected = provider.GetResources()
            .Single(resource => resource.Id == database.EffectiveResourceId);

        Assert.Empty(projected.DependsOn);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_DoesNotProjectAspNetCoreProjectReferencesAsDependencies()
    {
        var api = CreateAspNetCoreProjectState("api");
        var frontend = CreateAspNetCoreProjectState(
            "frontend",
            references:
            [
                ResourceReference.ReferenceResourceId(
                    api.EffectiveResourceId,
                    typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            ]);
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, frontend]),
            CreateAspNetCoreProjectResolver());

        var projected = provider.GetResources()
            .Single(resource => resource.Id == frontend.EffectiveResourceId);

        Assert.Empty(projected.DependsOn);
        Assert.False(projected.ResourceAttributes.ContainsKey(
            AspNetCoreProjectResourceTypeProvider.Attributes.References));
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_ProjectsAspNetCoreProjectTypeExpectedCapabilities()
    {
        var api = CreateAspNetCoreProjectState("api");
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api]),
            CreateAspNetCoreProjectResolver());

        var projected = Assert.Single(provider.GetResources());

        Assert.True(projected.HasCapability(ResourceCapabilityIds.EndpointSource));
        Assert.True(projected.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.False(projected.HasCapability(ResourceLogSourceCapabilityIds.LogSources.ToString()));
    }

    [Fact]
    public async Task AspNetCoreProjectResourceManagerMonitoringProvider_ProjectsProcessMetrics()
    {
        var runtime = new RecordingAspNetCoreProjectRuntimeController
        {
            Status = AspNetCoreProjectRuntimeStatus.Running,
            MonitoringSnapshot = new ResourceProcessMonitoringSnapshot(
                123,
                new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 28, 8, 1, 0, TimeSpan.Zero),
                12.5,
                TimeSpan.FromSeconds(42),
                64 * 1024 * 1024,
                48 * 1024 * 1024,
                8)
        };
        var provider = new AspNetCoreProjectResourceManagerMonitoringProvider(runtime);
        var resource = new ResourceManagerResource(
            "application.dotnet-app:api",
            "api",
            "application.dotnet-app",
            "Resource model",
            "local",
            ResourceManagerResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString());

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal(".NET app", snapshot.Provider);
        Assert.Equal("Available", snapshot.Status);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.cpu.usage" &&
            metric.Value == 12.5);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.process.count" &&
            metric.Attributes?["process.id"] == "123");
    }

    [Fact]
    public async Task ExecutableApplicationResourceManagerMonitoringProvider_ProjectsProcessMetrics()
    {
        var runtime = new RecordingExecutableApplicationRuntimeController
        {
            MonitoringSnapshot = new ResourceProcessMonitoringSnapshot(
                321,
                new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 28, 8, 1, 0, TimeSpan.Zero),
                6.75,
                TimeSpan.FromSeconds(22),
                24 * 1024 * 1024,
                18 * 1024 * 1024,
                5)
        };
        var provider = new ExecutableApplicationResourceManagerMonitoringProvider(runtime);
        var resource = new ResourceManagerResource(
            "application.executable:worker",
            "worker",
            "application.executable",
            "Resource model",
            "local",
            ResourceManagerResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString());

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Executable application", snapshot.Provider);
        Assert.Equal("Available", snapshot.Status);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.cpu.usage" &&
            metric.Value == 6.75);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.process.count" &&
            metric.Attributes?["process.id"] == "321");
    }

    [Fact]
    public async Task ConfigurationStoreResourceManagerMonitoringProvider_ProjectsProcessMetrics()
    {
        var runtime = new RecordingConfigurationStoreRuntimeController
        {
            Status = ResourceWebAppRuntimeStatus.Running,
            MonitoringSnapshot = new ResourceProcessMonitoringSnapshot(
                456,
                new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 28, 8, 1, 0, TimeSpan.Zero),
                3.25,
                TimeSpan.FromSeconds(11),
                32 * 1024 * 1024,
                24 * 1024 * 1024,
                6)
        };
        var provider = new ConfigurationStoreResourceManagerMonitoringProvider(runtime);
        var resource = new ResourceManagerResource(
            "configuration.store:settings",
            "settings",
            "configuration.store",
            "Resource model",
            "local",
            ResourceManagerResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString());

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Configuration Store", snapshot.Provider);
        Assert.Equal("Available", snapshot.Status);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.cpu.usage" &&
            metric.Value == 3.25);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.process.count" &&
            metric.Attributes?["process.id"] == "456");
    }

    [Fact]
    public async Task SecretsVaultResourceManagerMonitoringProvider_ProjectsProcessMetrics()
    {
        var runtime = new RecordingSecretsVaultRuntimeController
        {
            Status = ResourceWebAppRuntimeStatus.Running,
            MonitoringSnapshot = new ResourceProcessMonitoringSnapshot(
                789,
                new DateTimeOffset(2026, 6, 28, 8, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 6, 28, 8, 1, 0, TimeSpan.Zero),
                1.5,
                TimeSpan.FromSeconds(7),
                16 * 1024 * 1024,
                12 * 1024 * 1024,
                4)
        };
        var provider = new SecretsVaultResourceManagerMonitoringProvider(runtime);
        var resource = new ResourceManagerResource(
            "secrets.vault:vault",
            "vault",
            "secrets.vault",
            "Resource model",
            "local",
            ResourceManagerResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: SecretsVaultResourceTypeProvider.ResourceTypeId.ToString());

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Secrets Vault", snapshot.Provider);
        Assert.Equal("Available", snapshot.Status);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.cpu.usage" &&
            metric.Value == 1.5);
        Assert.Contains(snapshot.Metrics, metric =>
            metric.Name == "resource.process.count" &&
            metric.Attributes?["process.id"] == "789");
    }

    [Fact]
    public void BuiltInProviderResourceManagerProjections_RegistersProcessMonitoringProviders()
    {
        var services = new ServiceCollection();

        services.AddBuiltInProviderResourceManagerProjections();
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider.GetServices<IResourceMonitoringProvider>().ToArray();

        Assert.Contains(providers, provider =>
            provider.GetType() == typeof(AspNetCoreProjectResourceManagerMonitoringProvider));
        Assert.Contains(providers, provider =>
            provider.GetType() == typeof(ExecutableApplicationResourceManagerMonitoringProvider));
        Assert.Contains(providers, provider =>
            provider.GetType() == typeof(ConfigurationStoreResourceManagerMonitoringProvider));
        Assert.Contains(providers, provider =>
            provider.GetType() == typeof(SecretsVaultResourceManagerMonitoringProvider));
        Assert.Contains(providers, provider =>
            provider.GetType() == typeof(JavaScriptAppResourceManagerMonitoringProvider));
    }

    [Fact]
    public void BuiltInProviderResourceManagerIntegration_RegistersProjectionsAndGraphProcedureBridge()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();

        services.AddBuiltInProviderResourceManagerIntegration();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.Contains(
            serviceProvider.GetServices<IResourceMonitoringProvider>(),
            provider => provider.GetType() == typeof(AspNetCoreProjectResourceManagerMonitoringProvider));
        var provider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceProvider>()));
        var availabilityProvider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceActionAvailabilityProvider>()));

        Assert.Same(provider, availabilityProvider);
    }

    [Fact]
    public void UseResourceGraphIntegration_RegistersGraphServicesAndResourceManagerBridge()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        var builder = services.AddCloudShellControlPlane();

        builder.UseResourceGraphIntegration();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<ResourceResolver>());
        var provider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceProvider>()));
        var availabilityProvider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceActionAvailabilityProvider>()));

        Assert.Same(provider, availabilityProvider);
    }

    [Fact]
    public void UseResourceProviderMethods_EnsureSingleResourceGraphIntegration()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        var builder = services.AddCloudShellControlPlane();

        builder
            .UseConfigurationStoreResourceProvider()
            .UseSecretsVaultResourceProvider()
            .UseAspNetCoreProjectResourceProvider();
        using var serviceProvider = services.BuildServiceProvider();

        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();

        Assert.Contains(ConfigurationStoreResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SecretsVaultResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(AspNetCoreProjectResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.NotNull(serviceProvider.GetService<ResourceResolver>());
        Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceProvider>()));
        Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceActionAvailabilityProvider>()));
    }

    [Fact]
    public async Task UseBuiltInResourceModelProviders_RegistersProviderCatalogAndGraphBridgeWithoutMaterializingDefaults()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        var builder = services.AddCloudShellControlPlane();

        builder
            .UseBuiltInResourceModelProviders()
            .UseConfigurationStoreResourceProvider(runtime =>
            {
                runtime.ServiceProjectPath = "services/configuration-store.csproj";
            })
            .UseSecretsVaultResourceProvider(runtime =>
            {
                runtime.ServiceProjectPath = "services/secrets-vault.csproj";
            });
        using var serviceProvider = services.BuildServiceProvider();

        var typeIds = serviceProvider
            .GetServices<IResourceTypeProvider>()
            .Select(provider => provider.TypeId)
            .ToHashSet();

        Assert.Contains(ExecutableApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerApplicationResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ConfigurationStoreResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(SecretsVaultResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(NetworkResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Contains(ContainerHostResourceTypeProvider.ResourceTypeId, typeIds);
        Assert.Equal(
            "services/configuration-store.csproj",
            serviceProvider.GetRequiredService<ConfigurationStoreRuntimeOptions>().ServiceProjectPath);
        Assert.Equal(
            "services/secrets-vault.csproj",
            serviceProvider.GetRequiredService<SecretsVaultRuntimeOptions>().ServiceProjectPath);
        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<INetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphEndpointMappingReconciler>(
            serviceProvider.GetRequiredService<IVirtualNetworkEndpointMappingReconciler>());
        Assert.IsType<ResourceModelGraphDnsZoneNameMappingReconciler>(
            serviceProvider.GetRequiredService<IDnsZoneNameMappingReconciler>());

        var provider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceProvider>()));
        var availabilityProvider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceActionAvailabilityProvider>()));

        Assert.Same(provider, availabilityProvider);

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.DoesNotContain(snapshot.Resources, resource =>
            resource.EffectiveResourceId == NetworkResourceDefinitionBuilderExtensions.DefaultNetworkResourceId);
        Assert.DoesNotContain(snapshot.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);
        Assert.Contains(snapshot.Resources, resource =>
            resource.EffectiveResourceId == CreateExecutableState().EffectiveResourceId);
    }

    [Fact]
    public async Task AddDefaultInMemoryResourceModelGraphResources_DoesNotOverrideExplicitInitialState()
    {
        var explicitHost = new ResourceState(
            "default",
            ContainerHostResourceTypeProvider.ResourceTypeId,
            ResourceId: ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId,
            ProviderId: ContainerHostResourceTypeProvider.ProviderId,
            DisplayName: "Explicit container host");
        var presetGraph = new ResourceGraphBuilder();
        presetGraph.GetContainerHost();
        var presetResources = presetGraph
            .BuildGraph()
            .Resources
            .Select(ResourceState.FromDefinition);
        var services = new ServiceCollection();

        services.AddInMemoryResourceModelGraph([explicitHost]);
        services.AddDefaultInMemoryResourceModelGraphResources(presetResources);
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var host = Assert.Single(snapshot.Resources, resource =>
            resource.EffectiveResourceId == ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId);

        Assert.Equal("Explicit container host", host.DisplayName);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_DoesNotProjectInvalidTypedDependency()
    {
        var worker = CreateExecutableState(
            "worker",
            dependsOn: [],
            includeVolumeConsumer: false);
        var api = CreateExecutableState(
            dependsOn: [],
            includeVolumeConsumer: false) with
        {
            DependsOn =
                [
                    ResourceReference.DependsOnResourceId(
                        worker.EffectiveResourceId,
                        typeId: LocalVolumeResourceTypeProvider.ResourceTypeId)
                ]
        };
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, worker]),
            CreateResolver(),
            [new VolumeConsumerGraphDependencyProvider()]);

        var projected = provider.GetResources()
            .Single(resource => resource.Id == api.EffectiveResourceId);
        var diagnostic = Assert.Single(provider.GetResourceModelDiagnostics());

        Assert.Empty(projected.DependsOn);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch, diagnostic.Code);
        Assert.Equal(api.EffectiveResourceId, diagnostic.ResourceId);
        Assert.Contains(worker.EffectiveResourceId, diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_UsesRegisteredStateProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceModelResourceManagerStateProvider>(
            new StaticResourceModelStateProvider(
                "application.executable:api",
                ResourceManagerResourceState.Running));
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .Single();

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal(ResourceManagerResourceState.Running, projected.State);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_UsesRegisteredEndpointProjectionProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceModelResourceManagerEndpointProjectionProvider>(
            new StaticResourceModelEndpointProjectionProvider("application.executable:api"));
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .Single();

        var projected = Assert.Single(provider.GetResources());

        var endpoint = Assert.Single(projected.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5010, endpoint.TargetPort);
        var endpointNetworkMapping = Assert.Single(projected.ResourceEndpointNetworkMappings);
        Assert.Equal("http://localhost:5010", endpointNetworkMapping.Address);
        Assert.Equal("http://localhost:5010", projected.PrimaryEndpoint);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_UsesRegisteredObservabilityProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceModelResourceManagerObservabilityProvider>(
            new StaticResourceModelObservabilityProvider("application.executable:api"));
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .Single();

        var projected = Assert.Single(provider.GetResources());

        Assert.True(projected.EffectiveObservability.Logs);
        Assert.True(projected.EffectiveObservability.Traces);
        Assert.True(projected.EffectiveObservability.Metrics);
        Assert.Equal("api", projected.EffectiveObservability.ServiceName);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesBoundResourceFromGraph()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(
                "application.executable:api",
                new ResourceDefinitionResolutionContext("local", "developer"));

        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        var target = Assert.Single(resolution.Resources);
        Assert.Same(target, resolution.Target);
        Assert.Equal("application.executable:api", target.EffectiveResourceId);

        var volumes = target.Capabilities.Get<VolumeConsumerCapability>();
        var start = target.Operations.Get<ExecutableStartOperation>();

        Assert.NotNull(volumes);
        Assert.NotNull(start);
        Assert.Same(target, volumes.Resource);
        Assert.Same(target, start.Resource);
        Assert.Equal("storage.volume:data", Assert.Single(volumes.Mounts).Volume);
        Assert.True(await start.CanExecuteAsync());
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsDiagnosticWhenResourceIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync("application.executable:missing");

        Assert.True(resolution.HasErrors);
        Assert.Null(resolution.Target);
        Assert.Empty(resolution.Resources);
        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, diagnostic.Code);
        Assert.Equal("application.executable:missing", diagnostic.Target);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_CanResolveDependencyClosure()
    {
        var worker = CreateExecutableState(
            "worker",
            dependsOn: [],
            includeVolumeConsumer: false);
        var api = CreateExecutableState(
            "api",
            dependsOn: [worker.EffectiveResourceId],
            includeVolumeConsumer: false);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([api, worker]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [api.EffectiveResourceId, worker.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var reference = Assert.Single(resolution.ResolvedReferences);
        Assert.True(reference.IsResolved);
        Assert.Equal(worker.EffectiveResourceId, reference.Reference.Value);
        Assert.Same(resolution.Resources[1], reference.Resource);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_IncludesCapabilityProvidedDependencies()
    {
        var volume = CreateLocalVolumeState();
        var api = CreateExecutableState(dependsOn: []);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([api, volume]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [api.EffectiveResourceId, volume.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var reference = Assert.Single(resolution.ResolvedReferences);
        Assert.True(reference.IsResolved);
        Assert.Equal(volume.EffectiveResourceId, reference.Reference.Value);
        Assert.Same(resolution.Resources[1], reference.Resource);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesReferenceFromGraph()
    {
        var volume = CreateLocalVolumeState();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([volume]);
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveReferenceAsync(ResourceReference.DependsOnResourceId(volume.EffectiveResourceId));

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        Assert.Equal(volume.EffectiveResourceId, resolution.Reference.Value);
        Assert.Equal(volume.EffectiveResourceId, resolution.Resource?.EffectiveResourceId);
        Assert.Equal(LocalVolumeResourceTypeProvider.ResourceTypeId, resolution.Resource?.Type.TypeId);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_DoesNotBindProjectionsForInvalidTypedReference()
    {
        var worker = CreateExecutableState(
            "worker",
            dependsOn: [],
            includeVolumeConsumer: false);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([worker]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveReferenceAsync(ResourceReference.DependsOnResourceId(
                worker.EffectiveResourceId,
                typeId: LocalVolumeResourceTypeProvider.ResourceTypeId));

        Assert.False(resolution.IsResolved);
        Assert.True(resolution.HasErrors);
        Assert.Equal(worker.EffectiveResourceId, resolution.Resource?.EffectiveResourceId);
        Assert.Null(resolution.Resource?.Operations.Get(
            ExecutableApplicationResourceTypeProvider.Operations.Start));
        Assert.Contains(resolution.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsUnresolvedReferenceForUnsupportedAddressingMode()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState()]);
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var reference = new ResourceReference(
            "provider-native-volume",
            ResourceReferenceRelationships.DependsOn,
            ResourceReferenceAddressingModes.ProviderNative);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveReferenceAsync(reference);

        Assert.False(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        Assert.Empty(resolution.Diagnostics);
        Assert.Same(reference, resolution.Reference);
        Assert.Null(resolution.Resource);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesCapabilityProjection()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveCapabilityAsync(
                "application.executable:api",
                VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        var capability = Assert.IsType<VolumeConsumerCapability>(resolution.Capability);
        Assert.Same(resolution.Resource, capability.Resource);
        Assert.Equal(VolumeConsumerCapabilityProvider.CapabilityIdValue, resolution.CapabilityId);
        Assert.Equal("storage.volume:data", Assert.Single(capability.Mounts).Volume);

        var changes = capability.AddMount(new("storage:logs", "Logs"));

        Assert.Same(resolution.Resource, changes.Resource);
        Assert.Equal(resolution.Resource?.EffectiveResourceId, changes.Resource.EffectiveResourceId);
        var attributeChange = Assert.Single(changes.AttributeChanges);
        Assert.Equal(
            ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString()),
            attributeChange.AttributeId);
        Assert.Empty(changes.CapabilityChanges);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsDiagnosticWhenCapabilityProjectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddSingleton(ExecutableApplicationResourceTypeProvider.ClassDefinition);
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveCapabilityAsync(
                "application.executable:api",
                VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.False(resolution.IsResolved);
        Assert.True(resolution.HasErrors);
        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceCapabilityProjectionMissing, diagnostic.Code);
        Assert.Equal("application.executable:api", diagnostic.Target);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesResourceManagerActionToOperationProjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutableApplicationRuntimeController, NoopExecutableApplicationRuntimeController>();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync("application.executable:api", ResourceAction.Start);

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        var operation = Assert.IsType<ExecutableStartOperation>(resolution.Operation);
        Assert.Same(resolution.Resource, operation.Resource);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Operations.Start, resolution.OperationId);

        var executableOperation = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            resolution.Operation);
        Assert.True(await executableOperation.CanExecuteAsync());
        Assert.False((await executableOperation.ExecuteAsync()).HasErrors);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesCustomOperationProjection()
    {
        var volume = CreateLocalVolumeState();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([volume]);
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var action = new ResourceAction(
            LocalVolumeResourceTypeProvider.Operations.Provision.ToString(),
            "Provision");

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync(volume.EffectiveResourceId, action);

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        var operation = Assert.IsType<LocalVolumeProvisionOperation>(resolution.Operation);
        Assert.Same(resolution.Resource, operation.Resource);
        Assert.Equal(LocalVolumeResourceTypeProvider.Operations.Provision, resolution.OperationId);
        Assert.False(await operation.CanExecuteAsync());
        Assert.Contains("no local volume provisioner", operation.UnavailableReason, StringComparison.Ordinal);
        var execution = await operation.ExecuteAsync();

        Assert.True(execution.HasErrors);
        Assert.Contains(execution.Diagnostics, diagnostic =>
            diagnostic.Code == "storage.volume.provisionUnavailable");
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsDiagnosticWhenOperationProjectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddSingleton(ExecutableApplicationResourceTypeProvider.ClassDefinition);
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync("application.executable:api", ResourceAction.Start);

        Assert.False(resolution.IsResolved);
        Assert.True(resolution.HasErrors);
        var diagnostic = Assert.Single(resolution.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceOperationProjectionMissing, diagnostic.Code);
        Assert.Equal("application.executable:api", diagnostic.Target);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ExecutesExecutableOperationProjection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IExecutableApplicationRuntimeController, NoopExecutableApplicationRuntimeController>();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = provider.GetResources()
            .Single(resource => resource.Id == "application.executable:api");
        var procedure = new ResourceProcedureContext(
            resource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.True(provider.CanEvaluateAction(resource, ResourceAction.Start));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, ResourceAction.Start));

        var result = await provider.ExecuteActionAsync(procedure, ResourceAction.Start);

        Assert.Equal("Executed Start for api.", result.Message);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ExecutesProviderOwnedOperationService()
    {
        var runtimeController = new RecordingExecutableApplicationRuntimeController();
        var services = new ServiceCollection();
        services.AddSingleton<IExecutableApplicationRuntimeController>(runtimeController);
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = provider.GetResources()
            .Single(resource => resource.Id == "application.executable:api");
        var procedure = new ResourceProcedureContext(
            resource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        var result = await provider.ExecuteActionAsync(procedure, ResourceAction.Start);

        Assert.Equal("Executed Start for api.", result.Message);
        Assert.Equal([resource.Id], runtimeController.StartedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_BlocksExecutionForInvalidTypedDependency()
    {
        var worker = CreateExecutableState(
            "worker",
            dependsOn: [],
            includeVolumeConsumer: false);
        var api = CreateExecutableState(
            dependsOn: [],
            includeVolumeConsumer: false) with
        {
            DependsOn =
                [
                    ResourceReference.DependsOnResourceId(
                        worker.EffectiveResourceId,
                        typeId: LocalVolumeResourceTypeProvider.ResourceTypeId)
                ]
        };
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([api, worker]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = provider.GetResources()
            .Single(resource => resource.Id == api.EffectiveResourceId);
        var procedure = new ResourceProcedureContext(
            resource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        var reason = await provider.GetActionUnavailableReasonAsync(procedure, ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains(worker.EffectiveResourceId, reason, StringComparison.Ordinal);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.ExecuteActionAsync(procedure, ResourceAction.Start));
        Assert.Contains(worker.EffectiveResourceId, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ExecutesCustomOperationProjection()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState()]);
        var provisioner = new RecordingLocalVolumeProvisioner();
        services.AddSingleton<ILocalVolumeProvisioner>(provisioner);
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = Assert.Single(provider.GetResources());
        var action = Assert.Single(resource.ResourceActions, action =>
            action.Id == LocalVolumeResourceTypeProvider.Operations.Provision.ToString());
        var procedure = new ResourceProcedureContext(
            resource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Equal("Storage Volume Provision", action.DisplayName);
        Assert.True(provider.CanEvaluateAction(resource, action));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, action));

        var result = await provider.ExecuteActionAsync(procedure, action);

        Assert.Equal(
            "Executed Storage Volume Provision for data. Provisioned local volume.",
            result.Message);
        Assert.Equal(["storage.volume:data"], provisioner.ProvisionedResourceIds);
    }

    [Fact]
    public async Task ResourceDefinitionTemplateService_AppliesTemplateWithoutProviderSerialization()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var templates = serviceProvider.GetRequiredService<ResourceDefinitionTemplateService>();
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: "API",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    "template-volume",
                    typeId: LocalVolumeResourceTypeProvider.ResourceTypeId,
                    providerId: LocalVolumeResourceTypeProvider.ProviderId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            });
        var template = new ResourceTemplate(
            "applications",
            [
                definition with
                {
                    DependsOn =
                    [
                        ResourceReference.DependsOnResourceId(
                            "storage.volume:data",
                            typeId: LocalVolumeResourceTypeProvider.ResourceTypeId,
                            providerId: LocalVolumeResourceTypeProvider.ProviderId)
                    ]
                }
            ],
            EnvironmentId: "local");

        var result = await templates.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(EnvironmentId: "local", PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var graphModel = serviceProvider.GetRequiredService<ResourceGraphModel>();
        var imported = (await graphModel.GetSnapshotAsync()).Resources
            .Single(resource => resource.EffectiveResourceId == "application.executable:api");
        Assert.Equal("API", imported.DisplayName);
        Assert.Equal("dotnet", imported.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        var dependency = Assert.Single(imported.StartupDependencies);
        Assert.Equal("storage.volume:data", dependency.Value);
        Assert.Equal(LocalVolumeResourceTypeProvider.ResourceTypeId, dependency.TypeId);
        Assert.Equal(LocalVolumeResourceTypeProvider.ProviderId, dependency.ProviderId);
    }

    [Fact]
    public void AddResourceModelGraphProcedureProvider_RegistersSameScopedBridgeForProviderAndAvailability()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();

        var provider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceProvider>()));
        var availabilityProvider = Assert.IsType<ResourceModelGraphProcedureProvider>(
            Assert.Single(serviceProvider.GetServices<IResourceActionAvailabilityProvider>()));

        Assert.Same(provider, availabilityProvider);
    }

    [Fact]
    public void ResourceModelGraphProcedureProvider_DoesNotEvaluateActionsForOtherProviderResources()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = new ResourceManagerResource(
            "other:api",
            "api",
            "other.kind",
            "other-provider",
            "local",
            CloudShell.Abstractions.ResourceManager.ResourceState.Stopped,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            Actions: [ResourceAction.Start],
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.ResourceGraphMembership] = ResourceGraphMembershipKinds.Declared
            });

        Assert.False(provider.CanEvaluateAction(resource, ResourceAction.Start));
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ReturnsUnavailableReasonWhenProjectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelGraphServices(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resource = Assert.Single(provider.GetResources());
        var procedure = new ResourceProcedureContext(
            resource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        var reason = await provider.GetActionUnavailableReasonAsync(procedure, ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains("no operation projection is available", reason);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.ExecuteActionAsync(procedure, ResourceAction.Start));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_CommitsDefinitionOverlay()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphVersion.Initial, result.BaseVersion);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Single(result.Changes.AcceptedResources);

        var committed = result.Commit.Snapshot!.Resources.Single(resource =>
            resource.EffectiveResourceId == "application.executable:api");
        Assert.Equal(new ResourceRevision(1), committed.Revision);
        Assert.Equal(
            "dotnet-watch",
            committed.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_MatchesExplicitIdResourceByNameWhenDefinitionHasNoId()
    {
        var explicitIdState = CreateExecutableState() with
        {
            ResourceId = "application.executable:custom-api"
        };
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), explicitIdState]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        var committed = result.Commit.Snapshot!.Resources.Single(resource =>
            resource.EffectiveResourceId == "application.executable:custom-api");
        Assert.Equal("api", committed.Name);
        Assert.Equal("application.executable:custom-api", committed.EffectiveResourceId);
        Assert.Equal(
            "dotnet-watch",
            committed.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsRenameWhenDefinitionTargetsExplicitId()
    {
        var explicitIdState = CreateExecutableState() with
        {
            ResourceId = "application.executable:custom-api"
        };
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), explicitIdState]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "renamed-api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ResourceId: "application.executable:custom-api",
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 0, 0, TimeSpan.Zero)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(
            ResourceDefinitionDiagnosticCodes.ResourceDefinitionIdentityChangeNotAllowed,
            diagnostic.Code);
        var state = (await serviceProvider.GetRequiredService<ResourceGraphModel>().GetSnapshotAsync())
            .Resources
            .Single(resource => resource.EffectiveResourceId == "application.executable:custom-api");
        Assert.Equal("api", state.Name);
        Assert.Equal("dotnet", state.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsProviderDiagnostics()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = ""
                    })
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 0, 0, TimeSpan.Zero)));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Equal("application.executable.pathRequired", diagnostic.Code);

        var graphModel = serviceProvider.GetRequiredService<ResourceGraphModel>();
        var snapshot = await graphModel.GetSnapshotAsync();
        var state = snapshot.Resources.Single(resource =>
            resource.EffectiveResourceId == "application.executable:api");
        Assert.Equal(
            "dotnet",
            state.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesTemplateAndCreatesMissingResource()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var template = new ResourceTemplate(
            "local-app",
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
                    })
            ],
            EnvironmentId: "local");

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 30, 0, TimeSpan.Zero)));

        var created = Assert.Single(result.Commit.Snapshot!.Resources);
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphVersion.Initial, result.BaseVersion);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.True(Assert.Single(result.Changes.AcceptedResources).ChangeSet.IsNewResource);
        Assert.Equal("application.executable:api", created.EffectiveResourceId);
        Assert.Equal(new ResourceRevision(1), created.Revision);
        Assert.Equal("dotnet", created.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesIdentityAndAccessGrantAttributesToDeclarations()
    {
        var declarations = new ResourceDeclarationStore();
        var services = new ServiceCollection();
        services.AddSingleton(declarations);
        services.AddSingleton<IResourcePermissionGrantReader>(declarations);
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var builder = new TestCloudShellBuilder(services);

        declarations.Declare(
            builder,
            ResourceModelResourceProvider.DefaultProviderId,
            "application.executable:api");
        declarations.Declare(
            builder,
            ResourceModelResourceProvider.DefaultProviderId,
            "configuration.store:settings");

        var template = ResourceTemplateSerializer.DeserializeTemplate("""
resources:
  - type: application.executable
    name: api
    path: dotnet
    identity:
      kind: provider
      providerId: identity:development
      name: api-service
      subject: application.executable:api
      provisionOnStartup: true
      scopes:
      - configuration.read
      claims:
        resource: application.executable:api
  - type: configuration.store
    name: settings
    endpoint: http://localhost:5101
    access:
      grants:
      - principal:
          kind: resourceIdentity
          id: application.executable:api/identities/api-service
          providerId: identity:development
          sourceResourceId: application.executable:api
          sourceIdentityName: api-service
        permission: CloudShell.Configuration/stores/settings/read/action
""");

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        var apiDeclaration = declarations.GetDeclaration("application.executable:api");
        Assert.NotNull(apiDeclaration);
        Assert.Equal("identity:development", apiDeclaration.IdentityBinding?.ProviderId);
        Assert.Equal("api-service", apiDeclaration.IdentityBinding?.Name);
        Assert.Equal("application.executable:api", apiDeclaration.IdentityBinding?.Subject);
        Assert.Equal(["configuration.read"], apiDeclaration.IdentityBinding?.IdentityScopes);
        Assert.Equal("application.executable:api", apiDeclaration.IdentityBinding?.IdentityClaims["resource"]);
        Assert.True(apiDeclaration.ProvisionIdentityOnStartup);

        var grant = Assert.Single(declarations.GetPermissionGrants());
        Assert.Equal("configuration.store:settings", grant.TargetResourceId);
        Assert.Equal(ConfigurationStoreResourceOperationPermissions.ReadSettings, grant.Permission);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("application.executable:api/identities/api-service", grant.Principal.Id);
        Assert.Equal("identity:development", grant.Principal.ProviderId);
        Assert.Equal("application.executable:api", grant.Principal.SourceResourceId);
        Assert.Equal("api-service", grant.Principal.SourceIdentityName);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_HonorsExplicitApplyModes()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var createTemplate = new ResourceTemplate(
            "create-worker",
            [
                new(
                    "worker",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-worker"
                    })
            ]);

        var updateOnlyCreate = await service.ApplyTemplateAsync(
            createTemplate,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 45, 0, TimeSpan.Zero)),
            ResourceModelGraphDefinitionApplyOptions.Default);

        var missingDiagnostic = Assert.Single(updateOnlyCreate.Diagnostics);
        Assert.True(updateOnlyCreate.HasErrors);
        Assert.False(updateOnlyCreate.IsCommitted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, missingDiagnostic.Code);
        Assert.Equal("application.executable:worker", missingDiagnostic.Target);

        var createOnlyExisting = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 46, 0, TimeSpan.Zero)),
            new ResourceModelGraphDefinitionApplyOptions(ResourceDefinitionApplyMode.CreateOnly));

        var existsDiagnostic = Assert.Single(createOnlyExisting.Diagnostics);
        Assert.True(createOnlyExisting.HasErrors);
        Assert.False(createOnlyExisting.IsCommitted);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceAlreadyExists, existsDiagnostic.Code);
        Assert.Equal("application.executable:api", existsDiagnostic.Target);

        var graphModel = serviceProvider.GetRequiredService<ResourceGraphModel>();
        var snapshot = await graphModel.GetSnapshotAsync();
        var state = snapshot.Resources.Single(resource =>
            resource.EffectiveResourceId == "application.executable:api");
        Assert.Equal("application.executable:api", state.EffectiveResourceId);
        Assert.Equal("dotnet", state.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_ReconcilesOnlyAfterCommittedAcceptedState()
    {
        var reconciler = new RecordingGraphApplyReconciler();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddSingleton<IResourceModelGraphApplyReconciler>(reconciler);
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var rejected = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = ""
                    })
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 47, 0, TimeSpan.Zero)));

        Assert.True(rejected.HasErrors);
        Assert.False(rejected.IsCommitted);
        Assert.Empty(reconciler.Contexts);

        var accepted = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 48, 0, TimeSpan.Zero)));

        Assert.False(accepted.HasErrors, FormatDiagnostics(accepted.Diagnostics));
        Assert.True(accepted.IsCommitted);
        var context = Assert.Single(reconciler.Contexts);
        Assert.Equal(ResourceGraphCommitStatus.Committed, context.Commit.Summary.Status);
        Assert.Equal("application.executable:api", Assert.Single(context.Changes.AcceptedResources)
            .AcceptedState!
            .EffectiveResourceId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesMaterializedChangesThroughMatchingAppliers()
    {
        var matchingApplier = new RecordingMaterializedChangeApplier(
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);
        var nonMatchingApplier = new RecordingMaterializedChangeApplier(
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddSingleton<IResourceModelGraphMaterializedChangeApplier>(matchingApplier);
        services.AddSingleton<IResourceModelGraphMaterializedChangeApplier>(nonMatchingApplier);
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 30, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        var context = Assert.Single(matchingApplier.Contexts);
        Assert.Equal("application.executable:api", context.ChangeSet.Resource.EffectiveResourceId);
        Assert.Equal("developer", context.Reconciliation.CommitContext.PrincipalId);
        Assert.Equal(ResourceGraphCommitStatus.Committed, context.Reconciliation.Commit.Summary.Status);
        Assert.Empty(nonMatchingApplier.Contexts);
    }

    [Fact]
    public async Task ResourceDefinitionTemplateService_ExportsResourceTemplateFromGraph()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState(), CreateExecutableState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var templates = serviceProvider.GetRequiredService<ResourceDefinitionTemplateService>();

        var result = await templates.ExportTemplateAsync(
            "local-app",
            environmentId: "local");

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.Equal("local-app", result.Template.Name);
        Assert.Equal("local", result.Template.EnvironmentId);
        Assert.Equal(
            ["application.executable:api", "storage.volume:data"],
            result.Template.Resources
                .Select(resource => resource.EffectiveResourceId)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var executable = Assert.Single(result.Template.Resources, resource =>
            resource.EffectiveResourceId == "application.executable:api");
        Assert.Equal(
            "dotnet",
            executable.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceDefinitionTemplateService_SeedsConfigurationEntriesOnlyWhenCreatingStore()
    {
        using var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddConfigurationStoreResourceType(options =>
        {
            options.DefinitionsDirectory = directory.Path;
        });
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var templates = serviceProvider.GetRequiredService<ResourceDefinitionTemplateService>();
        var store = new ConfigurationStoreResourceDefinitionBuilder("settings")
            .WithEndpoint("http://localhost:5138")
            .WithSeed(seed => seed.Setting("Sample--Message", "Hello from template"))
            .Build();

        var create = await apply.ApplyTemplateAsync(
            new ResourceTemplate("configuration-store", [store]),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(create.HasErrors, FormatDiagnostics(create.Diagnostics));
        Assert.True(create.IsCommitted);
        var setting = Assert.Single(await serviceProvider
            .GetRequiredService<IConfigurationStoreRuntimeSettingManager>()
            .ListSettingsAsync(store.EffectiveResourceId));
        Assert.Equal("Sample--Message", setting.Name);
        Assert.Equal("Hello from template", setting.Value);

        var committed = Assert.Single(create.Commit.Snapshot!.Resources);
        Assert.False(committed.ResourceAttributeValues.ContainsKey(
            ConfigurationStoreResourceTypeProvider.Attributes.Settings));
        Assert.Equal("1", committed.ResourceAttributes[
            ConfigurationStoreResourceTypeProvider.Attributes.SettingCount]);

        var export = await templates.ExportTemplateAsync("configuration-store-export");
        Assert.False(export.HasErrors, FormatDiagnostics(export.Diagnostics));
        var exportedStore = Assert.Single(export.Template.Resources);
        Assert.False(exportedStore.ResourceAttributeValues.ContainsKey(
            ConfigurationStoreResourceTypeProvider.Attributes.Settings));
        Assert.False(exportedStore.ResourceAttributeValues.ContainsKey(
            ConfigurationStoreResourceTypeProvider.Attributes.SettingCount));

        var update = await apply.ApplyTemplateAsync(
            new ResourceTemplate("configuration-store-update", [store]),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 1, 0, TimeSpan.Zero)));

        Assert.True(update.HasErrors);
        Assert.False(update.IsCommitted);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Code == "configuration.store.settingsSeedUpdateNotAllowed");
    }

    [Fact]
    public async Task ResourceDefinitionTemplateService_SeedsSecretsOnlyWhenCreatingVault()
    {
        using var directory = new TemporaryDirectory();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddSecretsVaultResourceType(options =>
        {
            options.DefinitionsDirectory = directory.Path;
        });
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var templates = serviceProvider.GetRequiredService<ResourceDefinitionTemplateService>();
        var vault = new SecretsVaultResourceDefinitionBuilder("secrets")
            .WithEndpoint("http://localhost:6138")
            .WithSeed(seed => seed
                .Secret("Sample--ApiKey", "secret-from-template", "v1")
                .Secret("Sample--ApiKey", "rotated-secret-from-template", "v2")
                .Certificate(
                    new SecretsVaultSeedCertificate(
                        "ApiTls",
                        "certificate-from-template",
                        "v1",
                        "application/x-pem-file",
                        "ABC123",
                        "CN=api.local",
                        HasPrivateKey: true))
                .Certificate(
                    new SecretsVaultSeedCertificate(
                        "ApiTls",
                        "rotated-certificate-from-template",
                        "v2",
                        "application/x-pem-file",
                        "DEF456",
                        "CN=api.local",
                        HasPrivateKey: true)))
            .Build();

        var create = await apply.ApplyTemplateAsync(
            new ResourceTemplate("secrets-vault", [vault]),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 5, 0, TimeSpan.Zero)));

        Assert.False(create.HasErrors, FormatDiagnostics(create.Diagnostics));
        Assert.True(create.IsCommitted);
        var secrets = await serviceProvider
            .GetRequiredService<ISecretsVaultRuntimeSecretManager>()
            .ListSecretsAsync(vault.EffectiveResourceId);
        Assert.Equal(2, secrets.Count);
        Assert.Contains(secrets, secret =>
            secret.Name == "Sample--ApiKey" &&
            secret.Value == "secret-from-template" &&
            secret.Version == "v1");
        Assert.Contains(secrets, secret =>
            secret.Name == "Sample--ApiKey" &&
            secret.Value == "rotated-secret-from-template" &&
            secret.Version == "v2");
        var certificates = await serviceProvider
            .GetRequiredService<ISecretsVaultRuntimeSecretManager>()
            .ListCertificatesAsync(vault.EffectiveResourceId);
        Assert.Equal(2, certificates.Count);
        Assert.Contains(certificates, certificate =>
            certificate.Name == "ApiTls" &&
            certificate.Value == "certificate-from-template" &&
            certificate.Version == "v1" &&
            certificate.ContentType == "application/x-pem-file" &&
            certificate.Thumbprint == "ABC123");
        Assert.Contains(certificates, certificate =>
            certificate.Name == "ApiTls" &&
            certificate.Value == "rotated-certificate-from-template" &&
            certificate.Version == "v2" &&
            certificate.ContentType == "application/x-pem-file" &&
            certificate.Thumbprint == "DEF456");

        var committed = Assert.Single(create.Commit.Snapshot!.Resources);
        Assert.False(committed.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.Secrets));
        Assert.False(committed.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.Certificates));
        Assert.Equal("2", committed.ResourceAttributes[
            SecretsVaultResourceTypeProvider.Attributes.SecretCount]);
        Assert.Equal("2", committed.ResourceAttributes[
            SecretsVaultResourceTypeProvider.Attributes.CertificateCount]);

        var export = await templates.ExportTemplateAsync("secrets-vault-export");
        Assert.False(export.HasErrors, FormatDiagnostics(export.Diagnostics));
        var exportedVault = Assert.Single(export.Template.Resources);
        Assert.False(exportedVault.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.Secrets));
        Assert.False(exportedVault.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.Certificates));
        Assert.False(exportedVault.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.SecretCount));
        Assert.False(exportedVault.ResourceAttributeValues.ContainsKey(
            SecretsVaultResourceTypeProvider.Attributes.CertificateCount));

        var update = await apply.ApplyTemplateAsync(
            new ResourceTemplate("secrets-vault-update", [vault]),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 6, 0, TimeSpan.Zero)));

        Assert.True(update.HasErrors);
        Assert.False(update.IsCommitted);
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Code == "secrets.vault.secretsSeedUpdateNotAllowed");
        Assert.Contains(update.Diagnostics, diagnostic =>
            diagnostic.Code == "secrets.vault.certificatesSeedUpdateNotAllowed");
    }

    [Fact]
    public async Task ResourceDefinitionTemplateService_AppliesResourceTemplateThroughGraphApply()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var templates = serviceProvider.GetRequiredService<ResourceDefinitionTemplateService>();
        var template = new ResourceTemplate(
            "local-app",
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
                    })
            ],
            EnvironmentId: "local");

        var result = await templates.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 45, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        var created = Assert.Single(result.Apply.Commit.Snapshot!.Resources);
        Assert.Equal("application.executable:api", created.EffectiveResourceId);
        Assert.Equal("dotnet", created.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceDefinitionRegistrationService_AppliesSingleResourceDefinitionAndRegistersIt()
    {
        var resourceManager = new RecordingResourceManager();
        var registrationStore = new RecordingResourceRegistrationStore();
        var services = new ServiceCollection();
        services.AddSingleton<IResourceManager>(resourceManager);
        services.AddSingleton<IResourceRegistrationStore>(registrationStore);
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<IResourceDefinitionRegistrationService>();
        var resource = new ExecutableApplicationResourceDefinitionBuilder("api")
            .WithDisplayName("API")
            .WithExecutablePath("dotnet")
            .Build()
            .WithDeclarationAttributes(
                new ResourceIdentityBindingAttribute(
                    "identity:built-in",
                    Name: "api-service"));

        var result = await service.RegisterAsync(
            new FixedResourceDefinitionBuilder(resource),
            resourceGroupId: "apps",
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 28, 10, 0, 0, TimeSpan.Zero)));

        Assert.False(result.ApplyResult.HasErrors, FormatDiagnostics(result.ApplyResult.Diagnostics));
        Assert.True(result.ApplyResult.IsCommitted);
        Assert.True(result.Registered);
        Assert.Equal("application.executable:api", result.ResourceId);

        var state = Assert.Single(
            (await serviceProvider.GetRequiredService<ResourceGraphModel>().GetSnapshotAsync()).Resources);
        Assert.Equal("API", state.DisplayName);
        Assert.Equal("dotnet", state.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);

        var registration = Assert.Single(resourceManager.Registrations);
        Assert.Equal(ResourceModelResourceProvider.DefaultProviderId, registration.ProviderId);
        Assert.Equal(result.ResourceId, registration.ResourceId);
        Assert.Equal("apps", registration.ResourceGroupId);
        var persistedIdentity = registrationStore.GetRegistration(result.ResourceId)?.IdentityBinding;
        Assert.NotNull(persistedIdentity);
        Assert.Equal("identity:built-in", persistedIdentity.ProviderId);
        Assert.Equal("api-service", persistedIdentity.Name);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDefinitionChangesToExistingDeploymentResource()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var initialDefinition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: "API",
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            });
        var template = new ResourceTemplate(
            "local-app",
            [initialDefinition],
            EnvironmentId: "local");

        var created = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 30, 0, TimeSpan.Zero)));
        var changedDefinition = initialDefinition with
        {
            DisplayName = "API v2",
            Attributes = new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
            }
        };

        var changed = await service.ApplyDefinitionsAsync(
            [changedDefinition],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 16, 45, 0, TimeSpan.Zero)));

        Assert.False(created.HasErrors, FormatDiagnostics(created.Diagnostics));
        Assert.True(created.IsCommitted);
        Assert.False(changed.HasErrors, FormatDiagnostics(changed.Diagnostics));
        Assert.True(changed.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), changed.BaseVersion);
        Assert.Equal(new ResourceGraphVersion(2), changed.Commit.Version);
        Assert.False(Assert.Single(changed.Changes.AcceptedResources).ChangeSet.IsNewResource);
        Assert.Equal(ResourceGraphCommitStatus.Committed, changed.Commit.Summary.Status);
        Assert.Equal(1, changed.Commit.Summary.AttributeChangeCount);

        var committed = Assert.Single(changed.Commit.Snapshot!.Resources);
        Assert.Equal("application.executable:api", committed.EffectiveResourceId);
        Assert.Equal("API v2", committed.DisplayName);
        Assert.Equal(new ResourceRevision(2), committed.Revision);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, committed.ProviderId);
        Assert.Equal("dotnet-watch", committed.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_LeavesApplyPolicyToProviderOrControlPlane()
    {
        var provider = new RuntimePolicyResourceTypeProvider();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph(
        [
            new(
                "api",
                RuntimePolicyResourceTypeProvider.ResourceTypeId,
                Attributes: new Dictionary<ResourceAttributeId, string>
                {
                    [RuntimePolicyResourceTypeProvider.Attributes.Value] = "v1"
                })
        ]);
        services.AddSingleton(RuntimePolicyResourceTypeProvider.ClassDefinition);
        services.AddSingleton<IResourceTypeProvider>(provider);
        services.AddSingleton<IResourceChangeApplyProvider>(provider);
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var changedDefinition = new ResourceDefinition(
            "api",
            RuntimePolicyResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [RuntimePolicyResourceTypeProvider.Attributes.Value] = "v2"
            });

        provider.IsRunning = true;
        var rejected = await service.ApplyDefinitionsAsync(
            [changedDefinition],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 22, 0, 0, TimeSpan.Zero)));

        Assert.True(rejected.HasErrors);
        Assert.False(rejected.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, rejected.Commit.Summary.Status);
        Assert.Equal("policy.changeRequiresStoppedResource", Assert.Single(rejected.Diagnostics).Code);

        var graphModel = serviceProvider.GetRequiredService<ResourceGraphModel>();
        var rejectedSnapshot = await graphModel.GetSnapshotAsync();
        var unchanged = Assert.Single(rejectedSnapshot.Resources);
        Assert.Equal(ResourceGraphVersion.Initial, rejectedSnapshot.Version);
        Assert.Equal("v1", unchanged.ResourceAttributes[RuntimePolicyResourceTypeProvider.Attributes.Value]);

        provider.AcceptRunningChangesWithRestart = true;
        var accepted = await service.ApplyDefinitionsAsync(
            [changedDefinition],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 22, 5, 0, TimeSpan.Zero)));

        Assert.False(accepted.HasErrors, FormatDiagnostics(accepted.Diagnostics));
        Assert.True(accepted.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, accepted.Commit.Summary.Status);
        Assert.Equal(new ResourceGraphVersion(1), accepted.Commit.Version);
        var warning = Assert.Single(accepted.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("policy.restartRequired", warning.Code);

        var committed = Assert.Single(accepted.Commit.Snapshot!.Resources);
        Assert.Equal("v2", committed.ResourceAttributes[RuntimePolicyResourceTypeProvider.Attributes.Value]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_CanCommitThroughCustomResourceManagerRecordProjector()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            new ResourceManagerResourceRowProjector(),
            [
                new(
                    "application.executable:api",
                    ResourceDefinitionJson.FromValue(
                        ResourceRecord.FromState(CreateExecutableState(includeVolumeConsumer: false))),
                    OperationalState: "Running")
            ]);
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var stateProvider = Assert.IsType<InMemoryProjectedResourceStateProvider<ResourceManagerResourceRow>>(
            serviceProvider.GetRequiredService<IResourceStateProvider>());

        var result = await service.ApplyDefinitionsAsync(
            [
                new(
                    "api",
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet-watch"
                    })
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero)));

        var row = Assert.Single(stateProvider.GetRecords());
        var committedState = row.GraphData.Deserialize<ResourceRecord>()!.ToState();
        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal("Running", row.OperationalState);
        Assert.Equal("dotnet-watch", committedState.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal(new ResourceRevision(1), committedState.Revision);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 24, 18, 0, 0, TimeSpan.Zero),
            committedState.LastModifiedAt);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDeploymentAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var volume = graph.AddLocalVolume("data");
        var executable = graph
            .AddExecutableApplication("api")
            .WithExecutablePath("dotnet")
            .MountVolume(volume, "App_Data");
        var template = graph.BuildTemplate("local-app", environmentId: "local");

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 17, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors);
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Equal(2, result.Commit.Summary.AcceptedResourceCount);
        Assert.Equal(2, result.Commit.Snapshot!.Resources.Count);
        Assert.All(result.Changes.AcceptedResources, resource =>
            Assert.True(resource.ChangeSet.IsNewResource));

        var provider = Assert.Single(serviceProvider.GetServices<IResourceProvider>());
        var projectedResources = provider.GetResources();
        var projectedVolume = Assert.Single(projectedResources, resource =>
            resource.Id == volume.EffectiveResourceId);
        var projectedExecutable = Assert.Single(projectedResources, resource =>
            resource.Id == executable.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Storage, projectedVolume.ResourceClass);
        Assert.Equal(LocalVolumeResourceTypeProvider.ProviderId, projectedVolume.Provider);
        Assert.Equal("volume", projectedVolume.ResourceAttributes["storage.kind"]);
        Assert.Equal("local", projectedVolume.ResourceAttributes["storage.medium"]);
        Assert.Contains(projectedVolume.ResourceActions, action =>
            action.Id == LocalVolumeResourceTypeProvider.Operations.Provision.ToString());
        Assert.Equal(ResourceManagerClass.Executable, projectedExecutable.ResourceClass);
        Assert.Equal([volume.EffectiveResourceId], projectedExecutable.DependsOn);
        Assert.Contains(projectedExecutable.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(executable.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [executable.EffectiveResourceId, volume.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var volumeCapability = Assert.IsType<VolumeConsumerCapability>(
            resolution.Target!.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(volumeCapability.Mounts).Volume);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesContainerApplicationAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddContainerHostResourceType();
        services.AddNetworkResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(
            new StaticContainerApplicationRuntimeHandler(ContainerApplicationRuntimeStatus.Running));
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        graph.Add(volume);
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2)
            .WithHttpEndpoint(
                targetPort: 8080,
                host: "localhost",
                port: 5092)
            .MountVolume(volume.EffectiveResourceId, "/data");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 19, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources();
        var projectedContainer = Assert.Single(projectedResources, resource =>
            resource.Id == container.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Container, projectedContainer.ResourceClass);
        Assert.Equal(ContainerApplicationResourceTypeProvider.ProviderId, projectedContainer.Provider);
        Assert.Equal(ResourceManagerResourceState.Running, projectedContainer.State);
        Assert.Equal("ghcr.io/example/api:latest", projectedContainer.ResourceAttributes["container.image"]);
        Assert.Equal("2", projectedContainer.ResourceAttributes["container.replicas"]);
        Assert.Equal("http://localhost:5092", projectedContainer.PrimaryEndpoint);
        Assert.Equal(8080, Assert.Single(projectedContainer.Endpoints).TargetPort);
        Assert.Equal([host.EffectiveResourceId, volume.EffectiveResourceId], projectedContainer.DependsOn);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.EndpointSource);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.Monitoring);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.LogSources);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projectedContainer.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.Start.ToString());
        var stop = Assert.Single(projectedContainer.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.Stop.ToString());
        var restart = Assert.Single(projectedContainer.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.Restart.ToString());
        var deploymentProvider = Assert.IsAssignableFrom<IResourceOrchestratorDeploymentProvider>(provider);
        var deployment = await deploymentProvider.DescribeDeploymentAsync(
            new ResourceProcedureContext(
                projectedContainer,
                null,
                null,
                new EmptyResourceRegistrationStore()));

        Assert.NotNull(deployment);
        Assert.Equal("default", deployment.OrchestratorId);
        Assert.Equal(container.EffectiveResourceId, deployment.SourceResourceId);
        Assert.Equal("cloudshell-application-container-app-api", deployment.ServiceId);
        Assert.Equal("ghcr.io/example/api:latest", deployment.Spec.Service.Workload.Image);
        Assert.Equal(2, deployment.Spec.Service.Workload.Replicas);
        Assert.True(deployment.Spec.Service.Workload.ReplicasEnabled);
        Assert.Equal(host.EffectiveResourceId, deployment.Spec.Service.Workload.ContainerHostId);
        var initialRuntimeRevisionId = deployment.RevisionId;
        Assert.StartsWith("rev-img-", initialRuntimeRevisionId, StringComparison.Ordinal);
        Assert.NotNull(deployment.Spec.Definition);
        var deploymentService = Assert.Single(deployment.Spec.Definition.DeploymentServices);
        var replicaGroupDefinition = Assert.Single(deploymentService.ReplicaGroupDefinitions);
        Assert.Equal(
            $"cloudshell-application-container-app-api-{initialRuntimeRevisionId}-replicas",
            replicaGroupDefinition.Name);
        Assert.Equal(initialRuntimeRevisionId, replicaGroupDefinition.RuntimeRevisionId);
        Assert.Equal(2, replicaGroupDefinition.RequestedReplicaSlots);
        Assert.Equal(2, replicaGroupDefinition.RequestedReplicas);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(container.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [container.EffectiveResourceId, host.EffectiveResourceId, volume.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var volumeCapability = Assert.IsType<VolumeConsumerCapability>(
            resolution.Target!.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(volumeCapability.Mounts).Volume);

        var procedure = new ResourceProcedureContext(
            projectedContainer,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, restart));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, stop));

        var stopResult = await provider.ExecuteActionAsync(procedure, stop);
        var restartResult = await provider.ExecuteActionAsync(procedure, restart);

        Assert.Equal("Executed Stop for api.", stopResult.Message);
        Assert.Equal("Executed Restart for api.", restartResult.Message);

        Assert.True(provider.CanUpdateImage(projectedContainer));
        var imageUpdate = await provider.UpdateImageAsync(
            procedure,
            "ghcr.io/example/api:v2",
            restartIfRunning: false,
            triggeredBy: "test",
            requestedReplicas: 4);

        Assert.Equal("Updated image for api to 'ghcr.io/example/api:v2'.", imageUpdate.Message);
        Assert.True(imageUpdate.RuntimeReconciliationRequired);
        Assert.Equal(container.EffectiveResourceId, imageUpdate.RuntimeReconciliationResourceId);

        var updatedContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);
        Assert.Equal("ghcr.io/example/api:v2", updatedContainer.ResourceAttributes["container.image"]);
        Assert.Equal("4", updatedContainer.ResourceAttributes["container.replicas"]);
        var imageDeployment = await deploymentProvider.DescribeDeploymentAsync(
            new ResourceProcedureContext(
                updatedContainer,
                null,
                null,
                new EmptyResourceRegistrationStore()));

        Assert.NotNull(imageDeployment);
        Assert.NotEqual(initialRuntimeRevisionId, imageDeployment.RevisionId);
        var imageReplicaGroupDefinition = Assert.Single(
            Assert.Single(imageDeployment.Spec.Definition!.DeploymentServices).ReplicaGroupDefinitions);
        Assert.Equal(imageDeployment.RevisionId, imageReplicaGroupDefinition.RuntimeRevisionId);
        Assert.Equal(4, imageReplicaGroupDefinition.RequestedReplicaSlots);

        Assert.True(provider.CanUpdateReplicas(updatedContainer));
        var replicaUpdate = await provider.UpdateReplicasAsync(
            procedure,
            5,
            restartIfRunning: false,
            triggeredBy: "test");

        Assert.Equal("Updated replicas for api to '5'.", replicaUpdate.Message);
        Assert.True(replicaUpdate.RuntimeReconciliationRequired);
        Assert.Equal(container.EffectiveResourceId, replicaUpdate.RuntimeReconciliationResourceId);

        var scaledContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);
        Assert.Equal("ghcr.io/example/api:v2", scaledContainer.ResourceAttributes["container.image"]);
        Assert.Equal("5", scaledContainer.ResourceAttributes["container.replicas"]);
        var scaleDeployment = await deploymentProvider.DescribeDeploymentAsync(
            new ResourceProcedureContext(
                scaledContainer,
                null,
                null,
                new EmptyResourceRegistrationStore()));

        Assert.NotNull(scaleDeployment);
        Assert.Equal(imageDeployment.RevisionId, scaleDeployment.RevisionId);
        var scaleReplicaGroupDefinition = Assert.Single(
            Assert.Single(scaleDeployment.Spec.Definition!.DeploymentServices).ReplicaGroupDefinitions);
        Assert.Equal(imageReplicaGroupDefinition.Name, scaleReplicaGroupDefinition.Name);
        Assert.Equal(imageDeployment.RevisionId, scaleReplicaGroupDefinition.RuntimeRevisionId);
        Assert.Equal(5, scaleReplicaGroupDefinition.RequestedReplicaSlots);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_ReconcilesContainerApplicationRuntimeFromTemplateChanges()
    {
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler(
            ContainerApplicationRuntimeStatus.Running);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        var deploymentCoordinator = new RecordingResourceOrchestratorDeploymentCoordinator();
        services.AddSingleton<IResourceOrchestratorDeploymentCoordinator>(deploymentCoordinator);
        services.AddScoped<IResourceManagerStore>(serviceProvider =>
            new ProjectedResourceManagerStore(() =>
                serviceProvider.GetServices<IResourceProvider>().ToArray()));
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2);

        var initialApply = await service.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(PrincipalId: "developer"));

        Assert.False(initialApply.HasErrors, FormatDiagnostics(initialApply.Diagnostics));
        Assert.True(initialApply.IsCommitted);
        Assert.Empty(runtimeHandler.Events);
        Assert.Empty(deploymentCoordinator.Deployments);

        var imageApply = await service.ApplyDefinitionsAsync(
            [
                new ResourceDefinition(
                    container.Name,
                    ContainerApplicationResourceTypeProvider.ResourceTypeId,
                    ResourceId: container.EffectiveResourceId,
                    Attributes: new ResourceAttributeValueMap(
                        new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                                "ghcr.io/example/api:v2",
                            [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = 4
                        }))
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(imageApply.HasErrors, FormatDiagnostics(imageApply.Diagnostics));
        Assert.Empty(runtimeHandler.Events);
        var imageDeployment = Assert.Single(deploymentCoordinator.Deployments);
        Assert.Equal(container.EffectiveResourceId, imageDeployment.SourceResourceId);
        Assert.Equal("ghcr.io/example/api:v2", imageDeployment.Spec.Service.Workload.Image);
        Assert.Equal(4, imageDeployment.Spec.Service.Workload.Replicas);
        Assert.StartsWith("rev-img-", imageDeployment.RevisionId, StringComparison.Ordinal);

        var replicaApply = await service.ApplyDefinitionsAsync(
            [
                new ResourceDefinition(
                    container.Name,
                    ContainerApplicationResourceTypeProvider.ResourceTypeId,
                    ResourceId: container.EffectiveResourceId,
                    Attributes: new ResourceAttributeValueMap(
                        new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = 5
                        }))
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(replicaApply.HasErrors, FormatDiagnostics(replicaApply.Diagnostics));
        Assert.Empty(runtimeHandler.Events);
        Assert.Collection(
            deploymentCoordinator.Deployments,
            first => Assert.Equal(imageDeployment.RevisionId, first.RevisionId),
            second =>
            {
                Assert.Equal(imageDeployment.RevisionId, second.RevisionId);
                Assert.Equal("ghcr.io/example/api:v2", second.Spec.Service.Workload.Image);
                Assert.Equal(5, second.Spec.Service.Workload.Replicas);
                var replicaGroup = Assert.Single(
                    Assert.Single(second.Spec.Definition!.DeploymentServices).ReplicaGroupDefinitions);
                Assert.Equal(
                    Assert.Single(
                        Assert.Single(imageDeployment.Spec.Definition!.DeploymentServices).ReplicaGroupDefinitions).Name,
                    replicaGroup.Name);
                Assert.Equal(5, replicaGroup.RequestedReplicaSlots);
            });
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_TearsDownRetiredReplicaGroupsAfterDeploymentReconciliation()
    {
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler(
            ContainerApplicationRuntimeStatus.Running);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        var deploymentCoordinator = new RecordingResourceOrchestratorDeploymentCoordinator(
            retirePreviousReplicaGroup: true);
        var tearDownOrchestrator = new RecordingReplicaGroupTearDownOrchestrator();
        services.AddSingleton<IResourceOrchestratorDeploymentCoordinator>(deploymentCoordinator);
        services.AddSingleton<IResourceOrchestratorDeploymentCleanupCoordinator>(
            new TestResourceOrchestratorDeploymentCleanupCoordinator());
        services.AddSingleton<IResourceOrchestrator>(tearDownOrchestrator);
        services.AddScoped<IResourceManagerStore>(serviceProvider =>
            new ProjectedResourceManagerStore(() =>
                serviceProvider.GetServices<IResourceProvider>().ToArray()));
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2);

        var initialApply = await service.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(PrincipalId: "developer"));

        Assert.False(initialApply.HasErrors, FormatDiagnostics(initialApply.Diagnostics));
        Assert.Empty(deploymentCoordinator.Deployments);
        Assert.Empty(tearDownOrchestrator.TornDownReplicaGroups);

        var imageApply = await service.ApplyDefinitionsAsync(
            [
                new ResourceDefinition(
                    container.Name,
                    ContainerApplicationResourceTypeProvider.ResourceTypeId,
                    ResourceId: container.EffectiveResourceId,
                    Attributes: new ResourceAttributeValueMap(
                        new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                        {
                            [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                                "ghcr.io/example/api:v2"
                        }))
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(imageApply.HasErrors, FormatDiagnostics(imageApply.Diagnostics));
        Assert.Empty(imageApply.Diagnostics);
        Assert.Single(deploymentCoordinator.Deployments);
        var tearDown = Assert.Single(tearDownOrchestrator.TornDownReplicaGroups);
        Assert.Contains("previous-revision-replicas", tearDown, StringComparison.Ordinal);
        Assert.Contains(":developer:", tearDown, StringComparison.Ordinal);
        Assert.Contains(
            "Retire previous graph apply revision.",
            tearDown,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ExecutesContainerApplicationOrchestratorService()
    {
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler(
            ContainerApplicationRuntimeStatus.Running);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2);
        var result = await apply.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.Empty(runtimeHandler.Events);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);
        var serviceProviderBridge = Assert.IsAssignableFrom<IResourceOrchestratorServiceProcedureProvider>(provider);
        var procedure = new ResourceProcedureContext(
            projectedContainer,
            null,
            null,
            new EmptyResourceRegistrationStore());
        var service = await serviceProviderBridge.CreateOrchestratorServiceAsync(procedure);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");
        var routingBindings = new[]
        {
            new ResourceOrchestratorServiceRoutingBindingDefinition(
                "api-public-http-routing",
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                service.Name,
                replicaGroup.Id,
                ResourceEndpointReference.ForEndpoint(projectedContainer.Id, "http"),
                LoadBalancerResourceId: "cloudshell.loadBalancer:public",
                RouteId: "cloudshell.loadBalancer:public/routes/api")
        };
        var previousReplicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service with
            {
                Workload = service.Workload with
                {
                    Replicas = 3,
                    ReplicasEnabled = true
                }
            },
            "rev-previous");

        Assert.True(serviceProviderBridge.CanExecuteOrchestratorService(
            projectedContainer,
            ResourceAction.Start));

        await serviceProviderBridge.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                procedure,
                service,
                replicaGroup,
                routingBindings),
            ResourceAction.Start);
        foreach (var instance in replicaGroup.Instances)
        {
            await serviceProviderBridge.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(procedure, service, instance, replicaGroup),
                ResourceAction.Start);
        }
        foreach (var instance in previousReplicaGroup.Instances)
        {
            await serviceProviderBridge.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(procedure, service, instance, previousReplicaGroup),
                ResourceAction.Stop);
        }

        Assert.Collection(
            runtimeHandler.Events,
            first =>
            {
                Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.UpdateImage, first.OperationId);
                Assert.Equal("ghcr.io/example/api:latest", first.Image);
                Assert.Equal(2, first.Replicas);
            },
            second =>
            {
                Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas, second.OperationId);
                Assert.Equal("ghcr.io/example/api:latest", second.Image);
                Assert.Equal(2, second.Replicas);
            });
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_UsesContainerApplicationOrchestratorRuntimePrimitives()
    {
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler(
            ContainerApplicationRuntimeStatus.Running);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddSingleton<IContainerApplicationOrchestratorRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2);
        var result = await apply.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);
        var serviceProviderBridge = Assert.IsAssignableFrom<IResourceOrchestratorServiceProcedureProvider>(provider);
        var procedure = new ResourceProcedureContext(
            projectedContainer,
            null,
            null,
            new EmptyResourceRegistrationStore());
        var service = await serviceProviderBridge.CreateOrchestratorServiceAsync(procedure);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");
        var routingBindings = new[]
        {
            new ResourceOrchestratorServiceRoutingBindingDefinition(
                "api-public-http-routing",
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                service.Name,
                replicaGroup.Id,
                ResourceEndpointReference.ForEndpoint(projectedContainer.Id, "http"),
                LoadBalancerResourceId: "cloudshell.loadBalancer:public",
                RouteId: "cloudshell.loadBalancer:public/routes/api")
        };
        var previousReplicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service with
            {
                Workload = service.Workload with
                {
                    Replicas = 3,
                    ReplicasEnabled = true
                }
            },
            "rev-previous");

        await serviceProviderBridge.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                procedure,
                service,
                replicaGroup,
                routingBindings),
            ResourceAction.Start);
        foreach (var instance in replicaGroup.Instances)
        {
            await serviceProviderBridge.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(procedure, service, instance, replicaGroup),
                ResourceAction.Start);
        }

        await serviceProviderBridge.ReconcileOrchestratorServiceRoutingAsync(
            new ResourceOrchestratorServiceProcedureContext(
                procedure,
                service,
                replicaGroup,
                routingBindings));
        foreach (var instance in previousReplicaGroup.Instances)
        {
            await serviceProviderBridge.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(procedure, service, instance, previousReplicaGroup),
                ResourceAction.Stop);
        }

        Assert.Empty(runtimeHandler.Events);
        Assert.Collection(
            runtimeHandler.OrchestratorEvents,
            first =>
            {
                Assert.Equal("prepare", first.Stage);
                Assert.Equal(1, first.RoutingBindingCount);
                Assert.Equal("cloudshell.loadBalancer:public/routes/api", first.RoutingRouteId);
            },
            second =>
            {
                Assert.Equal("instance", second.Stage);
                Assert.Equal(ResourceActionKind.Start, second.ActionKind);
                Assert.Equal(1, second.ReplicaOrdinal);
            },
            third =>
            {
                Assert.Equal("instance", third.Stage);
                Assert.Equal(ResourceActionKind.Start, third.ActionKind);
                Assert.Equal(2, third.ReplicaOrdinal);
            },
            fourth =>
            {
                Assert.Equal("routing", fourth.Stage);
                Assert.Equal(1, fourth.RoutingBindingCount);
                Assert.Equal("cloudshell.loadBalancer:public/routes/api", fourth.RoutingRouteId);
            },
            fifth =>
            {
                Assert.Equal("instance", fifth.Stage);
                Assert.Equal(ResourceActionKind.Stop, fifth.ActionKind);
                Assert.Equal(1, fifth.ReplicaOrdinal);
            },
            sixth =>
            {
                Assert.Equal("instance", sixth.Stage);
                Assert.Equal(ResourceActionKind.Stop, sixth.ActionKind);
                Assert.Equal(2, sixth.ReplicaOrdinal);
            },
            seventh =>
            {
                Assert.Equal("instance", seventh.Stage);
                Assert.Equal(ResourceActionKind.Stop, seventh.ActionKind);
                Assert.Equal(3, seventh.ReplicaOrdinal);
            });
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_StartsStoppedContainerApplicationService()
    {
        var runtimeHandler = new RecordingContainerApplicationRuntimeHandler(
            ContainerApplicationRuntimeStatus.Stopped);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2);
        var result = await apply.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);
        var serviceProviderBridge = Assert.IsAssignableFrom<IResourceOrchestratorServiceProcedureProvider>(provider);
        var procedure = new ResourceProcedureContext(
            projectedContainer,
            null,
            null,
            new EmptyResourceRegistrationStore());
        var service = await serviceProviderBridge.CreateOrchestratorServiceAsync(procedure);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
            service,
            "rev-test");

        await serviceProviderBridge.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(procedure, service, replicaGroup),
            ResourceAction.Start);

        var lifecycle = Assert.Single(runtimeHandler.Events);
        Assert.Equal(ResourceActionIds.Start, lifecycle.OperationId.ToString());
        Assert.Equal("ghcr.io/example/api:latest", lifecycle.Image);
        Assert.Equal(2, lifecycle.Replicas);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AcceptsDockerHostForContainerBackedWorkloads()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddSqlServerResourceType();
        services.AddRabbitMQResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph.AddDockerHost("engine");
        var container = graph
            .AddContainerApplication("api")
            .UseDockerHost(host)
            .WithImage("example/api:1.0");
        var sqlServer = graph
            .AddSqlServer("sql")
            .UseContainerHost(host, DockerHostResourceTypeProvider.ResourceTypeId);
        var rabbitMQ = graph
            .AddRabbitMQ("rabbitmq")
            .UseContainerHost(host, DockerHostResourceTypeProvider.ResourceTypeId);

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("docker-host-workloads", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 20, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedContainer = Assert.Single(projectedResources, resource =>
            resource.Id == container.EffectiveResourceId);
        var projectedSqlServer = Assert.Single(projectedResources, resource =>
            resource.Id == sqlServer.EffectiveResourceId);
        var projectedRabbitMQ = Assert.Single(projectedResources, resource =>
            resource.Id == rabbitMQ.EffectiveResourceId);

        Assert.Equal([host.EffectiveResourceId], projectedContainer.DependsOn);
        Assert.Equal([host.EffectiveResourceId], projectedSqlServer.DependsOn);
        Assert.Equal([host.EffectiveResourceId], projectedRabbitMQ.DependsOn);

        var resolver = serviceProvider.GetRequiredService<ResourceModelGraphResourceResolver>();
        var containerResolution = await resolver.ResolveAsync(container.EffectiveResourceId);
        var sqlResolution = await resolver.ResolveAsync(sqlServer.EffectiveResourceId);
        var rabbitMQResolution = await resolver.ResolveAsync(rabbitMQ.EffectiveResourceId);

        Assert.False(containerResolution.HasErrors, FormatDiagnostics(containerResolution.Diagnostics));
        Assert.False(sqlResolution.HasErrors, FormatDiagnostics(sqlResolution.Diagnostics));
        Assert.False(rabbitMQResolution.HasErrors, FormatDiagnostics(rabbitMQResolution.Diagnostics));

        var projectionResolver = serviceProvider.GetRequiredService<ResourceProjectionResolver>();
        var containerProjection = Assert.IsType<ContainerApplicationResource>(
            await projectionResolver.GetResourceProjectionAsync(
                containerResolution.Target!,
                new ResourceProjectionContext("local", "developer")));
        var sqlProjection = Assert.IsType<SqlServerResource>(
            await projectionResolver.GetResourceProjectionAsync(
                sqlResolution.Target!,
                new ResourceProjectionContext("local", "developer")));
        var rabbitMQProjection = Assert.IsType<RabbitMQResource>(
            await projectionResolver.GetResourceProjectionAsync(
                rabbitMQResolution.Target!,
                new ResourceProjectionContext("local", "developer")));

        Assert.Equal(host.EffectiveResourceId, containerProjection.ContainerHostResourceId);
        Assert.Equal(host.EffectiveResourceId, sqlProjection.ContainerHostResourceId);
        Assert.Equal(host.EffectiveResourceId, rabbitMQProjection.ContainerHostResourceId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesContainerAppDeploymentSampleGraph()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerHostResourceType();
        services.AddDockerContainerResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        const string registryAddress = "localhost:5023";
        var host = new ResourceDefinition(
            "sample",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerHostResourceTypeProvider.Attributes.Registry] = registryAddress
            });
        var registry = new ResourceDefinition(
            "sample-registry",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerContainerResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] = "registry:2",
                [DockerContainerResourceTypeProvider.Attributes.ContainerRegistry] = "docker.io"
            });
        var app = new ResourceDefinition(
            "sample-api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    registry.EffectiveResourceId,
                    typeId: DockerContainerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "cloudshell/mock-api:20260608.1",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry] =
                    registryAddress
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "container-app-deployment",
                [host, registry, app],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 21, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedRegistry = Assert.Single(projectedResources, resource =>
            resource.Id == registry.EffectiveResourceId);
        var projectedApp = Assert.Single(projectedResources, resource =>
            resource.Id == app.EffectiveResourceId);

        Assert.Equal([host.EffectiveResourceId], projectedRegistry.DependsOn);
        Assert.Equal([host.EffectiveResourceId, registry.EffectiveResourceId], projectedApp.DependsOn);
        Assert.Equal(registryAddress, projectedApp.ResourceAttributes["container.registry"]);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(app.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Contains(app.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(registry.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(host.EffectiveResourceId, resolvedResourceIds);

        var appProjection = Assert.IsType<ContainerApplicationResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));

        Assert.Equal("cloudshell/mock-api:20260608.1", appProjection.Image);
        Assert.Equal(registryAddress, appProjection.Registry);
        Assert.Equal(host.EffectiveResourceId, appProjection.ContainerHostResourceId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesJavaScriptAppAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddJavaScriptAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var settings = graph
            .AddConfigurationStore("settings")
            .WithEndpoint("http://localhost:5101");

        var app = graph
            .AddJavaScriptApp("frontend", "src/frontend")
            .WithPackageManager("npm")
            .WithScript("dev")
            .WithReference(settings)
            .WithHttpEndpoint(
                host: "localhost",
                port: 5173,
                targetPort: 5173)
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                name: "alive",
                interval: TimeSpan.FromSeconds(10));

        var template = graph.BuildTemplate("javascript-app", environmentId: "local");
        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == app.EffectiveResourceId);

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedApp = Assert.Single(provider.GetResources(), resource =>
            resource.Id == app.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Project, projectedApp.ResourceClass);
        Assert.Equal(ResourceManagerResourceState.Stopped, projectedApp.State);
        Assert.Equal(JavaScriptAppResourceTypeProvider.ProviderId, projectedApp.Provider);
        Assert.Equal("src/frontend", projectedApp.ResourceAttributes["project.path"]);
        Assert.Equal("node", projectedApp.ResourceAttributes["javascript.engine"]);
        Assert.Equal("npm", projectedApp.ResourceAttributes["javascript.packageManager"]);
        Assert.Equal("dev", projectedApp.ResourceAttributes["javascript.script"]);
        Assert.Empty(projectedApp.DependsOn);
        Assert.Equal("http://localhost:5173", projectedApp.PrimaryEndpoint);
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.EndpointSource.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.Monitoring.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        var healthCheck = Assert.Single(projectedApp.ResourceHealthChecks);
        Assert.Equal("alive", healthCheck.Name);
        Assert.Equal(ResourceProbeType.Liveness, healthCheck.Type);
        Assert.Equal("/alive", healthCheck.Path);
        Assert.Equal("http", healthCheck.EndpointName);
        var logSource = Assert.Single(projectedApp.ResourceLogSources);
        Assert.Equal("console", logSource.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, logSource.Kind);
        Assert.True(projectedApp.SupportsLogSources);
        var reference = Assert.Single(
            appDefinition.ResourceAttributeValues.GetObject<ResourceReference[]>(
                JavaScriptAppResourceTypeProvider.Attributes.References) ?? []);
        Assert.Equal(settings.EffectiveResourceId, reference.Value);
        var start = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaScriptAppResourceTypeProvider.Operations.Start.ToString());
        var stop = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaScriptAppResourceTypeProvider.Operations.Stop.ToString());
        var restart = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaScriptAppResourceTypeProvider.Operations.Restart.ToString());

        var procedure = new ResourceProcedureContext(
            projectedApp,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, start));
        Assert.NotNull(await provider.GetActionUnavailableReasonAsync(procedure, stop));
        Assert.NotNull(await provider.GetActionUnavailableReasonAsync(procedure, restart));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_ProjectsJavaScriptAppAsContainerBuild()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddContainerApplicationResourceType();
        services.AddJavaScriptAppResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(
            new StaticContainerApplicationRuntimeHandler(ContainerApplicationRuntimeStatus.Stopped));
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();

        var app = graph
            .AddJavaScriptApp("frontend", "samples/JavaScriptApp/App")
            .AsContainerApp(tag: "dev", dockerfile: "Dockerfile")
            .WithReplicas(3)
            .WithHttpEndpoint(
                host: "localhost",
                port: 5173,
                targetPort: 8080);

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("javascript-container-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedApp = Assert.Single(provider.GetResources(), resource =>
            resource.Id == app.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Container, projectedApp.ResourceClass);
        Assert.Equal(ResourceManagerResourceState.Stopped, projectedApp.State);
        Assert.Equal(ContainerApplicationResourceTypeProvider.ProviderId, projectedApp.Provider);
        Assert.Equal("cloudshell-javascript-frontend:dev", projectedApp.ResourceAttributes["container.image"]);
        Assert.Equal("samples/JavaScriptApp/App", projectedApp.ResourceAttributes["container.buildContext"]);
        Assert.Equal("Dockerfile", projectedApp.ResourceAttributes["container.dockerfile"]);
        Assert.Equal("3", projectedApp.ResourceAttributes["container.replicas"]);
        Assert.Equal("http://localhost:5173", projectedApp.PrimaryEndpoint);
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.EndpointSource.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.Monitoring.ToString());
        Assert.Contains(projectedApp.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas.ToString());

        var deploymentProvider = Assert.IsAssignableFrom<IResourceOrchestratorDeploymentProvider>(provider);
        var deployment = await deploymentProvider.DescribeDeploymentAsync(
            new ResourceProcedureContext(
                projectedApp,
                null,
                null,
                new EmptyResourceRegistrationStore()));

        Assert.NotNull(deployment);
        Assert.Equal(ResourceWorkloadKind.ContainerBuild, deployment.Spec.Service.Workload.Kind);
        Assert.Equal("cloudshell-javascript-frontend:dev", deployment.Spec.Service.Workload.Image);
        Assert.Equal("samples/JavaScriptApp/App", deployment.Spec.Service.Workload.BuildContext);
        Assert.Equal("Dockerfile", deployment.Spec.Service.Workload.Dockerfile);
        Assert.Equal("samples/JavaScriptApp/App", deployment.Spec.Service.Workload.ProjectPath);
        Assert.Equal(3, deployment.Spec.Service.Workload.Replicas);
        Assert.True(deployment.Spec.Service.Workload.ReplicasEnabled);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesJavaAppAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddJavaAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var settings = graph
            .AddConfigurationStore("settings")
            .WithEndpoint("http://localhost:5101");

        var app = graph
            .AddJavaApp("api", "src/api", "target/app.jar")
            .WithJvmArguments("-Xmx256m")
            .WithReference(settings)
            .WithHttpEndpoint(
                host: "localhost",
                port: 5185,
                targetPort: 5185)
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                name: "alive",
                interval: TimeSpan.FromSeconds(10));

        var template = graph.BuildTemplate("java-app", environmentId: "local");
        var appDefinition = Assert.Single(template.Resources, resource =>
            resource.EffectiveResourceId == app.EffectiveResourceId);

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedApp = Assert.Single(provider.GetResources(), resource =>
            resource.Id == app.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Project, projectedApp.ResourceClass);
        Assert.Equal(ResourceManagerResourceState.Stopped, projectedApp.State);
        Assert.Equal(JavaAppResourceTypeProvider.ProviderId, projectedApp.Provider);
        Assert.Equal("src/api", projectedApp.ResourceAttributes["project.path"]);
        Assert.Equal("java", projectedApp.ResourceAttributes["java.command"]);
        Assert.Equal("target/app.jar", projectedApp.ResourceAttributes["java.artifactPath"]);
        Assert.Equal("-Xmx256m", projectedApp.ResourceAttributes["java.jvmArguments"]);
        Assert.Empty(projectedApp.DependsOn);
        Assert.Equal("http://localhost:5185", projectedApp.PrimaryEndpoint);
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.EndpointSource.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.Monitoring.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedApp.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        var healthCheck = Assert.Single(projectedApp.ResourceHealthChecks);
        Assert.Equal("alive", healthCheck.Name);
        Assert.Equal(ResourceProbeType.Liveness, healthCheck.Type);
        Assert.Equal("/alive", healthCheck.Path);
        Assert.Equal("http", healthCheck.EndpointName);
        var logSource = Assert.Single(projectedApp.ResourceLogSources);
        Assert.Equal("console", logSource.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, logSource.Kind);
        Assert.True(projectedApp.SupportsLogSources);
        var reference = Assert.Single(
            appDefinition.ResourceAttributeValues.GetObject<ResourceReference[]>(
                JavaAppResourceTypeProvider.Attributes.References) ?? []);
        Assert.Equal(settings.EffectiveResourceId, reference.Value);
        var start = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaAppResourceTypeProvider.Operations.Start.ToString());
        var stop = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaAppResourceTypeProvider.Operations.Stop.ToString());
        var restart = Assert.Single(projectedApp.ResourceActions, action =>
            action.Id == JavaAppResourceTypeProvider.Operations.Restart.ToString());

        var procedure = new ResourceProcedureContext(
            projectedApp,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, start));
        Assert.NotNull(await provider.GetActionUnavailableReasonAsync(procedure, stop));
        Assert.NotNull(await provider.GetActionUnavailableReasonAsync(procedure, restart));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesAspNetCoreProjectAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController();
        services.AddSingleton<IAspNetCoreProjectRuntimeController>(runtimeController);
        services.AddLocalVolumeResourceType();
        services.AddNetworkResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        graph.Add(volume);
        var project = graph
            .AddDotnetProject("api", "src/Api/Api.csproj")
            .WithHotReload()
            .UseLaunchSettings(false)
            .WithHttpEndpoint(
                host: "localhost",
                port: 5010)
            .WithEnvironmentVariable(
                "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                "http://localhost:5104/api/control-plane/v1/traces/ingest")
            .MountVolume(volume.EffectiveResourceId, "App_Data")
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                name: "alive",
                interval: TimeSpan.FromSeconds(10));

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("project-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 21, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedProject = Assert.Single(provider.GetResources(), resource =>
            resource.Id == project.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Project, projectedProject.ResourceClass);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.ProviderId, projectedProject.Provider);
        Assert.Equal("src/Api/Api.csproj", projectedProject.ResourceAttributes["project.path"]);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedProject.ResourceAttributes["project.hotReload"]);
        Assert.False(projectedProject.ResourceAttributes.ContainsKey(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests));
        Assert.Equal([volume.EffectiveResourceId], projectedProject.DependsOn);
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.EndpointSource.ToString());
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.Monitoring.ToString());
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        var healthCheck = Assert.Single(projectedProject.ResourceHealthChecks);
        Assert.Equal("alive", healthCheck.Name);
        Assert.Equal(ResourceProbeType.Liveness, healthCheck.Type);
        Assert.Equal("/alive", healthCheck.Path);
        Assert.Equal("http", healthCheck.EndpointName);
        Assert.Equal(10, healthCheck.IntervalSeconds);
        Assert.True(projectedProject.SupportsLiveness);
        var logSource = Assert.Single(projectedProject.ResourceLogSources);
        Assert.Equal("console", logSource.Id);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, logSource.Kind);
        Assert.Equal(
            LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
            logSource.Capabilities);
        Assert.True(projectedProject.SupportsLogSources);
        var start = Assert.Single(projectedProject.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
        var stop = Assert.Single(projectedProject.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Stop.ToString());
        var restart = Assert.Single(projectedProject.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Restart.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(project.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [project.EffectiveResourceId, volume.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var capability = Assert.IsType<VolumeConsumerCapability>(
            resolution.Target!.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(capability.Mounts).Volume);
        var projectProjection = Assert.IsType<AspNetCoreProjectResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Null(projectProjection.Arguments);
        var endpointRequest = Assert.Single(projectProjection.EndpointRequests);
        Assert.Equal("http", endpointRequest.Name);
        Assert.Equal(5010, endpointRequest.Port);
        Assert.NotNull(await projectProjection.GetStopOperationAsync());
        var environmentVariables = resolution.Target.Attributes
            .GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables);
        Assert.NotNull(environmentVariables);
        Assert.True(environmentVariables.TryGetValue(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            out var environmentVariable));
        Assert.Equal(
            "http://localhost:5104/api/control-plane/v1/traces/ingest",
            environmentVariable!.Value);

        var procedure = new ResourceProcedureContext(
            projectedProject,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.NotNull(await provider.GetActionUnavailableReasonAsync(procedure, start));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, stop));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, restart));

        var stopResult = await provider.ExecuteActionAsync(procedure, stop);
        var restartResult = await provider.ExecuteActionAsync(procedure, restart);

        Assert.Equal("Executed Stop for api.", stopResult.Message);
        Assert.Equal("Executed Restart for api.", restartResult.Message);
        Assert.Equal(
            [
                (project.EffectiveResourceId, AspNetCoreProjectResourceTypeProvider.Operations.Stop),
                (project.EffectiveResourceId, AspNetCoreProjectResourceTypeProvider.Operations.Restart)
            ],
            runtimeController.ExecutedOperations);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AcceptsAspNetCoreProjectChangeWithRestartWarningWhenRunning()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController
        {
            Status = AspNetCoreProjectRuntimeStatus.Stopped
        };
        services.AddSingleton<IAspNetCoreProjectRuntimeController>(runtimeController);
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var initial = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] = "--urls http://localhost:5010"
            });

        var created = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "project-app",
                [initial],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 22, 15, 0, TimeSpan.Zero)));
        runtimeController.Status = AspNetCoreProjectRuntimeStatus.Running;

        var changed = await service.ApplyDefinitionsAsync(
            [
                initial with
                {
                    Attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                            "src/Api/Api.csproj",
                        [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                            "--urls http://localhost:5011"
                    }
                }
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 22, 20, 0, TimeSpan.Zero)));

        Assert.False(created.HasErrors, FormatDiagnostics(created.Diagnostics));
        Assert.True(created.IsCommitted);
        Assert.False(changed.HasErrors, FormatDiagnostics(changed.Diagnostics));
        Assert.True(changed.IsCommitted);
        var warning = Assert.Single(changed.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("application.aspNetCoreProject.restartRequired", warning.Code);

        var committed = Assert.Single(changed.Commit.Snapshot!.Resources);
        Assert.Equal(new ResourceGraphVersion(2), changed.Commit.Version);
        Assert.Equal("--urls http://localhost:5011", committed.ResourceAttributes[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesAspNetCoreArtifactAsNewRevisionWithRestartWarning()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController
        {
            Status = AspNetCoreProjectRuntimeStatus.Stopped
        };
        services.AddSingleton<IAspNetCoreProjectRuntimeController>(runtimeController);
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var initial = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            "application.dotnet-app:api",
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: CreateArtifactAttributes("rev-1", "hash-one"));

        var created = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "project-app",
                [initial],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 11, 16, 0, 0, TimeSpan.Zero)));
        runtimeController.Status = AspNetCoreProjectRuntimeStatus.Running;

        var changed = await service.ApplyDefinitionsAsync(
            [
                initial with
                {
                    Attributes = CreateArtifactAttributes("rev-2", "hash-two")
                }
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 11, 16, 5, 0, TimeSpan.Zero)));
        var restoredRevision = new DeploymentArtifactRevision(
            "deployment-artifact:application.dotnet-app:api",
            "rev-1",
            "zip",
            "hash-one",
            1024,
            new DateTimeOffset(2026, 7, 11, 15, 55, 0, TimeSpan.Zero),
            "dotnetPublishedOutput");
        var restored = await service.ApplyDefinitionsAsync(
            [
                initial with
                {
                    Attributes = CreateArtifactAttributes("rev-1", "hash-one"),
                    Metadata = ApplicationArtifactRestoreMetadataNames.FromRevision(restoredRevision)
                }
            ],
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 11, 16, 10, 0, TimeSpan.Zero)));

        Assert.False(created.HasErrors, FormatDiagnostics(created.Diagnostics));
        Assert.True(created.IsCommitted);
        Assert.False(changed.HasErrors, FormatDiagnostics(changed.Diagnostics));
        Assert.True(changed.IsCommitted);
        var warning = Assert.Single(changed.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal("application.aspNetCoreProject.restartRequired", warning.Code);

        var committed = Assert.Single(changed.Commit.Snapshot!.Resources);
        var source = committed.ResourceAttributeValues[ApplicationArtifactAttributeIds.Source]
            .ToObject<ApplicationArtifactReference>()!;
        Assert.Equal(new ResourceGraphVersion(2), changed.Commit.Version);
        Assert.Equal(new ResourceRevision(2), committed.Revision);
        Assert.Equal("rev-2", source.RevisionId);
        Assert.Equal("hash-two", source.ContentSha256);

        Assert.False(restored.HasErrors, FormatDiagnostics(restored.Diagnostics));
        Assert.True(restored.IsCommitted);
        var restoredState = Assert.Single(restored.Commit.Snapshot!.Resources);
        var restoredSource = restoredState.ResourceAttributeValues[ApplicationArtifactAttributeIds.Source]
            .ToObject<ApplicationArtifactReference>()!;
        Assert.Equal(new ResourceRevision(3), restoredState.Revision);
        Assert.Equal("rev-1", restoredSource.RevisionId);
        Assert.Equal("rev-1", restoredState.Metadata?[
            ApplicationArtifactRestoreMetadataNames.RestoredFromRevisionId]);
        Assert.Equal("hash-one", restoredState.Metadata?[
            ApplicationArtifactRestoreMetadataNames.RestoredFromContentSha256]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesContainerHostAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingContainerHostInspector();
        services.AddSingleton<IContainerHostInspector>(inspector);
        services.AddContainerHostResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("container-host", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 23, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedHost = Assert.Single(provider.GetResources(), resource =>
            resource.Id == host.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedHost.ResourceClass);
        Assert.Equal(ContainerHostResourceTypeProvider.ProviderId, projectedHost.Provider);
        Assert.Equal("containerHost", projectedHost.ResourceAttributes["infrastructure.kind"]);
        Assert.Equal("Docker", projectedHost.ResourceAttributes["container.host.kind"]);
        Assert.Contains(projectedHost.ResourceCapabilities, capability =>
            capability.Id == ContainerHostResourceTypeProvider.Capabilities.ContainerImage.ToString());
        Assert.Contains(projectedHost.ResourceCapabilities, capability =>
            capability.Id == ContainerHostResourceTypeProvider.Capabilities.StorageMountFileSystem.ToString());
        var inspect = Assert.Single(projectedHost.ResourceActions, action =>
            action.Id == ContainerHostResourceTypeProvider.Operations.Inspect.ToString());

        var procedure = new ResourceProcedureContext(
            projectedHost,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Container Host Inspect for docker.", procedureResult.Message);
        Assert.Equal([host.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDockerHostAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingDockerHostInspector();
        services.AddSingleton<IDockerHostInspector>(inspector);
        services.AddDockerHostResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph
            .AddDockerHost("engine")
            .UseLocalDocker();

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("docker-host", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 3, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedHost = Assert.Single(provider.GetResources(), resource =>
            resource.Id == host.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedHost.ResourceClass);
        Assert.Equal(DockerHostResourceTypeProvider.ProviderId, projectedHost.Provider);
        Assert.Equal("containerHost", projectedHost.ResourceAttributes["infrastructure.kind"]);
        Assert.Equal("local", projectedHost.ResourceAttributes["docker.host.kind"]);
        Assert.Equal("unix:///var/run/docker.sock", projectedHost.ResourceAttributes["docker.host.endpoint"]);
        Assert.Contains(projectedHost.ResourceCapabilities, capability =>
            capability.Id == DockerHostResourceTypeProvider.Capabilities.ContainerImage.ToString());
        Assert.Contains(projectedHost.ResourceCapabilities, capability =>
            capability.Id == DockerHostResourceTypeProvider.Capabilities.StorageMountFileSystem.ToString());
        var inspect = Assert.Single(projectedHost.ResourceActions, action =>
            action.Id == DockerHostResourceTypeProvider.Operations.Inspect.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(host.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<DockerHostResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.True(projection.SupportsContainerImages);

        var procedure = new ResourceProcedureContext(
            projectedHost,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Docker Host Inspect for engine.", procedureResult.Message);
        Assert.Equal([host.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDockerContainerAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerContainerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var container = graph
            .AddDockerContainer("api")
            .WithImage("example/api:1.0")
            .WithRegistry("registry.local");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("docker-container", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 4, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedContainer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == container.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Container, projectedContainer.ResourceClass);
        Assert.Equal(DockerContainerResourceTypeProvider.ProviderId, projectedContainer.Provider);
        Assert.Equal("ContainerImage", projectedContainer.ResourceAttributes["workload.kind"]);
        Assert.Equal("example/api:1.0", projectedContainer.ResourceAttributes["container.image"]);
        Assert.Equal("registry.local", projectedContainer.ResourceAttributes["container.registry"]);
        Assert.Equal("1", projectedContainer.ResourceAttributes["container.replicas"]);
        Assert.Equal("0", projectedContainer.ResourceAttributes["endpoints.count"]);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.Monitoring.ToString());
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        var start = Assert.Single(projectedContainer.ResourceActions, action =>
            action.Id == DockerContainerResourceTypeProvider.Operations.Start.ToString());
        Assert.Equal(ResourceActionKind.Start, start.Kind);
        var unpause = Assert.Single(projectedContainer.ResourceActions, action =>
            action.Id == DockerContainerResourceTypeProvider.Operations.Unpause.ToString());
        Assert.Equal("Docker Unpause", unpause.DisplayName);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(container.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<DockerContainerResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("example/api:1.0", projection.Image);
        Assert.True(projection.SupportsMonitoring);
        Assert.True(projection.SupportsLogSources);

        var procedure = new ResourceProcedureContext(
            projectedContainer,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, start));

        var procedureResult = await provider.ExecuteActionAsync(procedure, start);

        Assert.Equal("Executed Start for api.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsDockerContainerEndpointCountChanges()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerContainerResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var container = new ResourceDefinition(
            "api",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerContainerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0",
                [DockerContainerResourceTypeProvider.Attributes.EndpointCount] = "2"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "docker-container",
                [container],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 5, 0, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange &&
            string.Equals(
                diagnostic.Target,
                DockerContainerResourceTypeProvider.Attributes.EndpointCount.ToString(),
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesConfigurationStoreAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingConfigurationStoreInspector();
        services.AddSingleton<IConfigurationStoreInspector>(inspector);
        services.AddConfigurationStoreResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var configurationStore = new ConfigurationStoreResourceDefinitionBuilder("settings")
            .WithRuntimeMonitoring()
            .WithEndpoint("http://localhost:5138")
            .Build();

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "configuration-store",
                [configurationStore],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 0, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedStore = Assert.Single(provider.GetResources(), resource =>
            resource.Id == configurationStore.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Configuration, projectedStore.ResourceClass);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ProviderId, projectedStore.Provider);
        Assert.Equal("store", projectedStore.ResourceAttributes["kind"]);
        Assert.Equal("http://localhost:5138", projectedStore.ResourceAttributes["endpoint"]);
        Assert.Equal("0", projectedStore.ResourceAttributes["settingCount"]);
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.EndpointSource);
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.Monitoring);
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        AssertServiceHealthAndLiveness(projectedStore, "settings");
        var inspect = Assert.Single(projectedStore.ResourceActions, action =>
            action.Id == ConfigurationStoreResourceTypeProvider.Operations.Inspect.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(configurationStore.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<ConfigurationStoreResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal(0, projection.SettingCount);

        var procedure = new ResourceProcedureContext(
            projectedStore,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Configuration Store Inspect for settings.", procedureResult.Message);
        Assert.Equal([configurationStore.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesHostConfigurationSourceAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingHostConfigurationSourceInspector();
        services.AddSingleton<IHostConfigurationSourceInspector>(inspector);
        services.AddHostConfigurationSourceResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var hostConfiguration = graph.AddHostConfigurationSource("host-settings");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("host-configuration", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 2, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedSource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == hostConfiguration.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Configuration, projectedSource.ResourceClass);
        Assert.Equal(HostConfigurationSourceResourceTypeProvider.ProviderId, projectedSource.Provider);
        Assert.Equal("host", projectedSource.ResourceAttributes["configuration.kind"]);
        Assert.Equal("host", projectedSource.ResourceAttributes["configuration.source"]);
        Assert.Equal("0", projectedSource.ResourceAttributes[
            HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount]);
        var inspect = Assert.Single(projectedSource.ResourceActions, action =>
            action.Id == HostConfigurationSourceResourceTypeProvider.Operations.Inspect.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(hostConfiguration.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<HostConfigurationSourceResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal(0, projection.EntryCount);

        var procedure = new ResourceProcedureContext(
            projectedSource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Configuration Host Inspect for host-settings.", procedureResult.Message);
        Assert.Equal([hostConfiguration.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesLoadBalancerAcrossProviderBoundaries()
    {
        var configurationApplier = new RecordingLoadBalancerConfigurationApplier();
        var services = new ServiceCollection();
        services.AddSingleton<ILoadBalancerConfigurationApplier>(configurationApplier);
        services.AddInMemoryResourceModelGraph();
        services.AddContainerApplicationResourceType();
        services.AddDockerHostResourceType();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var host = graph.AddDockerHost("engine");
        var target = graph
            .AddContainerApplication("api")
            .WithImage("example/api:1.0");
        var loadBalancer = graph
            .AddLoadBalancer("edge")
            .UseHost(host)
            .AddBackendTarget(target, ContainerApplicationResourceTypeProvider.ResourceTypeId)
            .WithProvider("traefik");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("load-balancer", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 4, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedLoadBalancer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == loadBalancer.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedLoadBalancer.ResourceClass);
        Assert.Equal(LoadBalancerResourceTypeProvider.ProviderId, projectedLoadBalancer.Provider);
        Assert.Equal("traefik", projectedLoadBalancer.ResourceAttributes["loadBalancer.provider"]);
        Assert.Equal(host.EffectiveResourceId, projectedLoadBalancer.ResourceAttributes["loadBalancer.hostResourceId"]);
        Assert.Equal("0", projectedLoadBalancer.ResourceAttributes["loadBalancer.routes"]);
        Assert.Equal([host.EffectiveResourceId, target.EffectiveResourceId], projectedLoadBalancer.DependsOn);
        Assert.Contains(projectedLoadBalancer.ResourceCapabilities, capability =>
            capability.Id == LoadBalancerResourceTypeProvider.Capabilities.NetworkingLoadBalancer.ToString());
        var apply = Assert.Single(projectedLoadBalancer.ResourceActions, action =>
            action.Id == LoadBalancerResourceTypeProvider.Operations.ApplyConfiguration.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(loadBalancer.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<LoadBalancerResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal(0, projection.RouteCount);
        Assert.Equal([host.EffectiveResourceId, target.EffectiveResourceId], projection.References.Select(reference => reference.Value));

        var procedure = new ResourceProcedureContext(
            projectedLoadBalancer,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, apply));

        var procedureResult = await provider.ExecuteActionAsync(procedure, apply);

        Assert.Equal("Executed ApplyLoadBalancerConfiguration for edge.", procedureResult.Message);
        Assert.Equal([loadBalancer.EffectiveResourceId], configurationApplier.AppliedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsLoadBalancerWithNetworkBackendTarget()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLoadBalancerResourceType();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var network = new ResourceDefinition(
            "default",
            NetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: NetworkResourceTypeProvider.ProviderId);
        var loadBalancer = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            ProviderId: LoadBalancerResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    network.EffectiveResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "invalid-load-balancer",
                [network, loadBalancer],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 18, 0, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Message.Contains("cannot use resource type 'cloudshell.network'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesNetworkAcrossProviderBoundaries()
    {
        var reconciler = new RecordingNetworkEndpointMappingReconciler();
        var services = new ServiceCollection();
        services.AddSingleton<INetworkEndpointMappingReconciler>(reconciler);
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var network = new ResourceDefinition(
            "edge-network",
            NetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: NetworkResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NetworkResourceTypeProvider.Attributes.NetworkKind] = "Virtual",
                [NetworkResourceTypeProvider.Attributes.HostReadiness] = "providerRequired",
                [NetworkResourceTypeProvider.Attributes.MappingProviders] = "traefik"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "network",
                [network],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 5, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedNetwork = Assert.Single(provider.GetResources(), resource =>
            resource.Id == network.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedNetwork.ResourceClass);
        Assert.Equal(NetworkResourceTypeProvider.ProviderId, projectedNetwork.Provider);
        Assert.Equal("Virtual", projectedNetwork.ResourceAttributes["network.kind"]);
        Assert.Equal("providerRequired", projectedNetwork.ResourceAttributes["network.hostReadiness"]);
        Assert.Equal("traefik", projectedNetwork.ResourceAttributes["network.mappingProviders"]);
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == NetworkResourceTypeProvider.Capabilities.NetworkingEndpointMapper.ToString());
        var reconcile = Assert.Single(projectedNetwork.ResourceActions, action =>
            action.Id == NetworkResourceTypeProvider.Operations.ReconcileEndpointMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(network.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<NetworkResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("traefik", projection.MappingProviders);

        var procedure = new ResourceProcedureContext(
            projectedNetwork,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed ReconcileEndpointMappings for edge-network.", procedureResult.Message);
        Assert.Equal([network.EffectiveResourceId], reconciler.ReconciledResourceIds);
        Assert.Equal([network.EffectiveResourceId], reconciler.ContextResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesVirtualNetworkAcrossProviderBoundaries()
    {
        var reconciler = new RecordingVirtualNetworkEndpointMappingReconciler();
        var services = new ServiceCollection();
        services.AddSingleton<IVirtualNetworkEndpointMappingReconciler>(reconciler);
        services.AddInMemoryResourceModelGraph();
        services.AddVirtualNetworkResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var network = new ResourceDefinition(
            "app",
            VirtualNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: VirtualNetworkResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [VirtualNetworkResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant(),
                [VirtualNetworkResourceTypeProvider.Attributes.HostReadiness] = "providerRequired",
                [VirtualNetworkResourceTypeProvider.Attributes.MappingProviders] = "cloudshell.loadBalancer:edge"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "virtual-network",
                [network],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 11, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedNetwork = Assert.Single(provider.GetResources(), resource =>
            resource.Id == network.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedNetwork.ResourceClass);
        Assert.Equal(VirtualNetworkResourceTypeProvider.ProviderId, projectedNetwork.Provider);
        Assert.Equal("Virtual", projectedNetwork.ResourceAttributes["network.kind"]);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedNetwork.ResourceAttributes["network.default"]);
        Assert.Equal("providerRequired", projectedNetwork.ResourceAttributes["network.hostReadiness"]);
        Assert.Equal("cloudshell.loadBalancer:edge", projectedNetwork.ResourceAttributes["network.mappingProviders"]);
        Assert.DoesNotContain("endpoints.count", projectedNetwork.ResourceAttributes.Keys);
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == VirtualNetworkResourceTypeProvider.Capabilities.NetworkingVirtualNetwork.ToString());
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == VirtualNetworkResourceTypeProvider.Capabilities.NetworkingIngress.ToString());
        var reconcile = Assert.Single(projectedNetwork.ResourceActions, action =>
            action.Id == VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(network.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<VirtualNetworkResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.True(projection.IsDefault);
        Assert.True(projection.SupportsIngress);

        var procedure = new ResourceProcedureContext(
            projectedNetwork,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed ReconcileEndpointMappings for app.", procedureResult.Message);
        Assert.Equal([network.EffectiveResourceId], reconciler.ReconciledResourceIds);
        Assert.Equal([network.EffectiveResourceId], reconciler.ContextResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesLocalHostNetworkAcrossProviderBoundaries()
    {
        var reconciler = new RecordingLocalHostNetworkEndpointMappingReconciler();
        var services = new ServiceCollection();
        services.AddSingleton<ILocalHostNetworkEndpointMappingReconciler>(reconciler);
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var network = new ResourceDefinition(
            "host-local",
            LocalHostNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalHostNetworkResourceTypeProvider.ProviderId);

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "local-host-network",
                [network],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedNetwork = Assert.Single(provider.GetResources(), resource =>
            resource.Id == network.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedNetwork.ResourceClass);
        Assert.Equal(LocalHostNetworkResourceTypeProvider.ProviderId, projectedNetwork.Provider);
        Assert.Equal("hostNetworking", projectedNetwork.ResourceAttributes["infrastructure.kind"]);
        Assert.Equal("ready", projectedNetwork.ResourceAttributes["network.hostReadiness"]);
        Assert.Equal("cross-platform", projectedNetwork.ResourceAttributes["host.os"]);
        Assert.Equal("localProxy", projectedNetwork.ResourceAttributes["networking.mode"]);
        Assert.DoesNotContain("network.provisionedMappingCount", projectedNetwork.ResourceAttributes.Keys);
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingProvider.ToString());
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork.ToString());
        var reconcile = Assert.Single(projectedNetwork.ResourceActions, action =>
            action.Id == LocalHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(network.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<LocalHostNetworkResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.True(projection.SupportsHostNetwork);
        Assert.Equal("cross-platform", projection.HostOperatingSystem);

        var procedure = new ResourceProcedureContext(
            projectedNetwork,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed ReconcileEndpointMappings for host-local.", procedureResult.Message);
        Assert.Equal([network.EffectiveResourceId], reconciler.ReconciledResourceIds);
        Assert.Equal([network.EffectiveResourceId], reconciler.ContextResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesHostVirtualNetworkInspiredGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var hostNetworking = new ResourceDefinition(
            "host-local",
            LocalHostNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalHostNetworkResourceTypeProvider.ProviderId);
        var api = new ResourceDefinition(
            "vnet-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:5291",
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    bool.FalseString.ToLowerInvariant()
            });
        var network = new ResourceDefinition(
            "sample-vnet",
            VirtualNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: VirtualNetworkResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    hostNetworking.EffectiveResourceId,
                    typeId: LocalHostNetworkResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    api.EffectiveResourceId,
                    typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [VirtualNetworkResourceTypeProvider.Attributes.IsDefault] =
                    bool.TrueString.ToLowerInvariant(),
                [VirtualNetworkResourceTypeProvider.Attributes.HostReadiness] =
                    "providerRequired",
                [VirtualNetworkResourceTypeProvider.Attributes.MappingProviders] =
                    hostNetworking.EffectiveResourceId
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "host-virtual-network",
                [hostNetworking, api, network],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 16, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Equal(3, result.Commit.Summary.AcceptedResourceCount);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedNetwork = Assert.Single(projectedResources, resource =>
            resource.Id == network.EffectiveResourceId);
        var projectedHost = Assert.Single(projectedResources, resource =>
            resource.Id == hostNetworking.EffectiveResourceId);
        var projectedApi = Assert.Single(projectedResources, resource =>
            resource.Id == api.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedNetwork.ResourceClass);
        Assert.Equal(ResourceManagerClass.Infrastructure, projectedHost.ResourceClass);
        Assert.Equal(ResourceManagerClass.Project, projectedApi.ResourceClass);
        Assert.Equal(
            [hostNetworking.EffectiveResourceId, api.EffectiveResourceId],
            projectedNetwork.DependsOn);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedNetwork.ResourceAttributes[
            VirtualNetworkResourceTypeProvider.Attributes.IsDefault]);
        Assert.Equal(hostNetworking.EffectiveResourceId, projectedNetwork.ResourceAttributes[
            VirtualNetworkResourceTypeProvider.Attributes.MappingProviders]);
        Assert.Contains(projectedNetwork.ResourceActions, action =>
            action.Id == VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings.ToString());
        Assert.Contains(projectedHost.ResourceCapabilities, capability =>
            capability.Id == LocalHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(network.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        Assert.Equal(
            [network.EffectiveResourceId, hostNetworking.EffectiveResourceId, api.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesMacOSHostNetworkAcrossProviderBoundaries()
    {
        var reconciler = new RecordingMacOSHostNetworkEndpointMappingReconciler();
        var services = new ServiceCollection();
        services.AddSingleton<IMacOSHostNetworkEndpointMappingReconciler>(reconciler);
        services.AddInMemoryResourceModelGraph();
        services.AddMacOSHostNetworkResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var network = new ResourceDefinition(
            "host-macos",
            MacOSHostNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: MacOSHostNetworkResourceTypeProvider.ProviderId);

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "macos-host-network",
                [network],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 12, 30, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedNetwork = Assert.Single(provider.GetResources(), resource =>
            resource.Id == network.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedNetwork.ResourceClass);
        Assert.Equal(MacOSHostNetworkResourceTypeProvider.ProviderId, projectedNetwork.Provider);
        Assert.Equal("hostNetworking", projectedNetwork.ResourceAttributes["infrastructure.kind"]);
        Assert.Equal("ready", projectedNetwork.ResourceAttributes["network.hostReadiness"]);
        Assert.Equal("macos", projectedNetwork.ResourceAttributes["host.os"]);
        Assert.Equal("localProxy", projectedNetwork.ResourceAttributes["networking.mode"]);
        Assert.DoesNotContain("network.provisionedMappingCount", projectedNetwork.ResourceAttributes.Keys);
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == MacOSHostNetworkResourceTypeProvider.Capabilities.NetworkingProvider.ToString());
        Assert.Contains(projectedNetwork.ResourceCapabilities, capability =>
            capability.Id == MacOSHostNetworkResourceTypeProvider.Capabilities.NetworkingHostNetwork.ToString());
        var reconcile = Assert.Single(projectedNetwork.ResourceActions, action =>
            action.Id == MacOSHostNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(network.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<MacOSHostNetworkResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.True(projection.SupportsHostNetwork);
        Assert.Equal("macos", projection.HostOperatingSystem);

        var procedure = new ResourceProcedureContext(
            projectedNetwork,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed ReconcileEndpointMappings for host-macos.", procedureResult.Message);
        Assert.Equal([network.EffectiveResourceId], reconciler.ReconciledResourceIds);
        Assert.Equal([network.EffectiveResourceId], reconciler.ContextResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDnsZoneAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var reconciler = new RecordingDnsZoneNameMappingReconciler();
        services.AddSingleton<IDnsZoneNameMappingReconciler>(reconciler);
        services.AddDnsZoneResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var zone = graph
            .AddDnsZone("local")
            .WithProvider("hosts-file");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("dns", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 6, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedZone = Assert.Single(provider.GetResources(), resource =>
            resource.Id == zone.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedZone.ResourceClass);
        Assert.Equal(DnsZoneResourceTypeProvider.ProviderId, projectedZone.Provider);
        Assert.Equal("local", projectedZone.ResourceAttributes["dns.zone"]);
        Assert.Equal("hosts-file", projectedZone.ResourceAttributes["dns.provider"]);
        Assert.DoesNotContain("dns.records", projectedZone.ResourceAttributes.Keys);
        Assert.Contains(projectedZone.ResourceCapabilities, capability =>
            capability.Id == DnsZoneResourceTypeProvider.Capabilities.NetworkingDnsZone.ToString());
        var reconcile = Assert.Single(projectedZone.ResourceActions, action =>
            action.Id == DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(zone.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<DnsZoneResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("hosts-file", projection.Provider);

        var procedure = new ResourceProcedureContext(
            projectedZone,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed ReconcileNameMappings for local.", procedureResult.Message);
        Assert.Equal([zone.EffectiveResourceId], reconciler.ReconciledResourceIds);
        Assert.Equal([zone.EffectiveResourceId], reconciler.ContextResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesNameMappingAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDnsZoneResourceType();
        services.AddLocalVolumeResourceType();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var zone = graph.AddDnsZone("local");
        var target = new ResourceDefinition(
            "api",
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);
        graph.Add(target);
        var mapping = zone
            .AddNameMapping("api-local")
            .MapsTarget(target.EffectiveResourceId)
            .WithHostName("api.local")
            .WithTargetEndpointName("http")
            .WithExposure("Public");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("name-mapping", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 7, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedMapping = Assert.Single(provider.GetResources(), resource =>
            resource.Id == mapping.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Network, projectedMapping.ResourceClass);
        Assert.Equal(NameMappingResourceTypeProvider.ProviderId, projectedMapping.Provider);
        Assert.Equal("api.local", projectedMapping.ResourceAttributes["nameMapping.hostName"]);
        Assert.Equal("http", projectedMapping.ResourceAttributes["nameMapping.targetEndpointName"]);
        Assert.Equal("Public", projectedMapping.ResourceAttributes["nameMapping.exposure"]);
        Assert.DoesNotContain("nameMapping.status", projectedMapping.ResourceAttributes.Keys);
        Assert.DoesNotContain("nameMapping.materializationStatus", projectedMapping.ResourceAttributes.Keys);
        Assert.Empty(projectedMapping.DependsOn);
        Assert.Equal(zone.EffectiveResourceId, projectedMapping.ParentResourceId);
        Assert.Equal(target.EffectiveResourceId, projectedMapping.ResourceAttributes[
            ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Contains(projectedMapping.ResourceCapabilities, capability =>
            capability.Id == NameMappingResourceTypeProvider.Capabilities.NetworkingNameMapping.ToString());
        Assert.Empty(projectedMapping.ResourceActions);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(mapping.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var statusAttribute = resolution.Target!.Attributes.Resolve(
            NameMappingResourceTypeProvider.Attributes.MaterializationStatus);
        Assert.NotNull(statusAttribute);
        Assert.True(statusAttribute.IsDefined);
        Assert.False(statusAttribute.IsSet);
        Assert.True(statusAttribute.ReadOnly);
        Assert.Equal(
            ResourceAttributeMutability.ProviderManaged,
            statusAttribute.Mutability);
        var projection = Assert.IsType<NameMappingResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("api.local", projection.HostName);
        Assert.Empty(projection.Resource.State.StartupDependencyIds);
        Assert.Contains(projection.References, reference =>
            reference.Relationship == ResourceReferenceRelationships.BelongsTo &&
            reference.Value == zone.EffectiveResourceId);
        Assert.Contains(projection.References, reference =>
            reference.Relationship == ResourceReferenceRelationships.Reference &&
            reference.Value == target.EffectiveResourceId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsNameMappingWithoutDnsZoneReference()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var target = new ResourceDefinition(
            "api",
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);
        var mapping = new ResourceDefinition(
            "api-local",
            NameMappingResourceTypeProvider.ResourceTypeId,
            ProviderId: NameMappingResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.ReferenceResourceId(target.EffectiveResourceId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NameMappingResourceTypeProvider.Attributes.HostName] = "api.local",
                [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "invalid-name-mapping",
                [target, mapping],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 16, 30, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "dns.nameMapping.zoneRequired" &&
            diagnostic.Message.Contains("must belong to a DNS zone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesApplicationExposureGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerApplicationResourceType();
        services.AddNetworkResourceType();
        services.AddServiceResourceType();
        services.AddDnsZoneResourceType();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var api = graph
            .AddContainerApplication("application-topology-api")
            .WithImage("example/application-topology-api:1.0");
        var network = graph.AddNetwork("application-topology-local");
        var apiService = graph
            .AddService("application-topology-api-service")
            .DependsOnTarget(api)
            .DependsOnNetwork(network)
            .WithRoutingMode("logical");
        var zone = graph
            .AddDnsZone("application-topology-local")
            .WithZoneName("application-topology.cloudshell.local")
            .WithProvider("hosts-file");
        var mapping = zone
            .AddNameMapping("application-topology-api-local")
            .MapsTarget(apiService)
            .WithHostName("api.application-topology.cloudshell.local")
            .WithTargetEndpointName("http")
            .WithExposure("Public");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("application-exposure", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 14, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedService = Assert.Single(projectedResources, resource =>
            resource.Id == apiService.EffectiveResourceId);
        var projectedMapping = Assert.Single(projectedResources, resource =>
            resource.Id == mapping.EffectiveResourceId);
        var projectedZone = Assert.Single(projectedResources, resource =>
            resource.Id == zone.EffectiveResourceId);

        Assert.Equal([api.EffectiveResourceId, network.EffectiveResourceId], projectedService.DependsOn);
        Assert.Empty(projectedMapping.DependsOn);
        Assert.Equal(zone.EffectiveResourceId, projectedMapping.ParentResourceId);
        Assert.Equal(apiService.EffectiveResourceId, projectedMapping.ResourceAttributes[
            ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Equal("api.application-topology.cloudshell.local", projectedMapping.ResourceAttributes[
            NameMappingResourceTypeProvider.Attributes.HostName]);
        Assert.Contains(projectedService.ResourceActions, action =>
            action.Id == ServiceResourceTypeProvider.Operations.Reconcile.ToString());
        Assert.Contains(projectedZone.ResourceActions, action =>
            action.Id == DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(mapping.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Equal([mapping.EffectiveResourceId], resolvedResourceIds);
        Assert.Empty(resolution.Resources[0].State.StartupDependencyIds);
        Assert.Contains(resolution.Resources[0].State.ResourceDependencies, reference =>
            reference.Relationship == ResourceReferenceRelationships.BelongsTo &&
            reference.Value == zone.EffectiveResourceId);
        Assert.Contains(resolution.Resources[0].State.ResourceDependencies, reference =>
            reference.Relationship == ResourceReferenceRelationships.Reference &&
            reference.Value == apiService.EffectiveResourceId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesStorageAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingStorageInspector();
        services.AddSingleton<IStorageInspector>(inspector);
        services.AddStorageResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var storageBuilder = graph.AddLocalStorage(
            "local",
            "Data/storage/local");
        var template = graph.BuildTemplate(
            "storage",
            environmentId: "local");
        var storage = Assert.Single(template.Resources);

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 8, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedStorage = Assert.Single(provider.GetResources(), resource =>
            resource.Id == storageBuilder.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Storage, projectedStorage.ResourceClass);
        Assert.Equal(StorageResourceTypeProvider.ProviderId, projectedStorage.Provider);
        Assert.Equal("provider", projectedStorage.ResourceAttributes["storage.kind"]);
        Assert.Equal("local", projectedStorage.ResourceAttributes["storage.provider"]);
        Assert.Equal("FileSystem", projectedStorage.ResourceAttributes["storage.medium"]);
        Assert.Equal("Data/storage/local", projectedStorage.ResourceAttributes["storage.location"]);
        Assert.DoesNotContain("storage.volumes", projectedStorage.ResourceAttributes.Keys);
        Assert.DoesNotContain("storage.runtimeStatus", projectedStorage.ResourceAttributes.Keys);
        Assert.Contains(projectedStorage.ResourceCapabilities, capability =>
            capability.Id == StorageResourceTypeProvider.Capabilities.StorageProvider.ToString());
        Assert.Contains(projectedStorage.ResourceCapabilities, capability =>
            capability.Id == StorageResourceTypeProvider.Capabilities.StorageMountProvider.ToString());
        var inspect = Assert.Single(projectedStorage.ResourceActions, action =>
            action.Id == StorageResourceTypeProvider.Operations.Inspect.ToString());
        Assert.Equal("Storage Inspect", inspect.DisplayName);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(storageBuilder.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<StorageResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("FileSystem", projection.Medium);

        var procedure = new ResourceProcedureContext(
            projectedStorage,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Storage Inspect for local.", procedureResult.Message);
        Assert.Equal([storageBuilder.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesCloudShellVolumeAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var provisioner = new RecordingCloudShellVolumeProvisioner();
        services.AddSingleton<ICloudShellVolumeProvisioner>(provisioner);
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var storage = graph.AddLocalStorage("local");
        var volume = storage.AddVolume(
            "data",
            subPath: "data");
        var template = graph.BuildTemplate(
            "storage-volume",
            environmentId: "local");

        var result = await service.ApplyTemplateAsync(
            template,
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 9, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedVolume = Assert.Single(provider.GetResources(), resource =>
            resource.Id == volume.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Storage, projectedVolume.ResourceClass);
        Assert.Equal(CloudShellVolumeResourceTypeProvider.ProviderId, projectedVolume.Provider);
        Assert.Equal("volume", projectedVolume.ResourceAttributes["storage.kind"]);
        Assert.Equal("local", projectedVolume.ResourceAttributes["storage.volume.provider"]);
        Assert.Equal("FileSystem", projectedVolume.ResourceAttributes["storage.volume.medium"]);
        Assert.Equal("data", projectedVolume.ResourceAttributes["storage.volume.subPath"]);
        Assert.Equal("ReadWriteOnce", projectedVolume.ResourceAttributes["storage.volume.accessMode"]);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedVolume.ResourceAttributes["storage.volume.persistent"]);
        Assert.DoesNotContain("storage.volume.storageResourceId", projectedVolume.ResourceAttributes.Keys);
        Assert.DoesNotContain("storage.runtimeStatus", projectedVolume.ResourceAttributes.Keys);
        Assert.Equal([storage.EffectiveResourceId], projectedVolume.DependsOn);
        Assert.Contains(projectedVolume.ResourceCapabilities, capability =>
            capability.Id == CloudShellVolumeResourceTypeProvider.Capabilities.StorageVolume.ToString());
        var provision = Assert.Single(projectedVolume.ResourceActions, action =>
            action.Id == CloudShellVolumeResourceTypeProvider.Operations.Provision.ToString());
        Assert.Equal("Storage Volume Provision", provision.DisplayName);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(volume.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<CloudShellVolumeResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("data", projection.SubPath);
        Assert.Equal([storage.EffectiveResourceId], projection.References.Select(reference => reference.Value));

        var procedure = new ResourceProcedureContext(
            projectedVolume,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, provision));

        var procedureResult = await provider.ExecuteActionAsync(procedure, provision);

        Assert.Equal("Executed Storage Volume Provision for data.", procedureResult.Message);
        Assert.Equal([volume.EffectiveResourceId], provisioner.ProvisionedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsCloudShellVolumeWithNonStorageReference()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var localVolume = new ResourceDefinition(
            "local-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);
        var volume = new ResourceDefinition(
            "data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    localVolume.EffectiveResourceId,
                    typeId: StorageResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "invalid-storage-volume",
                [localVolume, volume],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 17, 0, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Message.Contains("expected 'cloudshell.storage'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesServiceAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var reconciler = new RecordingServiceReconciler();
        services.AddSingleton<IServiceReconciler>(reconciler);
        services.AddContainerApplicationResourceType();
        services.AddNetworkResourceType();
        services.AddServiceResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var target = graph
            .AddContainerApplication("api")
            .WithImage("example/api:1.0");
        var network = graph.AddNetwork("default");
        var definition = graph
            .AddService("api-service")
            .DependsOnTarget(target)
            .DependsOnNetwork(network)
            .WithRoutingMode("logical");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("service", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 10, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedService = Assert.Single(provider.GetResources(), resource =>
            resource.Id == definition.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Service, projectedService.ResourceClass);
        Assert.Equal(ServiceResourceTypeProvider.ProviderId, projectedService.Provider);
        Assert.Equal("service", projectedService.ResourceAttributes["service.kind"]);
        Assert.Equal("logical", projectedService.ResourceAttributes["service.routingMode"]);
        Assert.DoesNotContain("service.targets", projectedService.ResourceAttributes.Keys);
        Assert.DoesNotContain("service.ports", projectedService.ResourceAttributes.Keys);
        Assert.DoesNotContain("endpoints.count", projectedService.ResourceAttributes.Keys);
        Assert.Equal([target.EffectiveResourceId, network.EffectiveResourceId], projectedService.DependsOn);
        Assert.Contains(projectedService.ResourceCapabilities, capability =>
            capability.Id == ResourceCommonCapabilityIds.EndpointSource.ToString());
        var reconcile = Assert.Single(projectedService.ResourceActions, action =>
            action.Id == ServiceResourceTypeProvider.Operations.Reconcile.ToString());
        Assert.Equal("Service Reconcile", reconcile.DisplayName);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(definition.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<ServiceResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("logical", projection.RoutingMode);

        var procedure = new ResourceProcedureContext(
            projectedService,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed Service Reconcile for api-service.", procedureResult.Message);
        Assert.Equal([definition.EffectiveResourceId], reconciler.ReconciledResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsServiceWithNonNetworkReference()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerApplicationResourceType();
        services.AddLocalVolumeResourceType();
        services.AddServiceResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var target = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0"
            });
        var notNetwork = new ResourceDefinition(
            "local-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);
        var definition = new ResourceDefinition(
            "api-service",
            ServiceResourceTypeProvider.ResourceTypeId,
            ProviderId: ServiceResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    target.EffectiveResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    notNetwork.EffectiveResourceId,
                    typeId: NetworkResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ServiceResourceTypeProvider.Attributes.RoutingMode] = "logical"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "invalid-service",
                [target, notNetwork, definition],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 17, 30, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Message.Contains("expected a network resource", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSecretsVaultAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var inspector = new RecordingSecretsVaultInspector();
        services.AddSingleton<ISecretsVaultInspector>(inspector);
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var vault = new SecretsVaultResourceDefinitionBuilder("vault")
            .WithRuntimeMonitoring()
            .WithEndpoint("http://localhost:6138")
            .Build();

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "secrets-vault",
                [vault],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 1, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedVault = Assert.Single(provider.GetResources(), resource =>
            resource.Id == vault.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.SecretsVault, projectedVault.ResourceClass);
        Assert.Equal(SecretsVaultResourceTypeProvider.ProviderId, projectedVault.Provider);
        Assert.Equal("vault", projectedVault.ResourceAttributes["kind"]);
        Assert.Equal("http://localhost:6138", projectedVault.ResourceAttributes["endpoint"]);
        Assert.Equal("0", projectedVault.ResourceAttributes["secretCount"]);
        Assert.Contains(projectedVault.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.EndpointSource);
        Assert.Contains(projectedVault.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.Monitoring);
        Assert.Contains(projectedVault.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedVault.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        AssertServiceHealthAndLiveness(projectedVault, "secrets");
        var inspect = Assert.Single(projectedVault.ResourceActions, action =>
            action.Id == SecretsVaultResourceTypeProvider.Operations.Inspect.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(vault.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<SecretsVaultResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal(0, projection.SecretCount);

        var procedure = new ResourceProcedureContext(
            projectedVault,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Secrets Vault Inspect for vault.", procedureResult.Message);
        Assert.Equal([vault.EffectiveResourceId], inspector.InspectedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesIdentityProvisioningAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var setupHandler = new RecordingIdentityProvisioningSetupHandler();
        services.AddSingleton<IIdentityProvisioningSetupHandler>(setupHandler);
        services.AddIdentityProvisioningResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var identity = graph
            .AddIdentityProvisioning("built-in")
            .WithIdentityProvider("Built-in Identity")
            .WithProviderKind("built-in");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("identity-provisioning", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 2, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedIdentity = Assert.Single(provider.GetResources(), resource =>
            resource.Id == identity.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedIdentity.ResourceClass);
        Assert.Equal(IdentityProvisioningResourceTypeProvider.ProviderId, projectedIdentity.Provider);
        Assert.Equal("identity-provisioning", projectedIdentity.ResourceAttributes["infrastructure.kind"]);
        Assert.Equal("Built-in Identity", projectedIdentity.ResourceAttributes["identity.provider"]);
        Assert.Equal("built-in", projectedIdentity.ResourceAttributes["identity.providerKind"]);
        Assert.Contains(projectedIdentity.ResourceCapabilities, capability =>
            capability.Id == IdentityProvisioningResourceTypeProvider.Capabilities.IdentityProvisioning.ToString());
        var setup = Assert.Single(projectedIdentity.ResourceActions, action =>
            action.Id == IdentityProvisioningResourceTypeProvider.Operations.Setup.ToString());
        Assert.Equal("Identity Provisioning Setup", setup.DisplayName);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(identity.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<IdentityProvisioningResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal("Built-in Identity", projection.IdentityProvider);
        Assert.True(projection.SupportsIdentityProvisioning);

        var procedure = new ResourceProcedureContext(
            projectedIdentity,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, setup));

        var procedureResult = await provider.ExecuteActionAsync(procedure, setup);

        Assert.Equal("Executed Identity Provisioning Setup for built-in.", procedureResult.Message);
        Assert.Equal([identity.EffectiveResourceId], setupHandler.SetupResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_ProjectsIdentityProvisioningSetupUnavailableWithoutSetupHandler()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddIdentityProvisioningResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var identity = graph
            .AddIdentityProvisioning("built-in")
            .WithIdentityProvider("Built-in Identity")
            .WithProviderKind("built-in");

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("identity-provisioning", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 2, 5, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(identity.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<IdentityProvisioningResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        var setupOperation = await projection.GetSetupOperationAsync();

        Assert.NotNull(setupOperation);
        Assert.False(await setupOperation.CanExecuteAsync());
        Assert.Contains("no identity provisioning setup handler", setupOperation.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSqlServerAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var accessReconciler = new RecordingSqlServerAccessReconciler();
        services.AddSingleton<ISqlServerAccessReconciler>(accessReconciler);
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var volume = new ResourceDefinition(
            "sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        graph.Add(volume);
        var sql = graph
            .AddSqlServer("sql")
            .WithVersion("2022")
            .WithEdition("Developer")
            .MountVolume(volume.EffectiveResourceId, "/var/opt/mssql")
            .DeclareDatabase("appdb", "Application DB", ensureCreated: true);

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("sql-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 20, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedSql = Assert.Single(provider.GetResources(), resource =>
            resource.Id == sql.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Service, projectedSql.ResourceClass);
        Assert.Equal(SqlServerResourceTypeProvider.ProviderId, projectedSql.Provider);
        Assert.Equal(ResourceManagerResourceState.Unknown, projectedSql.State);
        Assert.Equal("2022", projectedSql.ResourceAttributes["version"]);
        Assert.Equal(
            [ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId, volume.EffectiveResourceId],
            projectedSql.DependsOn);
        Assert.Contains(projectedSql.ResourceActions, action =>
            action.Id == "start" &&
            action.Kind == ResourceActionKind.Start);
        Assert.Contains(projectedSql.ResourceActions, action =>
            action.Id == "stop" &&
            action.Kind == ResourceActionKind.Stop);
        Assert.Contains(projectedSql.ResourceActions, action =>
            action.Id == "restart" &&
            action.Kind == ResourceActionKind.Restart);
        var reconcile = Assert.Single(projectedSql.ResourceActions, action =>
            action.Id == SqlServerResourceTypeProvider.Operations.ReconcileAccess.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(sql.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [
                sql.EffectiveResourceId,
                ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId,
                volume.EffectiveResourceId
            ],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var capability = Assert.IsType<VolumeConsumerCapability>(
            resolution.Target!.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(capability.Mounts).Volume);

        var procedure = new ResourceProcedureContext(
            projectedSql,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        var procedureResult = await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal("Executed Application Sql Server Reconcile Access for sql.", procedureResult.Message);
        Assert.Equal([sql.EffectiveResourceId], accessReconciler.ReconciledResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_ProjectsRabbitMQReconcileAccessUnavailableWithoutReconciler()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddRabbitMQResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var broker = graph
            .AddRabbitMQ("rabbitmq")
            .WithVersion("3")
            .WithAmqpEndpoint(host: "localhost", port: 5672)
            .WithManagementEndpoint(host: "localhost", port: 15672);

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("rabbitmq-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 10, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(broker.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<RabbitMQResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));
        var reconcileOperation = await projection.GetReconcileAccessOperationAsync();

        Assert.NotNull(reconcileOperation);
        Assert.False(await reconcileOperation.CanExecuteAsync());
        Assert.Contains("no RabbitMQ access reconciler", reconcileOperation.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesRabbitMQAcrossProviderBoundaries()
    {
        var accessReconciler = new RecordingRabbitMQAccessReconciler();
        var declarations = new ResourceDeclarationStore();
        var services = new ServiceCollection();
        services.AddSingleton(declarations);
        services.AddSingleton<IResourcePermissionGrantReader>(declarations);
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddSingleton<IRabbitMQAccessReconciler>(accessReconciler);
        services.AddRabbitMQResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var volume = graph.AddVolume(
            "rabbitmq-data",
            path: "./Data/storage/rabbitmq");
        var broker = graph
            .AddRabbitMQ("rabbitmq")
            .WithVersion("3")
            .WithAmqpEndpoint(host: "localhost", port: 5672)
            .WithManagementEndpoint(host: "localhost", port: 15672)
            .MountVolume(volume, RabbitMQResourceDefaults.DataPath);

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("rabbitmq-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 3, 12, 15, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedRabbitMQ = Assert.Single(provider.GetResources(), resource =>
            resource.Id == broker.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Service, projectedRabbitMQ.ResourceClass);
        Assert.Equal(RabbitMQResourceTypeProvider.ProviderId, projectedRabbitMQ.Provider);
        Assert.Equal(ResourceManagerResourceState.Unknown, projectedRabbitMQ.State);
        Assert.Equal("3", projectedRabbitMQ.ResourceAttributes["version"]);
        Assert.Equal(
            [ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId, volume.EffectiveResourceId],
            projectedRabbitMQ.DependsOn);
        Assert.Contains(projectedRabbitMQ.ResourceActions, action =>
            action.Id == "start" &&
            action.Kind == ResourceActionKind.Start);
        Assert.Contains(projectedRabbitMQ.ResourceActions, action =>
            action.Id == "stop" &&
            action.Kind == ResourceActionKind.Stop);
        Assert.Contains(projectedRabbitMQ.ResourceActions, action =>
            action.Id == "restart" &&
            action.Kind == ResourceActionKind.Restart);
        var reconcile = Assert.Single(projectedRabbitMQ.ResourceActions, action =>
            action.Id == RabbitMQResourceTypeProvider.Operations.ReconcileAccess.ToString());
        Assert.Equal(
            RabbitMQResourceOperationPermissions.ReconcileAccess,
            reconcile.RequiredPermission);
        var amqp = Assert.Single(projectedRabbitMQ.Endpoints, endpoint => endpoint.Name == "amqp");
        Assert.Equal("tcp", amqp.Protocol);
        Assert.Equal(5672, amqp.TargetPort);
        var management = Assert.Single(projectedRabbitMQ.Endpoints, endpoint => endpoint.Name == "management");
        Assert.Equal("http", management.Protocol);
        Assert.Equal(15672, management.TargetPort);
        Assert.Contains(projectedRabbitMQ.ResourceEndpointNetworkMappings, mapping =>
            mapping.Target.EndpointName == "management" &&
            mapping.Address == "http://localhost:15672");
        Assert.Contains(projectedRabbitMQ.ResourceCapabilities, capability =>
            capability.Id == ResourceLogSourceCapabilityIds.LogSources.ToString());
        var logSource = Assert.Single(projectedRabbitMQ.ResourceLogSources);
        Assert.Equal("container", logSource.Id);
        Assert.Equal("Container logs", logSource.Name);
        Assert.Equal(ResourceLogSourceKind.Container, logSource.Kind);
        Assert.Equal(LogFormat.PlainText, logSource.Format);
        Assert.Equal(
            LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
            logSource.Capabilities);
        Assert.Equal(ResourceLogSourceOrigin.ProviderDefault, logSource.Origin);
        Assert.Equal(ResourceLogSourcePurpose.Default, logSource.Purpose);
        Assert.Equal(LogSourceAvailability.ResourceRunning, logSource.Availability);
        Assert.True(projectedRabbitMQ.SupportsLogSources);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(broker.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [
                broker.EffectiveResourceId,
                ContainerHostResourceDefinitionBuilderExtensions.DefaultContainerHostResourceId,
                volume.EffectiveResourceId
            ],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
        var capability = Assert.IsType<VolumeConsumerCapability>(
            resolution.Target!.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(capability.Mounts).Volume);

        var projection = Assert.IsType<RabbitMQResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target,
                    new ResourceProjectionContext("local", "developer")));
        var reconcileOperation = await projection.GetReconcileAccessOperationAsync();

        Assert.NotNull(reconcileOperation);
        Assert.True(await reconcileOperation.CanExecuteAsync());

        var grant = new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity(
                "application.dotnet-app:api",
                "default"),
            broker.EffectiveResourceId,
            RabbitMQResourceOperationPermissions.Publish);
        declarations.AddPermissionGrant(grant);

        var plan = reconcileOperation.PlanReconciliation();
        Assert.Equal(broker.EffectiveResourceId, plan.Resource.EffectiveResourceId);
        Assert.Equal([grant], plan.Grants);

        var procedure = new ResourceProcedureContext(
            projectedRabbitMQ,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, reconcile));

        await provider.ExecuteActionAsync(procedure, reconcile);

        Assert.Equal([broker.EffectiveResourceId], accessReconciler.ReconciledResourceIds);
        Assert.Equal([grant], Assert.Single(accessReconciler.ReconciledGrants));

        var statusProvider = serviceProvider
            .GetServices<IResourcePermissionGrantStatusProvider>()
            .OfType<RabbitMQPermissionGrantStatusProvider>()
            .Single();
        var status = await statusProvider.GetStatusAsync(
            new ResourcePermissionGrantStatusRequest(projectedRabbitMQ, grant));

        Assert.True(statusProvider.CanGetStatus(new ResourcePermissionGrantStatusRequest(projectedRabbitMQ, grant)));
        Assert.Equal(ResourcePermissionGrantEffectivenessState.Pending, status.State);
        Assert.Equal(RabbitMQResourceTypeProvider.ProviderId, status.ProviderId);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDeviceRegistryAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddSecretsVaultResourceType();
        services.AddDeviceRegistryResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var vault = graph
            .AddSecretsVault("vault")
            .WithSeed(seed => seed.Certificate("factory-ca", "certificate"));
        var registry = graph
            .AddDeviceRegistry("devices")
            .WithEndpoint("http://localhost:7140")
            .WithHeartbeatStaleAfter(TimeSpan.FromMinutes(5))
            .TrustCertificate(vault.Certificate("factory-ca"))
            .UseEnrollmentPolicy(policy =>
            {
                policy.AllowSubjectPrefix("device/");
                policy.RequireClaim("manufacturer", "acme");
            });

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("iot-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedRegistry = Assert.Single(provider.GetResources(), resource =>
            resource.Id == registry.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Service, projectedRegistry.ResourceClass);
        Assert.Equal(DeviceRegistryResourceTypeProvider.ProviderId, projectedRegistry.Provider);
        Assert.Equal(ResourceManagerResourceState.Unknown, projectedRegistry.State);
        Assert.Equal("registry", projectedRegistry.ResourceAttributes["kind"]);
        Assert.Equal("http://localhost:7140", projectedRegistry.ResourceAttributes["endpoint"]);
        Assert.Equal("300", projectedRegistry.ResourceAttributes["heartbeat.staleAfterSeconds"]);
        Assert.Equal("0", projectedRegistry.ResourceAttributes["enrolledDeviceCount"]);
        Assert.Contains(projectedRegistry.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.EndpointSource);
        Assert.Contains(projectedRegistry.ResourceCapabilities, capability =>
            capability.Id == ResourceCapabilityIds.Monitoring);
        Assert.Contains(projectedRegistry.ResourceActions, action =>
            action.Id == "start" &&
            action.Kind == ResourceActionKind.Start);
        Assert.Contains(projectedRegistry.DependsOn, dependency =>
            dependency == vault.EffectiveResourceId);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(registry.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        var projection = Assert.IsType<DeviceRegistryResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolution.Target!,
                    new ResourceProjectionContext("local", "developer")));

        Assert.Equal("http://localhost:7140", projection.Endpoint);
        Assert.Equal(300, projection.HeartbeatStaleAfterSeconds);
        Assert.Equal(0, projection.EnrolledDeviceCount);
        Assert.Equal("factory-ca", Assert.Single(projection.TrustedCertificates).Name);
        Assert.Equal("device/", Assert.Single(projection.AllowedSubjectPrefixes));
        Assert.Equal("manufacturer", Assert.Single(projection.RequiredClaims).Name);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSqlDatabaseAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var creationHandler = new RecordingSqlDatabaseCreationHandler();
        services.AddSingleton<ISqlDatabaseCreationHandler>(creationHandler);
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceGraphBuilder();
        var server = graph.AddSqlServer("sql");
        var database = graph
            .AddSqlDatabase("appdb")
            .BelongsToServer(server)
            .EnsureCreated();

        var result = await service.ApplyTemplateAsync(
            graph.BuildTemplate("sql-database-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 22, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedDatabase = Assert.Single(provider.GetResources(), resource =>
            resource.Id == database.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Service, projectedDatabase.ResourceClass);
        Assert.Equal(SqlDatabaseResourceTypeProvider.ProviderId, projectedDatabase.Provider);
        Assert.Equal("appdb", projectedDatabase.ResourceAttributes["database.name"]);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedDatabase.ResourceAttributes["database.ensureCreated"]);
        Assert.Equal([server.EffectiveResourceId], projectedDatabase.DependsOn);
        var ensureCreated = Assert.Single(projectedDatabase.ResourceActions, action =>
            action.Id == SqlDatabaseResourceTypeProvider.Operations.EnsureCreated.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(database.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [database.EffectiveResourceId, server.EffectiveResourceId, "cloudshell.container-host:default"],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));

        var procedure = new ResourceProcedureContext(
            projectedDatabase,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, ensureCreated));

        var procedureResult = await provider.ExecuteActionAsync(procedure, ensureCreated);

        Assert.Equal("Executed Application Sql Database Ensure Created for appdb.", procedureResult.Message);
        Assert.Equal([database.EffectiveResourceId], creationHandler.CreatedResourceIds);
        Assert.Equal([server.EffectiveResourceId], creationHandler.ServerResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesContainerHostSampleGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddContainerHostResourceType();
        services.AddSqlServerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var host = new ResourceDefinition(
            "docker",
            ContainerHostResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerHostResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerHostResourceTypeProvider.Attributes.HostKind] = "Docker",
                [ContainerHostResourceTypeProvider.Attributes.Endpoint] = "unix:///var/run/docker.sock",
                [ContainerHostResourceTypeProvider.Attributes.Registry] = "docker.io",
                [ContainerHostResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant()
            });
        var storage = new ResourceDefinition(
            "local",
            StorageResourceTypeProvider.ResourceTypeId,
            ProviderId: StorageResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [StorageResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                [StorageResourceTypeProvider.Attributes.Location] = "./Data/storage"
            });
        var volume = new ResourceDefinition(
            "sql-data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    storage.EffectiveResourceId,
                    typeId: StorageResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "sql-server",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = bool.TrueString.ToLowerInvariant()
            });
        var sqlServer = new ResourceDefinition(
            "sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: ContainerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "container-host",
                [host, storage, volume, sqlServer],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 19, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Equal(4, result.Commit.Summary.AcceptedResourceCount);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedHost = Assert.Single(projectedResources, resource =>
            resource.Id == host.EffectiveResourceId);
        var projectedStorage = Assert.Single(projectedResources, resource =>
            resource.Id == storage.EffectiveResourceId);
        var projectedVolume = Assert.Single(projectedResources, resource =>
            resource.Id == volume.EffectiveResourceId);
        var projectedSqlServer = Assert.Single(projectedResources, resource =>
            resource.Id == sqlServer.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Infrastructure, projectedHost.ResourceClass);
        Assert.Equal(ResourceManagerClass.Storage, projectedStorage.ResourceClass);
        Assert.Equal("./Data/storage", projectedStorage.ResourceAttributes["storage.location"]);
        Assert.Equal([storage.EffectiveResourceId], projectedVolume.DependsOn);
        Assert.Equal("sql-server", projectedVolume.ResourceAttributes["storage.volume.subPath"]);
        Assert.Equal([host.EffectiveResourceId, volume.EffectiveResourceId], projectedSqlServer.DependsOn);

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(sqlServer.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Contains(sqlServer.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(host.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(volume.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(storage.EffectiveResourceId, resolvedResourceIds);
        var resolvedSqlServer = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == sqlServer.EffectiveResourceId);
        var sqlProjection = Assert.IsType<SqlServerResource>(
            await serviceProvider
                .GetRequiredService<ResourceProjectionResolver>()
                .GetResourceProjectionAsync(
                    resolvedSqlServer,
                    new ResourceProjectionContext("local", "developer")));
        Assert.Equal(host.EffectiveResourceId, sqlProjection.ContainerHostResourceId);
        var volumeCapability = Assert.IsType<VolumeConsumerCapability>(
            resolvedSqlServer.Capabilities.Get<VolumeConsumerCapability>());
        var mount = Assert.Single(volumeCapability.Mounts);
        Assert.Equal(volume.EffectiveResourceId, mount.Volume);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesApplicationTopologyInspiredGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var volume = new ResourceDefinition(
            "application-topology-sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var sqlServer = new ResourceDefinition(
            "application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer",
                [SqlServerResourceTypeProvider.Attributes.Databases] =
                    ResourceAttributeValue.FromObject(new SqlServerDatabaseDefinition[]
                    {
                        new("application_topology", "Application Topology", EnsureCreated: true)
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });
        var database = new ResourceDefinition(
            "application-topology-db",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlDatabaseResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    sqlServer.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "application_topology",
                [SqlDatabaseResourceTypeProvider.Attributes.Source] = "declared",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = bool.TrueString.ToLowerInvariant()
            });
        var settings = new ResourceDefinition(
            "application-topology-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });
        var secrets = new ResourceDefinition(
            "application-topology-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });
        var api = new ResourceDefinition(
            "application-topology-api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    database.EffectiveResourceId,
                    typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    settings.EffectiveResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    secrets.EffectiveResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "application-topology",
                [volume, sqlServer, database, settings, secrets, api],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 13, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Equal(6, result.Commit.Summary.AcceptedResourceCount);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedApi = Assert.Single(projectedResources, resource =>
            resource.Id == api.EffectiveResourceId);
        var projectedSqlServer = Assert.Single(projectedResources, resource =>
            resource.Id == sqlServer.EffectiveResourceId);
        var projectedDatabase = Assert.Single(projectedResources, resource =>
            resource.Id == database.EffectiveResourceId);
        var projectedSettings = Assert.Single(projectedResources, resource =>
            resource.Id == settings.EffectiveResourceId);
        var projectedSecrets = Assert.Single(projectedResources, resource =>
            resource.Id == secrets.EffectiveResourceId);

        Assert.Equal(
            [database.EffectiveResourceId, settings.EffectiveResourceId, secrets.EffectiveResourceId],
            projectedApi.DependsOn);
        Assert.Equal([volume.EffectiveResourceId], projectedSqlServer.DependsOn);
        Assert.Equal([sqlServer.EffectiveResourceId], projectedDatabase.DependsOn);
        Assert.Equal(ResourceManagerClass.Configuration, projectedSettings.ResourceClass);
        Assert.Equal(ResourceManagerClass.SecretsVault, projectedSecrets.ResourceClass);
        Assert.Contains(projectedDatabase.ResourceActions, action =>
            action.Id == SqlDatabaseResourceTypeProvider.Operations.EnsureCreated.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Equal(6, resolvedResourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(api.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(database.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(sqlServer.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(volume.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(settings.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(secrets.EffectiveResourceId, resolvedResourceIds);

        var resolvedSqlServer = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == sqlServer.EffectiveResourceId);
        var volumeCapability = Assert.IsType<VolumeConsumerCapability>(
            resolvedSqlServer.Capabilities.Get<VolumeConsumerCapability>());
        Assert.Equal(volume.EffectiveResourceId, Assert.Single(volumeCapability.Mounts).Volume);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesApplicationTopologyProjectGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var volume = new ResourceDefinition(
            "application-topology-sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var sqlServer = new ResourceDefinition(
            "application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "tds",
                            "tcp",
                            TargetPort: 1433,
                            Host: "localhost",
                            Port: 14334,
                            Exposure: "Local")
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });
        var database = new ResourceDefinition(
            "application-topology-db",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlDatabaseResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    sqlServer.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "application_topology",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = bool.TrueString.ToLowerInvariant()
            });
        var settings = new ResourceDefinition(
            "application-topology-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });
        var secrets = new ResourceDefinition(
            "application-topology-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });
        var api = new ResourceDefinition(
            "application-topology-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    database.EffectiveResourceId,
                    typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    settings.EffectiveResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    secrets.EffectiveResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../Api/CloudShell.ApplicationTopologyApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:21422",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    bool.TrueString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    bool.FalseString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        ResourceReference.ReferenceResourceId(
                            sqlServer.EffectiveResourceId,
                            typeId: SqlServerResourceTypeProvider.ResourceTypeId),
                        ResourceReference.ReferenceResourceId(
                            settings.EffectiveResourceId,
                            typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                        ResourceReference.ReferenceResourceId(
                            secrets.EffectiveResourceId,
                            typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
                    })
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "application-topology-project",
                [volume, sqlServer, database, settings, secrets, api],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 15, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedSqlServer = Assert.Single(provider.GetResources(), resource =>
            resource.Id == sqlServer.EffectiveResourceId);
        var projectedApi = Assert.Single(provider.GetResources(), resource =>
            resource.Id == api.EffectiveResourceId);

        var sqlEndpoint = Assert.Single(projectedSqlServer.Endpoints);
        Assert.Equal("tds", sqlEndpoint.Name);
        Assert.Equal("tcp", sqlEndpoint.Protocol);
        Assert.Equal(1433, sqlEndpoint.TargetPort);
        var sqlEndpointMapping = Assert.Single(projectedSqlServer.ResourceEndpointNetworkMappings);
        Assert.Equal("tds", sqlEndpointMapping.Target.EndpointName);
        Assert.Equal("localhost:14334", sqlEndpointMapping.Address);

        Assert.Equal(ResourceManagerClass.Project, projectedApi.ResourceClass);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.ProviderId, projectedApi.Provider);
        Assert.Equal("../Api/CloudShell.ApplicationTopologyApi.csproj", projectedApi.ResourceAttributes[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath]);
        Assert.Equal(
            [database.EffectiveResourceId, settings.EffectiveResourceId, secrets.EffectiveResourceId],
            projectedApi.DependsOn);
        Assert.Contains(projectedApi.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
        Assert.Contains(projectedApi.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Stop.ToString());
        Assert.Contains(projectedApi.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Restart.ToString());
        Assert.False(projectedApi.ResourceAttributes.ContainsKey(
            AspNetCoreProjectResourceTypeProvider.Attributes.References));

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Equal(6, resolvedResourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(api.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(database.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(sqlServer.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(volume.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(settings.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(secrets.EffectiveResourceId, resolvedResourceIds);

        var serviceDiscoveryVariables = await new AspNetCoreProjectServiceDiscoveryEnvironmentResolver(
            serviceProvider.GetRequiredService<ResourceGraphModel>())
            .ResolveAsync(resolution.Target!);

        Assert.Equal(
            "localhost:14334",
            serviceDiscoveryVariables["services__application-topology-sql-server__tds__0"]);
        Assert.Equal(
            "http://localhost:5138",
            serviceDiscoveryVariables["services__application-topology-settings__settings__0"]);
        Assert.Equal(
            "http://localhost:6138",
            serviceDiscoveryVariables["services__application-topology-secrets__secrets__0"]);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSettingsAndSecretsInspiredGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddIdentityProvisioningResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var identity = new ResourceDefinition(
            "settings-secrets-identity",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Built-in Identity",
                [IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind] = "built-in"
            });
        var settings = new ResourceDefinition(
            "settings-secrets-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });
        var secrets = new ResourceDefinition(
            "settings-secrets-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });
        var api = new ResourceDefinition(
            "settings-secrets-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    settings.EffectiveResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    secrets.EffectiveResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:5227",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    bool.FalseString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    bool.FalseString.ToLowerInvariant()
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "settings-and-secrets",
                [identity, settings, secrets, api],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 25, 22, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Committed, result.Commit.Summary.Status);
        Assert.Equal(4, result.Commit.Summary.AcceptedResourceCount);

        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var projectedResources = provider.GetResources().ToArray();
        var projectedApi = Assert.Single(projectedResources, resource =>
            resource.Id == api.EffectiveResourceId);
        var projectedIdentity = Assert.Single(projectedResources, resource =>
            resource.Id == identity.EffectiveResourceId);
        var projectedSettings = Assert.Single(projectedResources, resource =>
            resource.Id == settings.EffectiveResourceId);
        var projectedSecrets = Assert.Single(projectedResources, resource =>
            resource.Id == secrets.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Project, projectedApi.ResourceClass);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.ProviderId, projectedApi.Provider);
        Assert.Equal(
            [identity.EffectiveResourceId, settings.EffectiveResourceId, secrets.EffectiveResourceId],
            projectedApi.DependsOn);
        Assert.Equal(ResourceManagerClass.Infrastructure, projectedIdentity.ResourceClass);
        Assert.Equal(ResourceManagerClass.Configuration, projectedSettings.ResourceClass);
        Assert.Equal(ResourceManagerClass.SecretsVault, projectedSecrets.ResourceClass);
        AssertServiceHealthAndLiveness(projectedSettings, "settings");
        var settingsEndpoint = Assert.Single(projectedSettings.Endpoints);
        Assert.Equal("settings", settingsEndpoint.Name);
        Assert.Equal("http", settingsEndpoint.Protocol);
        Assert.Equal(5138, settingsEndpoint.TargetPort);
        Assert.Equal(
            $"http://localhost:5138/api/configuration/stores/{Uri.EscapeDataString(settings.EffectiveResourceId)}/settings",
            Assert.Single(projectedSettings.ResourceEndpointNetworkMappings).Address);
        AssertServiceHealthAndLiveness(projectedSecrets, "secrets");
        var secretsEndpoint = Assert.Single(projectedSecrets.Endpoints);
        Assert.Equal("secrets", secretsEndpoint.Name);
        Assert.Equal("http", secretsEndpoint.Protocol);
        Assert.Equal(6138, secretsEndpoint.TargetPort);
        Assert.Equal(
            $"http://localhost:6138/api/secrets/vaults/{Uri.EscapeDataString(secrets.EffectiveResourceId)}/secrets",
            Assert.Single(projectedSecrets.ResourceEndpointNetworkMappings).Address);
        Assert.Contains(projectedIdentity.ResourceCapabilities, capability =>
            capability.Id == IdentityProvisioningResourceTypeProvider.Capabilities.IdentityProvisioning.ToString());
        var settingsInspect = Assert.Single(projectedSettings.ResourceActions, action =>
            action.Id == ConfigurationStoreResourceTypeProvider.Operations.Inspect.ToString());
        var secretsInspect = Assert.Single(projectedSecrets.ResourceActions, action =>
            action.Id == SecretsVaultResourceTypeProvider.Operations.Inspect.ToString());
        Assert.Contains(projectedApi.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
        Assert.Contains(projectedApi.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Stop.ToString());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                projectedSettings,
                null,
                null,
                new EmptyResourceRegistrationStore()),
            settingsInspect));
        Assert.Null(await provider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                projectedSecrets,
                null,
                null,
                new EmptyResourceRegistrationStore()),
            secretsInspect));

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors, FormatDiagnostics(resolution.Diagnostics));
        var resolvedResourceIds = resolution.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToArray();
        Assert.Equal(4, resolvedResourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(api.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(identity.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(settings.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(secrets.EffectiveResourceId, resolvedResourceIds);

        var projectionResolver = serviceProvider.GetRequiredService<ResourceProjectionResolver>();
        var context = new ResourceProjectionContext("local", "developer");
        var resolvedApi = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == api.EffectiveResourceId);
        var resolvedIdentity = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == identity.EffectiveResourceId);
        var resolvedSettings = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == settings.EffectiveResourceId);
        var resolvedSecrets = Assert.Single(resolution.Resources, resource =>
            resource.EffectiveResourceId == secrets.EffectiveResourceId);
        var apiProjection = Assert.IsType<AspNetCoreProjectResource>(
            await projectionResolver.GetResourceProjectionAsync(resolvedApi, context));
        var identityProjection = Assert.IsType<IdentityProvisioningResource>(
            await projectionResolver.GetResourceProjectionAsync(resolvedIdentity, context));
        var settingsProjection = Assert.IsType<ConfigurationStoreResource>(
            await projectionResolver.GetResourceProjectionAsync(resolvedSettings, context));
        var secretsProjection = Assert.IsType<SecretsVaultResource>(
            await projectionResolver.GetResourceProjectionAsync(resolvedSecrets, context));

        Assert.Equal("../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj", apiProjection.ProjectPath);
        Assert.Equal("--urls http://localhost:5227", apiProjection.Arguments);
        Assert.False(apiProjection.HotReload);
        Assert.False(apiProjection.UseLaunchSettings);
        Assert.Equal("Built-in Identity", identityProjection.IdentityProvider);
        Assert.Equal("http://localhost:5138", settingsProjection.Endpoint);
        Assert.Equal("http://localhost:6138", secretsProjection.Endpoint);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_RejectsInvalidCapabilityReference()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var executable = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("storage.volume:missing", "App_Data")
                    ]))
            });

        var result = await service.ApplyTemplateAsync(
            new ResourceTemplate(
                "local-app",
                [executable],
                EnvironmentId: "local"),
            new ResourceGraphCommitContext(
                PrincipalId: "developer",
                Timestamp: new DateTimeOffset(2026, 6, 24, 17, 30, 0, TimeSpan.Zero)));

        Assert.True(result.HasErrors);
        Assert.False(result.IsCommitted);
        Assert.Equal(ResourceGraphCommitStatus.Rejected, result.Commit.Summary.Status);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceMissing &&
            diagnostic.Target == executable.EffectiveResourceId);

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.Empty(snapshot.Resources);
    }

    private static ResourceState CreateExecutableState(
        string name = "api",
        IReadOnlyList<string>? dependsOn = null,
        bool includeVolumeConsumer = true,
        IReadOnlyList<VolumeMountDefinition>? mounts = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: name.ToUpperInvariant(),
            DependsOn: ToReferences(dependsOn ?? ["storage.volume:data"]),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet",
                [ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                    ResourceAttributeValue.FromObject(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: includeVolumeConsumer
                ? new Dictionary<ResourceCapabilityId, JsonElement>
                {
                    [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                        ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                            mounts ??
                            [
                                new("storage.volume:data", "App_Data")
                            ]))
                }
                : null);

    private static IReadOnlyList<ResourceReference>? ToReferences(
        IReadOnlyList<string>? resourceIds) =>
        resourceIds?.Select(resourceId => ResourceReference.DependsOnResourceId(resourceId)).ToArray();

    private static ResourceState CreateLocalVolumeState(
        string name = "data") =>
        new(
            name,
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);

    private static ResourceAttributeValueMap CreateArtifactAttributes(
        string revisionId,
        string contentSha256) =>
        new(new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ApplicationArtifactAttributeIds.SourceKind] =
                ResourceAttributeValue.String(DeploymentArtifactSourceKinds.UploadedArtifact),
            [ApplicationArtifactAttributeIds.SourceOwner] =
                ResourceAttributeValue.String(ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner),
            [ApplicationArtifactAttributeIds.Enabled] =
                ResourceAttributeValue.Boolean(true),
            [ApplicationArtifactAttributeIds.Source] =
                ResourceAttributeValue.FromObject(new ApplicationArtifactReference(
                    "deployment-artifact:application.dotnet-app:api",
                    revisionId,
                    "zip",
                    contentSha256,
                    1024,
                    ".",
                    "dotnetPublishedOutput"))
        });

    private static ResourceState CreateAspNetCoreProjectState(
        string name,
        IReadOnlyList<ResourceReference>? references = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                $"samples/{name}/{name}.csproj"
        };

        if (references is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                ResourceAttributeValue.FromObject(references);
        }

        return new ResourceState(
            name,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: attributes);
    }

    private static ResourceResolver CreateResolver() =>
        new(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
                    })
            ],
            [
                new ExecutableApplicationResourceTypeProvider().TypeDefinition
            ]);

    private static ResourceResolver CreateAspNetCoreProjectResolver() =>
        new(
            [
                AspNetCoreProjectResourceTypeProvider.ClassDefinition
            ],
            [
                new AspNetCoreProjectResourceTypeProvider().TypeDefinition
            ]);

    private static void AssertServiceHealthAndLiveness(
        ResourceManagerResource resource,
        string endpointName)
    {
        Assert.Equal(2, resource.ResourceHealthChecks.Count);
        Assert.Contains(resource.ResourceHealthChecks, check =>
            check.Name == "health" &&
            check.Type == ResourceProbeType.Health &&
            check.Path == "/healthz" &&
            check.EndpointName == endpointName);
        Assert.Contains(resource.ResourceHealthChecks, check =>
            check.Name == "liveness" &&
            check.Type == ResourceProbeType.Liveness &&
            check.Path == "/healthz" &&
            check.EndpointName == endpointName);
    }

    private static string FormatDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message} ({diagnostic.Target})"));

    private sealed record ResourceManagerResourceRow(
        string ResourceId,
        JsonElement GraphData,
        string OperationalState);

    private sealed class ResourceManagerResourceRowProjector :
        IResourceGraphStoreProjector<ResourceManagerResourceRow>
    {
        public string GetResourceId(ResourceManagerResourceRow record) =>
            record.ResourceId;

        public ResourceState ToState(ResourceManagerResourceRow record) =>
            record.GraphData.Deserialize<ResourceRecord>()?.ToState() ??
            throw new InvalidOperationException("Resource graph payload could not be read.");

        public ResourceManagerResourceRow FromState(
            ResourceState state,
            ResourceManagerResourceRow? currentRecord = null) =>
            (currentRecord ?? new(
                state.EffectiveResourceId,
                ResourceDefinitionJson.EmptyObject,
                OperationalState: "Unknown")) with
            {
                ResourceId = state.EffectiveResourceId,
                GraphData = ResourceDefinitionJson.FromValue(ResourceRecord.FromState(state))
            };
    }

    private sealed class EmptyResourceRegistrationStore : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [];

        public ResourceRegistration? GetRegistration(string resourceId) => null;

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class RecordingResourceRegistrationStore : IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> _registrations =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            _registrations.Values.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            _registrations.GetValueOrDefault(resourceId);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            _registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                dependsOn ?? []);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            _registrations.Remove(resourceId);
            return Task.CompletedTask;
        }

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "resource-model", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                ResourceGroupId = resourceGroupId,
                DependsOn = dependsOn ?? existing.DependsOn
            };
            return Task.CompletedTask;
        }

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "resource-model", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                DependsOn = dependsOn
            };
            return Task.CompletedTask;
        }

        public Task SetIdentityAsync(
            string resourceId,
            ResourceIdentityBinding? identity,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "resource-model", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                Identity = identity
            };
            return Task.CompletedTask;
        }
    }

    private sealed class FixedResourceDefinitionBuilder(ResourceDefinition definition) :
        IResourceDefinitionBuilder
    {
        public string Name => definition.Name;

        public ResourceTypeId ResourceTypeId => definition.TypeId;

        public string? ResourceProviderId => definition.ProviderId;

        public string EffectiveResourceId => definition.EffectiveResourceId;

        public ResourceDefinition Build() => definition;
    }

    private sealed class RecordingExecutableApplicationRuntimeController :
        IExecutableApplicationRuntimeController,
        IExecutableApplicationRuntimeMonitor
    {
        private readonly List<string> _startedResourceIds = [];

        public IReadOnlyList<string> StartedResourceIds => _startedResourceIds;

        public ResourceProcessMonitoringSnapshot? MonitoringSnapshot { get; init; }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _startedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MonitoringSnapshot);
    }

    private sealed class RecordingLocalHostNetworkEndpointMappingReconciler :
        ILocalHostNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<string> _contextResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<string> ContextResourceIds => _contextResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _contextResourceIds.Add(context.Resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingVirtualNetworkEndpointMappingReconciler :
        IVirtualNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<string> _contextResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<string> ContextResourceIds => _contextResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _contextResourceIds.Add(context.Resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingNetworkEndpointMappingReconciler :
        INetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<string> _contextResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<string> ContextResourceIds => _contextResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _contextResourceIds.Add(context.Resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingMacOSHostNetworkEndpointMappingReconciler :
        IMacOSHostNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<string> _contextResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<string> ContextResourceIds => _contextResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _contextResourceIds.Add(context.Resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingLoadBalancerConfigurationApplier :
        ILoadBalancerConfigurationApplier
    {
        private readonly List<string> _appliedResourceIds = [];

        public IReadOnlyList<string> AppliedResourceIds => _appliedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyConfigurationAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _appliedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingDnsZoneNameMappingReconciler :
        IDnsZoneNameMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<string> _contextResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<string> ContextResourceIds => _contextResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
            Resource resource,
            ResourceProjectionExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _contextResourceIds.Add(context.Resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingServiceReconciler :
        IServiceReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingLocalVolumeProvisioner :
        ILocalVolumeProvisioner
    {
        private readonly List<string> _provisionedResourceIds = [];

        public IReadOnlyList<string> ProvisionedResourceIds => _provisionedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _provisionedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>(
            [
                new ResourceDefinitionDiagnostic(
                    ResourceDefinitionDiagnosticSeverity.Information,
                    "localVolume.provisioned",
                    "Provisioned local volume.",
                    resource.EffectiveResourceId)
            ]);
        }
    }

    private sealed class RecordingCloudShellVolumeProvisioner :
        ICloudShellVolumeProvisioner
    {
        private readonly List<string> _provisionedResourceIds = [];

        public IReadOnlyList<string> ProvisionedResourceIds => _provisionedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _provisionedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingStorageInspector :
        IStorageInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingDockerHostInspector :
        IDockerHostInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingContainerHostInspector :
        IContainerHostInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingConfigurationStoreInspector :
        IConfigurationStoreInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingSecretsVaultInspector :
        ISecretsVaultInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingConfigurationStoreRuntimeController :
        IConfigurationStoreRuntimeController,
        IConfigurationStoreRuntimeMonitor
    {
        public ResourceWebAppRuntimeStatus Status { get; init; } =
            ResourceWebAppRuntimeStatus.Running;

        public ResourceProcessMonitoringSnapshot? MonitoringSnapshot { get; init; }

        public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
            Status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MonitoringSnapshot);
    }

    private sealed class RecordingSecretsVaultRuntimeController :
        ISecretsVaultRuntimeController,
        ISecretsVaultRuntimeMonitor
    {
        public ResourceWebAppRuntimeStatus Status { get; init; } =
            ResourceWebAppRuntimeStatus.Running;

        public ResourceProcessMonitoringSnapshot? MonitoringSnapshot { get; init; }

        public ResourceWebAppRuntimeStatus GetStatus(Resource resource) =>
            Status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MonitoringSnapshot);
    }

    private sealed class RecordingHostConfigurationSourceInspector :
        IHostConfigurationSourceInspector
    {
        private readonly List<string> _inspectedResourceIds = [];

        public IReadOnlyList<string> InspectedResourceIds => _inspectedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> InspectAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _inspectedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingIdentityProvisioningSetupHandler :
        IIdentityProvisioningSetupHandler
    {
        private readonly List<string> _setupResourceIds = [];

        public IReadOnlyList<string> SetupResourceIds => _setupResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _setupResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingSqlServerAccessReconciler :
        ISqlServerAccessReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingRabbitMQAccessReconciler :
        IRabbitMQAccessReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];
        private readonly List<IReadOnlyList<ResourcePermissionGrant>> _reconciledGrants = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public IReadOnlyList<IReadOnlyList<ResourcePermissionGrant>> ReconciledGrants => _reconciledGrants;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAccessAsync(
            Resource resource,
            IReadOnlyList<ResourcePermissionGrant> grants,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);
            _reconciledGrants.Add(grants);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingSqlDatabaseCreationHandler :
        ISqlDatabaseCreationHandler
    {
        private readonly List<string> _createdResourceIds = [];
        private readonly List<string> _serverResourceIds = [];

        public IReadOnlyList<string> CreatedResourceIds => _createdResourceIds;

        public IReadOnlyList<string> ServerResourceIds => _serverResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
            SqlDatabaseCreationContext context,
            CancellationToken cancellationToken = default)
        {
            _createdResourceIds.Add(context.Database.EffectiveResourceId);
            _serverResourceIds.Add(context.Server.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingAspNetCoreProjectRuntimeController :
        IAspNetCoreProjectRuntimeController,
        IAspNetCoreProjectRuntimeMonitor
    {
        private readonly List<(string ResourceId, ResourceOperationId OperationId)> _executedOperations = [];

        public IReadOnlyList<(string ResourceId, ResourceOperationId OperationId)> ExecutedOperations =>
            _executedOperations;

        public AspNetCoreProjectRuntimeStatus Status { get; set; } =
            AspNetCoreProjectRuntimeStatus.Running;

        public ResourceProcessMonitoringSnapshot? MonitoringSnapshot { get; init; }

        public AspNetCoreProjectRuntimeStatus GetStatus(Resource resource) =>
            Status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            _executedOperations.Add((resource.EffectiveResourceId, operationId));

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<ResourceProcessMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(MonitoringSnapshot);
    }

    private sealed class RuntimePolicyResourceTypeProvider :
        IResourceTypeProvider,
        IResourceChangeApplyProvider
    {
        public static readonly ResourceClassId ClassId = "policy";
        public static readonly ResourceTypeId ResourceTypeId = "policy.resource";

        public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

        public static class Attributes
        {
            public static readonly ResourceAttributeId Value = "policy.value";
        }

        public bool IsRunning { get; set; }

        public bool AcceptRunningChangesWithRestart { get; set; }

        public ResourceTypeId TypeId => ResourceTypeId;

        public ResourceTypeDefinition TypeDefinition { get; } = new(
            ResourceTypeId,
            ClassId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                [Attributes.Value] = new(ValueType: ResourceAttributeValueType.String)
            });

        public bool CanValidate(Resource resource) =>
            resource.Type.TypeId == ResourceTypeId;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            Resource resource,
            ResourceProviderContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);

        public bool CanApply(ResourceChangeSet changes) =>
            changes.Resource.Type.TypeId == ResourceTypeId;

        public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
            ResourceChangeSet changes,
            ResourceChangeApplyContext context,
            CancellationToken cancellationToken = default)
        {
            var changesValue = changes.AttributeChanges.Any(change => change.AttributeId == Attributes.Value);

            if (IsRunning && changesValue && !AcceptRunningChangesWithRestart)
            {
                return ValueTask.FromResult(ResourceChangeApplyResult.Rejected(
                    changes,
                    [
                        ResourceDefinitionDiagnostic.Error(
                            "policy.changeRequiresStoppedResource",
                            "The Control Plane policy for this resource requires it to be stopped before changing 'policy.value'.",
                            Attributes.Value)
                    ]));
            }

            if (IsRunning && changesValue)
            {
                return ValueTask.FromResult(new ResourceChangeApplyResult(
                    changes,
                    changes.ProposedState,
                    [
                        ResourceDefinitionDiagnostic.Warning(
                            "policy.restartRequired",
                            "The Control Plane accepted the graph change, but this resource type requires a restart to materialize it.",
                            changes.Resource.EffectiveResourceId)
                    ]));
            }

            return ValueTask.FromResult(ResourceChangeApplyResult.Accepted(changes));
        }
    }

    private sealed class RecordingResourceManager : IResourceManager
    {
        private readonly List<ResourceRegistration> _registrations = [];

        public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

        public IReadOnlyList<ResourceRegistration> Registrations => _registrations;

        public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceRegistration>>(_registrations.ToArray());

        public Task<ResourceRegistration?> GetResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_registrations.FirstOrDefault(registration => string.Equals(
                registration.ResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)));

        public Task RegisterResourceAsync(
            RegisterResourceCommand command,
            CancellationToken cancellationToken = default)
        {
            _registrations.Add(new(
                command.ResourceId,
                command.ProviderId,
                command.ResourceGroupId,
                DateTimeOffset.UnixEpoch,
                command.DependsOn ?? []));
            ResourcesChanged?.Invoke(
                this,
                new ResourceChangeNotification(ResourceChangeKind.ResourceRegistered, command.ResourceId));
            return Task.CompletedTask;
        }

        public Task AssignResourceGroupAsync(
            AssignResourceGroupCommand command,
            CancellationToken cancellationToken = default)
        {
            var index = _registrations.FindIndex(registration => string.Equals(
                registration.ResourceId,
                command.ResourceId,
                StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                var current = _registrations[index];
                _registrations[index] = current with
                {
                    ResourceGroupId = command.ResourceGroupId,
                    DependsOn = command.DependsOn ?? []
                };
            }

            ResourcesChanged?.Invoke(
                this,
                new ResourceChangeNotification(ResourceChangeKind.ResourceGroupAssigned, command.ResourceId));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceGroup> CreateResourceGroupAsync(
            CreateResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListAvailableResourcesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListResourcesAsync(
            ResourceQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceManagerResource?> GetResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListResourceChildrenAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateResourceAsync(
            CreateResourceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
            IReadOnlyList<string> resourceIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePrincipal>> QueryResourcePrincipalsAsync(
            ResourcePrincipalQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
            ResourcePermissionGrantQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePermissionGrantStatus>> ListResourcePermissionGrantStatusesAsync(
            ResourcePermissionGrantQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference identity,
            string targetResourceId,
            string permission,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task GrantResourcePermissionAsync(
            GrantResourcePermissionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RevokeResourcePermissionAsync(
            RevokeResourcePermissionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
            string providerId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetResourceDependenciesAsync(
            SetResourceDependenciesCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetResourceIdentityAsync(
            SetResourceIdentityCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> DeleteResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> ExecuteResourceActionAsync(
            ExecuteResourceActionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> UpdateResourceImageAsync(
            UpdateResourceImageCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
            UpdateResourceReplicasCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class StaticResourceModelStateProvider(
        string resourceId,
        ResourceManagerResourceState state) : IResourceModelResourceManagerStateProvider
    {
        public ResourceManagerResourceState? GetState(Resource resource) =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)
                ? state
                : null;
    }

    private sealed class ProjectedResourceManagerStore(
        Func<IReadOnlyList<IResourceProvider>> getProviders) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => getProviders();

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<ResourceManagerResource> GetAvailableResources() =>
            GetResources();

        public IReadOnlyList<ResourceManagerResource> GetResources() =>
            Providers
                .SelectMany(provider => provider.GetResources())
                .ToArray();

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() =>
            Providers
                .OfType<IResourceModelDiagnosticProvider>()
                .SelectMany(provider => provider.GetResourceModelDiagnostics())
                .ToArray();

        public ResourceManagerClass? GetResourceTypeClass(string resourceType) => null;

        public ResourceManagerResource? GetResource(string id) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<ResourceManagerResource> GetChildren(string resourceId) =>
            GetResources()
                .Where(resource =>
                    string.Equals(resource.ParentResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            GetResource(resourceId) is not null;
    }

    private sealed class RecordingResourceOrchestratorDeploymentCoordinator(
        bool retirePreviousReplicaGroup = false) :
        IResourceOrchestratorDeploymentCoordinator
    {
        private readonly List<ResourceOrchestratorDeployment> _deployments = [];

        public IReadOnlyList<ResourceOrchestratorDeployment> Deployments => _deployments;

        public bool CanApplyDeployment(
            ResourceManagerResource resource,
            ResourceOrchestratorDeployment deployment) =>
            string.Equals(resource.Id, deployment.SourceResourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
            ResourceManagerResource resource,
            ResourceOrchestratorDeployment deployment,
            CancellationToken cancellationToken = default,
            string? triggeredBy = null,
            string? cause = null)
        {
            _deployments.Add(deployment);
            var applied = deployment with { Status = ResourceOrchestratorDeploymentStatus.Active };
            var replicaGroup = ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                deployment.Spec.Service,
                deployment.RevisionId);
            var revision = new ResourceOrchestratorRevision(
                new ResourceOrchestratorEnvironmentRevisionId(
                    $"env-{deployment.Id}-{_deployments.Count.ToString(CultureInfo.InvariantCulture)}"),
                deployment.Id,
                deployment.SourceResourceId,
                deployment.ServiceId,
                _deployments.Count,
                DateTimeOffset.UtcNow,
                ResourceOrchestratorRevisionStatus.Active,
                replicaGroup,
                ProvisionedBy: triggeredBy,
                Definition: deployment.Spec.CreateDeploymentDefinition(deployment.RevisionId));
            var previousReplicaGroup = retirePreviousReplicaGroup
                ? CreatePreviousReplicaGroup(deployment)
                : null;
            IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> retiredReplicaGroups =
                retirePreviousReplicaGroup && previousReplicaGroup is not null
                ?
                [
                    new ResourceOrchestratorReplicaGroupTearDownRequest(
                        deployment.Spec.Service with
                        {
                            RuntimeRevisionId = previousReplicaGroup.RuntimeRevisionId,
                            Workload = deployment.Spec.Service.Workload with
                            {
                                Replicas = previousReplicaGroup.RequestedReplicas,
                                ReplicasEnabled = previousReplicaGroup.RequestedReplicas > 1
                            }
                        },
                        previousReplicaGroup,
                        "Retire previous graph apply revision.")
                ]
                : [];
            return Task.FromResult(new ResourceOrchestratorDeploymentApplyResult(
                applied,
                revision,
                ResourceProcedureResult.Completed($"Applied deployment '{deployment.Id}'."),
                retiredReplicaGroups,
                previousReplicaGroup));
        }

        private static ResourceOrchestratorReplicaGroup? CreatePreviousReplicaGroup(
            ResourceOrchestratorDeployment deployment)
        {
            var service = deployment.Spec.Service with
            {
                RuntimeRevisionId = "previous-revision",
                Workload = deployment.Spec.Service.Workload with
                {
                    Replicas = 1,
                    ReplicasEnabled = false
                }
            };
            return ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(
                service,
                "previous-revision");
        }
    }

    private sealed class RecordingReplicaGroupTearDownOrchestrator :
        IResourceOrchestrator,
        IResourceOrchestratorReplicaGroupTearDown
    {
        public string Id => "default";

        public string DisplayName => "Default";

        public List<string> TornDownReplicaGroups { get; } = [];

        public bool CanExecute(
            ResourceOrchestrationContext context,
            ResourceAction action) =>
            false;

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceOrchestrationContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool CanDelete(ResourceOrchestrationContext context) => false;

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceOrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool CanTearDownReplicaGroup(
            ResourceOrchestrationContext context,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup replicaGroup) =>
            string.Equals(context.Resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(service.Name, replicaGroup.ServiceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceProcedureResult> TearDownReplicaGroupAsync(
            ResourceOrchestrationContext context,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup replicaGroup,
            CancellationToken cancellationToken = default)
        {
            TornDownReplicaGroups.Add(
                $"{service.Name}:{replicaGroup.Id}:{context.TriggeredBy}:{context.Cause}");
            return Task.FromResult(ResourceProcedureResult.Completed(
                $"Tore down replica group '{replicaGroup.Id}'."));
        }
    }

    private sealed class StaticContainerApplicationRuntimeHandler(
        ContainerApplicationRuntimeStatus status) : IContainerApplicationRuntimeHandler
    {
        public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            Resource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            Resource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
    }

    private sealed class RecordingContainerApplicationRuntimeHandler(
        ContainerApplicationRuntimeStatus status) :
        IContainerApplicationRuntimeHandler,
        IContainerApplicationOrchestratorRuntimeHandler
    {
        private readonly List<ContainerApplicationRuntimeEvent> _events = [];
        private readonly List<ContainerApplicationOrchestratorRuntimeEvent> _orchestratorEvents = [];

        public IReadOnlyList<ContainerApplicationRuntimeEvent> Events => _events;

        public IReadOnlyList<ContainerApplicationOrchestratorRuntimeEvent> OrchestratorEvents => _orchestratorEvents;

        public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
            status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            Record(resource, operationId);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            Record(resource, ContainerApplicationResourceTypeProvider.Operations.UpdateImage);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            Record(resource, ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
            Resource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            RecordOrchestrator("prepare", resource, replicaGroup, routingBindings: routingBindings);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
            Resource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            RecordOrchestrator("routing", resource, replicaGroup, routingBindings: routingBindings);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
            Resource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            RecordOrchestrator("routing-teardown", resource, replicaGroup, routingBindings: routingBindings);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
            Resource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorServiceInstance instance,
            ResourceAction action,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            CancellationToken cancellationToken = default)
        {
            RecordOrchestrator("instance", resource, replicaGroup, instance, action.Kind);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        private void Record(
            Resource resource,
            ResourceOperationId operationId)
        {
            var container = new ContainerApplicationResource(resource);
            _events.Add(new ContainerApplicationRuntimeEvent(
                operationId,
                container.Image,
                container.Replicas));
        }

        private void RecordOrchestrator(
            string stage,
            Resource resource,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            ResourceOrchestratorServiceInstance? instance = null,
            ResourceActionKind? actionKind = null,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition>? routingBindings = null)
        {
            var container = new ContainerApplicationResource(resource);
            var bindings = routingBindings ?? [];
            _orchestratorEvents.Add(new ContainerApplicationOrchestratorRuntimeEvent(
                stage,
                actionKind,
                instance?.ReplicaOrdinal,
                replicaGroup?.RequestedReplicas,
                container.Image,
                container.Replicas,
                bindings.Count,
                bindings.FirstOrDefault()?.RouteId));
        }
    }

    private sealed record ContainerApplicationRuntimeEvent(
        ResourceOperationId OperationId,
        string? Image,
        int Replicas);

    private sealed record ContainerApplicationOrchestratorRuntimeEvent(
        string Stage,
        ResourceActionKind? ActionKind,
        int? ReplicaOrdinal,
        int? RequestedReplicas,
        string? Image,
        int Replicas,
        int RoutingBindingCount,
        string? RoutingRouteId);

    private sealed class StaticResourceModelEndpointProjectionProvider(
        string resourceId) : IResourceModelResourceManagerEndpointProjectionProvider
    {
        public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(Resource resource) =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)
                ? new ResourceModelResourceManagerEndpointProjection(
                    Endpoints:
                    [
                        ResourceEndpoint.Contract(
                            "http",
                            "http",
                            ResourceExposureScope.Local,
                            5010)
                    ],
                    EndpointNetworkMappings:
                    [
                        ResourceEndpointNetworkMapping.ForEndpoint(
                            resource.EffectiveResourceId,
                            "http",
                            "http://localhost:5010")
                    ])
                : null;
    }

    private sealed class StaticResourceModelObservabilityProvider(
        string resourceId) : IResourceModelResourceManagerObservabilityProvider
    {
        public ResourceObservability? GetObservability(Resource resource) =>
            string.Equals(
                resource.EffectiveResourceId,
                resourceId,
                StringComparison.OrdinalIgnoreCase)
                ? new ResourceObservability(
                    Logs: true,
                    Traces: true,
                    Metrics: true,
                    ServiceName: resource.Name)
                : null;
    }

    private sealed class RecordingGraphApplyReconciler : IResourceModelGraphApplyReconciler
    {
        private readonly List<ResourceModelGraphDefinitionApplyReconciliationContext> _contexts = [];

        public IReadOnlyList<ResourceModelGraphDefinitionApplyReconciliationContext> Contexts => _contexts;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileAsync(
            ResourceModelGraphDefinitionApplyReconciliationContext context,
            CancellationToken cancellationToken = default)
        {
            _contexts.Add(context);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingMaterializedChangeApplier(
        ResourceTypeId typeId) : IResourceModelGraphMaterializedChangeApplier
    {
        private readonly List<ResourceModelGraphMaterializedChangeContext> _contexts = [];

        public IReadOnlyList<ResourceModelGraphMaterializedChangeContext> Contexts => _contexts;

        public bool CanApplyMaterializedChange(
            ResourceModelGraphMaterializedChangeContext context) =>
            context.ChangeSet.Resource.Type.TypeId == typeId;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyMaterializedChangeAsync(
            ResourceModelGraphMaterializedChangeContext context,
            CancellationToken cancellationToken = default)
        {
            _contexts.Add(context);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class TestResourceOrchestratorDeploymentCleanupCoordinator :
        IResourceOrchestratorDeploymentCleanupCoordinator
    {
        public async Task<ResourceProcedureResult> RunPostApplyCleanupAsync(
            ResourceManagerResource resource,
            ResourceOrchestratorDeploymentApplyResult applyResult,
            ResourceProcedureResult result,
            string? triggeredBy,
            Func<ResourceOrchestratorDeploymentApplyResult, CancellationToken, Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>>? describeTearDownsAsync,
            Func<ResourceOrchestratorReplicaGroupTearDownRequest, ResourceOrchestratorReplicaGroup, string, CancellationToken, Task<ResourceProcedureResult>> tearDownReplicaGroupAsync,
            Func<ResourceProcedureResult, ResourceProcedureResult, ResourceProcedureResult>? mergeResults = null,
            CancellationToken cancellationToken = default)
        {
            var tearDowns = applyResult.ReplicaGroupsToTearDown;
            if (tearDowns.Count == 0 && describeTearDownsAsync is not null)
            {
                tearDowns = await describeTearDownsAsync(
                    applyResult,
                    cancellationToken);
            }

            var results = new List<ResourceProcedureResult> { result };
            foreach (var tearDown in tearDowns)
            {
                var replicaGroup = tearDown.ReplicaGroup ??
                    ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(tearDown.Service);
                var reason = tearDown.Reason ?? "Deployment retired superseded replica group.";
                results.Add(await tearDownReplicaGroupAsync(
                    tearDown,
                    replicaGroup,
                    reason,
                    cancellationToken));
            }

            return ResourceProcedureResult.Combine(
                results,
                result.Message);
        }
    }

    private sealed class TestCloudShellBuilder(IServiceCollection services) : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = services;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cloudshell-resource-model-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // Test cleanup should not hide assertion failures.
            }
        }
    }
}
