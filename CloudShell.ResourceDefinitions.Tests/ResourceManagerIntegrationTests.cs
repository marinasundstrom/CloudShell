using System.Text.Json;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceManagerResourceState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceDefinitions.Tests;

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
        Assert.Equal("dotnet", projected.ResourceAttributes["executable.path"]);
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
        Assert.Equal("dotnet", projected.ResourceAttributes["executable.path"]);
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
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
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
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
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
        Assert.Single(changes.CapabilityChanges);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsDiagnosticWhenCapabilityProjectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelGraphServices(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
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
        Assert.True(await operation.CanExecuteAsync());
        Assert.False((await operation.ExecuteAsync()).HasErrors);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ReturnsDiagnosticWhenOperationProjectionIsMissing()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddSingleton<IResourceTypeProvider>(new ExecutableApplicationResourceTypeProvider());
        services.AddResourceModelGraphServices(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
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
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
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
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
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

        Assert.Equal("Executed Storage Volume Provision for data.", result.Message);
        Assert.Equal(["storage.volume:data"], provisioner.ProvisionedResourceIds);
    }

    [Fact]
    public async Task ResourceModelGraphProcedureProvider_ImportsResourceDefinitionTemplate()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState()]);
        services.AddLocalVolumeResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
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
        var template = new ResourceTemplateDefinition(
            definition.Name,
            "resource-model",
            definition.TypeId.ToString(),
            ["template-volume"],
            ResourceModelGraphProcedureProvider.ResourceDefinitionTemplateConfigurationVersion,
            ResourceDefinitionJson.FromValue(definition, JsonSerializerOptions.Web),
            definition.EffectiveResourceId);
        var registrations = new RecordingResourceRegistrationStore();

        Assert.True(provider.CanImport(template));

        var result = await provider.ImportAsync(
            template,
            new ResourceTemplateImportContext("applications", registrations, ["storage.volume:data"]));

        Assert.Equal("application.executable:api", result.ResourceId);
        Assert.Equal("Imported resource model graph resource 'api'.", result.Message);
        var resource = provider.GetResources()
            .Single(resource => resource.Id == "application.executable:api");
        Assert.Equal("application.executable:api", resource.Id);
        Assert.Equal(["storage.volume:data"], resource.DependsOn);
        Assert.Equal("API", resource.DisplayName);
        Assert.Equal("dotnet", resource.ResourceAttributes[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);

        var registration = registrations.GetRegistration(resource.Id);
        Assert.NotNull(registration);
        Assert.Equal("resource-model", registration.ProviderId);
        Assert.Equal("applications", registration.ResourceGroupId);
        Assert.Equal(["storage.volume:data"], registration.DependsOn);

        var graphModel = serviceProvider.GetRequiredService<ResourceGraphModel>();
        var imported = (await graphModel.GetSnapshotAsync()).Resources
            .Single(resource => resource.EffectiveResourceId == "application.executable:api");
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
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDeploymentAndCreatesMissingResource()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var deployment = new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            deployment,
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
        var deployment = new ResourceDeploymentDefinition(
            "local-app",
            [initialDefinition],
            EnvironmentId: "local");

        var created = await service.ApplyDeploymentAsync(
            deployment,
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
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var executable = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DependsOn: [ResourceReference.DependsOnResourceId(volume.EffectiveResourceId)],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "App_Data")
                    ]))
            });
        var deployment = new ResourceDeploymentDefinition(
            "local-app",
            [volume, executable],
            EnvironmentId: "local");

        var result = await service.ApplyDeploymentAsync(
            deployment,
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
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var host = new ResourceDefinition(
            "docker",
            ContainerHostResourceTypeProvider.ResourceTypeId);
        var container = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DependsOn:
            [
                new(
                    host.EffectiveResourceId,
                    ResourceReferenceRelationships.DependsOn,
                    ResourceReferenceAddressingModes.ResourceId,
                    TypeId: ContainerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "ghcr.io/example/api:latest",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = "2",
                [ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            TargetPort: 8080,
                            Host: "localhost",
                            Port: 5092,
                            Exposure: "Local")
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/data")
                    ]))
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "container-app",
                [host, volume, container],
                EnvironmentId: "local"),
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
        Assert.Equal("ghcr.io/example/api:latest", projectedContainer.ResourceAttributes["container.image"]);
        Assert.Equal("2", projectedContainer.ResourceAttributes["container.replicas"]);
        Assert.Equal("http://localhost:5092", projectedContainer.PrimaryEndpoint);
        Assert.Equal(8080, Assert.Single(projectedContainer.Endpoints).TargetPort);
        Assert.Equal([host.EffectiveResourceId, volume.EffectiveResourceId], projectedContainer.DependsOn);
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projectedContainer.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.Start.ToString());
        var restart = Assert.Single(projectedContainer.ResourceActions, action =>
            action.Id == ContainerApplicationResourceTypeProvider.Operations.Restart.ToString());

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

        var procedureResult = await provider.ExecuteActionAsync(procedure, restart);

        Assert.Equal("Executed Restart for api.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AcceptsDockerHostForContainerBackedWorkloads()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddSqlServerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var host = new ResourceDefinition(
            "engine",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId);
        var container = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0"
            });
        var sqlServer = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ]);

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "docker-host-workloads",
                [host, container, sqlServer],
                EnvironmentId: "local"),
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

        Assert.Equal([host.EffectiveResourceId], projectedContainer.DependsOn);
        Assert.Equal([host.EffectiveResourceId], projectedSqlServer.DependsOn);

        var resolver = serviceProvider.GetRequiredService<ResourceModelGraphResourceResolver>();
        var containerResolution = await resolver.ResolveAsync(container.EffectiveResourceId);
        var sqlResolution = await resolver.ResolveAsync(sqlServer.EffectiveResourceId);

        Assert.False(containerResolution.HasErrors, FormatDiagnostics(containerResolution.Diagnostics));
        Assert.False(sqlResolution.HasErrors, FormatDiagnostics(sqlResolution.Diagnostics));

        var projectionResolver = serviceProvider.GetRequiredService<ResourceProjectionResolver>();
        var containerProjection = Assert.IsType<ContainerApplicationResource>(
            await projectionResolver.GetResourceProjectionAsync(
                containerResolution.Target!,
                new ResourceProjectionContext("local", "developer")));
        var sqlProjection = Assert.IsType<SqlServerResource>(
            await projectionResolver.GetResourceProjectionAsync(
                sqlResolution.Target!,
                new ResourceProjectionContext("local", "developer")));

        Assert.Equal(host.EffectiveResourceId, containerProjection.ContainerHostResourceId);
        Assert.Equal(host.EffectiveResourceId, sqlProjection.ContainerHostResourceId);
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
    public async Task ResourceModelGraphDefinitionApplyService_AppliesAspNetCoreProjectAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var runtimeController = new RecordingAspNetCoreProjectRuntimeController();
        services.AddSingleton<IAspNetCoreProjectRuntimeController>(runtimeController);
        services.AddLocalVolumeResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var volume = new ResourceDefinition(
            "data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var project = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] = true,
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] = false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            Host: "localhost",
                            Port: 5010,
                            Exposure: "Local")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                            "http://localhost:5104/api/control-plane/v1/traces/ingest")
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "App_Data")
                    ])),
                [ResourceHealthCheckCapabilityIds.HealthChecks] =
                    ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                    [
                        ResourceHealthCheckDefinition.HttpLiveness(
                            "/alive",
                            endpointName: "http",
                            name: "alive",
                            intervalSeconds: 10)
                    ]))
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "project-app",
                [volume, project],
                EnvironmentId: "local"),
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
            .GetObject<AspNetCoreProjectEnvironmentVariableValue[]>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables);
        var environmentVariable = Assert.Single(environmentVariables ?? []);
        Assert.Equal("CLOUDSHELL_TRACE_INGEST_ENDPOINT", environmentVariable.Name);
        Assert.Equal(
            "http://localhost:5104/api/control-plane/v1/traces/ingest",
            environmentVariable.Value);

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
        services.AddAspNetCoreProjectResourceType();
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

        var created = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "container-host",
                [host],
                EnvironmentId: "local"),
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
        var host = new ResourceDefinition(
            "engine",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerHostResourceTypeProvider.Attributes.HostKind] = "local",
                [DockerHostResourceTypeProvider.Attributes.Endpoint] = "unix:///var/run/docker.sock",
                [DockerHostResourceTypeProvider.Attributes.Registry] = "docker.io",
                [DockerHostResourceTypeProvider.Attributes.IsDefault] = bool.TrueString.ToLowerInvariant()
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "docker-host",
                [host],
                EnvironmentId: "local"),
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
        var container = new ResourceDefinition(
            "api",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerContainerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0",
                [DockerContainerResourceTypeProvider.Attributes.ContainerRegistry] = "registry.local"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "docker-container",
                [container],
                EnvironmentId: "local"),
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
            capability.Id == DockerContainerResourceTypeProvider.Capabilities.Monitoring.ToString());
        Assert.Contains(projectedContainer.ResourceCapabilities, capability =>
            capability.Id == DockerContainerResourceTypeProvider.Capabilities.LogSources.ToString());
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        var configurationStore = new ResourceDefinition(
            "settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        Assert.Equal("store", projectedStore.ResourceAttributes["configuration.kind"]);
        Assert.Equal("http://localhost:5138", projectedStore.ResourceAttributes["configuration.endpoint"]);
        Assert.Equal("0", projectedStore.ResourceAttributes["configuration.entries.count"]);
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(projectedStore.ResourceCapabilities, capability =>
            capability.Id == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        AssertServiceHealthAndLiveness(projectedStore, "entries");
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
        Assert.Equal(0, projection.EntryCount);

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
        var hostConfiguration = new ResourceDefinition(
            "host-settings",
            HostConfigurationSourceResourceTypeProvider.ResourceTypeId,
            ProviderId: HostConfigurationSourceResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>());

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "host-configuration",
                [hostConfiguration],
                EnvironmentId: "local"),
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
        Assert.Equal("0", projectedSource.ResourceAttributes["configuration.entries.count"]);
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
        var host = new ResourceDefinition(
            "engine",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId);
        var target = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0"
            });
        var loadBalancer = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            ProviderId: LoadBalancerResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    host.EffectiveResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    target.EffectiveResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.HostResourceId] = host.EffectiveResourceId
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "load-balancer",
                [host, target, loadBalancer],
                EnvironmentId: "local"),
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesHostVirtualNetworkInspiredGraphAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddAspNetCoreProjectResourceType();
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        var zone = new ResourceDefinition(
            "local",
            DnsZoneResourceTypeProvider.ResourceTypeId,
            ProviderId: DnsZoneResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "local",
                [DnsZoneResourceTypeProvider.Attributes.Provider] = "hosts-file"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "dns",
                [zone],
                EnvironmentId: "local"),
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
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var zone = new ResourceDefinition(
            "local",
            DnsZoneResourceTypeProvider.ResourceTypeId,
            ProviderId: DnsZoneResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "local"
            });
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
                ResourceReference.DependsOnResourceId(
                    zone.EffectiveResourceId,
                    typeId: DnsZoneResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(target.EffectiveResourceId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NameMappingResourceTypeProvider.Attributes.HostName] = "api.local",
                [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http",
                [NameMappingResourceTypeProvider.Attributes.Exposure] = "Public"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "name-mapping",
                [zone, target, mapping],
                EnvironmentId: "local"),
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
        Assert.Equal([zone.EffectiveResourceId, target.EffectiveResourceId], projectedMapping.DependsOn);
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
        Assert.Equal([zone.EffectiveResourceId, target.EffectiveResourceId], projection.References.Select(reference => reference.Value));
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
                ResourceReference.DependsOnResourceId(target.EffectiveResourceId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NameMappingResourceTypeProvider.Attributes.HostName] = "api.local",
                [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.ResourceCapabilityReferenceInvalid &&
            diagnostic.Message.Contains("must reference a DNS zone", StringComparison.OrdinalIgnoreCase));
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
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var api = new ResourceDefinition(
            "application-topology-api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/application-topology-api:1.0"
            });
        var network = new ResourceDefinition(
            "application-topology-local",
            NetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: NetworkResourceTypeProvider.ProviderId);
        var apiService = new ResourceDefinition(
            "application-topology-api-service",
            ServiceResourceTypeProvider.ResourceTypeId,
            ProviderId: ServiceResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    api.EffectiveResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    network.EffectiveResourceId,
                    typeId: NetworkResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ServiceResourceTypeProvider.Attributes.RoutingMode] = "logical"
            });
        var zone = new ResourceDefinition(
            "application-topology-local",
            DnsZoneResourceTypeProvider.ResourceTypeId,
            ProviderId: DnsZoneResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "application-topology.cloudshell.local",
                [DnsZoneResourceTypeProvider.Attributes.Provider] = "hosts-file"
            });
        var mapping = new ResourceDefinition(
            "application-topology-api-local",
            NameMappingResourceTypeProvider.ResourceTypeId,
            ProviderId: NameMappingResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    zone.EffectiveResourceId,
                    typeId: DnsZoneResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    apiService.EffectiveResourceId,
                    typeId: ServiceResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [NameMappingResourceTypeProvider.Attributes.HostName] =
                    "api.application-topology.cloudshell.local",
                [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http",
                [NameMappingResourceTypeProvider.Attributes.Exposure] = "Public"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "application-exposure",
                [api, network, apiService, zone, mapping],
                EnvironmentId: "local"),
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
        Assert.Equal([zone.EffectiveResourceId, apiService.EffectiveResourceId], projectedMapping.DependsOn);
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
        Assert.Equal(5, resolvedResourceIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains(mapping.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(zone.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(apiService.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(api.EffectiveResourceId, resolvedResourceIds);
        Assert.Contains(network.EffectiveResourceId, resolvedResourceIds);
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
        var storage = new ResourceDefinition(
            "local",
            StorageResourceTypeProvider.ResourceTypeId,
            ProviderId: StorageResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [StorageResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                [StorageResourceTypeProvider.Attributes.Location] = "Data/storage/local"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "storage",
                [storage],
                EnvironmentId: "local"),
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
            resource.Id == storage.EffectiveResourceId);

        Assert.Equal(ResourceManagerClass.Storage, projectedStorage.ResourceClass);
        Assert.Equal(StorageResourceTypeProvider.ProviderId, projectedStorage.Provider);
        Assert.Equal("provider", projectedStorage.ResourceAttributes["storage.kind"]);
        Assert.Equal("Local Storage", projectedStorage.ResourceAttributes["storage.provider"]);
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
            .ResolveAsync(storage.EffectiveResourceId);

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
        Assert.Equal([storage.EffectiveResourceId], inspector.InspectedResourceIds);
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
        var storage = new ResourceDefinition(
            "local",
            StorageResourceTypeProvider.ResourceTypeId,
            ProviderId: StorageResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [StorageResourceTypeProvider.Attributes.Provider] = "Local Storage",
                [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem"
            });
        var volume = new ResourceDefinition(
            "data",
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
                [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "data",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = bool.TrueString.ToLowerInvariant()
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "storage-volume",
                [storage, volume],
                EnvironmentId: "local"),
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
        Assert.Equal("Local Storage", projectedVolume.ResourceAttributes["storage.volume.provider"]);
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        var target = new ResourceDefinition(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/api:1.0"
            });
        var network = new ResourceDefinition(
            "default",
            NetworkResourceTypeProvider.ResourceTypeId,
            ProviderId: NetworkResourceTypeProvider.ProviderId);
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
                    network.EffectiveResourceId,
                    typeId: NetworkResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ServiceResourceTypeProvider.Attributes.RoutingMode] = "logical"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "service",
                [target, network, definition],
                EnvironmentId: "local"),
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
            capability.Id == ServiceResourceTypeProvider.Capabilities.EndpointSource.ToString());
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        var vault = new ResourceDefinition(
            "vault",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        Assert.Equal("vault", projectedVault.ResourceAttributes["secrets.kind"]);
        Assert.Equal("http://localhost:6138", projectedVault.ResourceAttributes["secrets.endpoint"]);
        Assert.Equal("0", projectedVault.ResourceAttributes["secrets.entries.count"]);
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
        var identity = new ResourceDefinition(
            "built-in",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Built-in Identity",
                [IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind] = "built-in"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "identity-provisioning",
                [identity],
                EnvironmentId: "local"),
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
        var volume = new ResourceDefinition(
            "sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var sql = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlServerResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new SqlServerConfiguration(
                    [
                        new("appdb", "Application DB", EnsureCreated: true)
                    ]))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "sql-app",
                [volume, sql],
                EnvironmentId: "local"),
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
        Assert.Equal("2022", projectedSql.ResourceAttributes["sqlserver.version"]);
        Assert.Equal([volume.EffectiveResourceId], projectedSql.DependsOn);
        var reconcile = Assert.Single(projectedSql.ResourceActions, action =>
            action.Id == SqlServerResourceTypeProvider.Operations.ReconcileAccess.ToString());

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(sql.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [sql.EffectiveResourceId, volume.EffectiveResourceId],
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
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSqlDatabaseAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        var creationHandler = new RecordingSqlDatabaseCreationHandler();
        services.AddSingleton<ISqlDatabaseCreationHandler>(creationHandler);
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var server = new ResourceDefinition(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId);
        var database = new ResourceDefinition(
            "appdb",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlDatabaseResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    server.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "appdb",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = bool.TrueString.ToLowerInvariant()
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "sql-database-app",
                [server, database],
                EnvironmentId: "local"),
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
            [database.EffectiveResourceId, server.EffectiveResourceId],
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlServerResourceTypeProvider.Attributes.Version] = "2022",
                [SqlServerResourceTypeProvider.Attributes.Edition] = "Developer"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [SqlServerResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new SqlServerConfiguration(
                    [
                        new("application_topology", "Application Topology", EnsureCreated: true)
                    ]))
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerProjections();
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
            serviceDiscoveryVariables["services__application-topology-settings__entries__0"]);
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
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerProjections();
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
        AssertServiceHealthAndLiveness(projectedSettings, "entries");
        var settingsEndpoint = Assert.Single(projectedSettings.Endpoints);
        Assert.Equal("entries", settingsEndpoint.Name);
        Assert.Equal("http", settingsEndpoint.Protocol);
        Assert.Equal(5138, settingsEndpoint.TargetPort);
        Assert.Equal(
            $"http://localhost:5138/api/configuration/stores/{Uri.EscapeDataString(settings.EffectiveResourceId)}/entries",
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

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
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
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
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
    }

    private sealed class RecordingExecutableApplicationRuntimeController :
        IExecutableApplicationRuntimeController
    {
        private readonly List<string> _startedResourceIds = [];

        public IReadOnlyList<string> StartedResourceIds => _startedResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> StartAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _startedResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingLocalHostNetworkEndpointMappingReconciler :
        ILocalHostNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingVirtualNetworkEndpointMappingReconciler :
        IVirtualNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingNetworkEndpointMappingReconciler :
        INetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingMacOSHostNetworkEndpointMappingReconciler :
        IMacOSHostNetworkEndpointMappingReconciler
    {
        private readonly List<string> _reconciledResourceIds = [];

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileEndpointMappingsAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

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

        public IReadOnlyList<string> ReconciledResourceIds => _reconciledResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileNameMappingsAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _reconciledResourceIds.Add(resource.EffectiveResourceId);

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

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
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

    private sealed class RecordingSqlDatabaseCreationHandler :
        ISqlDatabaseCreationHandler
    {
        private readonly List<string> _createdResourceIds = [];

        public IReadOnlyList<string> CreatedResourceIds => _createdResourceIds;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> EnsureCreatedAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            _createdResourceIds.Add(resource.EffectiveResourceId);

            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }

    private sealed class RecordingAspNetCoreProjectRuntimeController :
        IAspNetCoreProjectRuntimeController
    {
        private readonly List<(string ResourceId, ResourceOperationId OperationId)> _executedOperations = [];

        public IReadOnlyList<(string ResourceId, ResourceOperationId OperationId)> ExecutedOperations =>
            _executedOperations;

        public AspNetCoreProjectRuntimeStatus Status { get; set; } =
            AspNetCoreProjectRuntimeStatus.Running;

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
}
