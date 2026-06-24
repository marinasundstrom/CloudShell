using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

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
        Assert.Contains(projected.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
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
            .ResolveReferenceAsync(ResourceReference.ResourceId(volume.EffectiveResourceId));

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        Assert.Equal(volume.EffectiveResourceId, resolution.Reference.Value);
        Assert.Equal(volume.EffectiveResourceId, resolution.Resource?.EffectiveResourceId);
        Assert.Equal(LocalVolumeResourceTypeProvider.ResourceTypeId, resolution.Resource?.Type.TypeId);
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
    public async Task ResourceModelGraphProcedureProvider_ExecutesCustomOperationProjection()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateLocalVolumeState()]);
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
            DependsOn: [ResourceReference.ResourceId(volume.EffectiveResourceId)],
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
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "ghcr.io/example/api:latest",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = "2"
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
    public async Task ResourceModelGraphDefinitionApplyService_AppliesAspNetCoreProjectAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] = "--urls http://localhost:5010",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] = bool.TrueString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] = bool.FalseString.ToLowerInvariant()
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "App_Data")
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
        Assert.Equal("--urls http://localhost:5010", projectedProject.ResourceAttributes["project.arguments"]);
        Assert.Equal(bool.TrueString.ToLowerInvariant(), projectedProject.ResourceAttributes["project.hotReload"]);
        Assert.Equal([volume.EffectiveResourceId], projectedProject.DependsOn);
        Assert.Contains(projectedProject.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projectedProject.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
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

        var procedure = new ResourceProcedureContext(
            projectedProject,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, restart));

        var procedureResult = await provider.ExecuteActionAsync(procedure, restart);

        Assert.Equal("Executed Restart for api.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesContainerHostAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDockerHostAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesConfigurationStoreAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138",
                [ConfigurationStoreResourceTypeProvider.Attributes.EntryCount] = "3"
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
        Assert.Equal("3", projectedStore.ResourceAttributes["configuration.entries"]);
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
        Assert.Equal(3, projection.EntryCount);

        var procedure = new ResourceProcedureContext(
            projectedStore,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Configuration Store Inspect for settings.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesHostConfigurationSourceAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddHostConfigurationSourceResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var hostConfiguration = new ResourceDefinition(
            "host-settings",
            HostConfigurationSourceResourceTypeProvider.ResourceTypeId,
            ProviderId: HostConfigurationSourceResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [HostConfigurationSourceResourceTypeProvider.Attributes.EntryCount] = "4"
            });

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
        Assert.Equal("4", projectedSource.ResourceAttributes["configuration.entries"]);
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
        Assert.Equal(4, projection.EntryCount);

        var procedure = new ResourceProcedureContext(
            projectedSource,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Configuration Host Inspect for host-settings.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesLoadBalancerAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLoadBalancerResourceType();
        services.AddResourceModelGraphServices();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var service = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var loadBalancer = new ResourceDefinition(
            "edge",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            ProviderId: LoadBalancerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.HostResourceId] = "docker:engine",
                [LoadBalancerResourceTypeProvider.Attributes.EntrypointCount] = "2",
                [LoadBalancerResourceTypeProvider.Attributes.RouteCount] = "3",
                [LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount] = "2",
                [LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount] = "1",
                [LoadBalancerResourceTypeProvider.Attributes.EndpointCount] = "2"
            });

        var result = await service.ApplyDeploymentAsync(
            new ResourceDeploymentDefinition(
                "load-balancer",
                [loadBalancer],
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
        Assert.Equal("docker:engine", projectedLoadBalancer.ResourceAttributes["loadBalancer.hostResourceId"]);
        Assert.Equal("3", projectedLoadBalancer.ResourceAttributes["loadBalancer.routes"]);
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
        Assert.Equal(3, projection.RouteCount);

        var procedure = new ResourceProcedureContext(
            projectedLoadBalancer,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, apply));

        var procedureResult = await provider.ExecuteActionAsync(procedure, apply);

        Assert.Equal("Executed ApplyLoadBalancerConfiguration for edge.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesNetworkAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesDnsZoneAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
                ResourceReference.ResourceId(zone.EffectiveResourceId),
                ResourceReference.ResourceId(target.EffectiveResourceId)
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
    public async Task ResourceModelGraphDefinitionApplyService_AppliesStorageAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesCloudShellVolumeAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
            DependsOn: [ResourceReference.ResourceId(storage.EffectiveResourceId)],
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSecretsVaultAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138",
                [SecretsVaultResourceTypeProvider.Attributes.SecretCount] = "2"
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
        Assert.Equal("2", projectedVault.ResourceAttributes["secrets.count"]);
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
        Assert.Equal(2, projection.SecretCount);

        var procedure = new ResourceProcedureContext(
            projectedVault,
            null,
            null,
            new EmptyResourceRegistrationStore());

        Assert.Null(await provider.GetActionUnavailableReasonAsync(procedure, inspect));

        var procedureResult = await provider.ExecuteActionAsync(procedure, inspect);

        Assert.Equal("Executed Secrets Vault Inspect for vault.", procedureResult.Message);
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSqlServerAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
    }

    [Fact]
    public async Task ResourceModelGraphDefinitionApplyService_AppliesSqlDatabaseAcrossProviderBoundaries()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
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
            DependsOn: [ResourceReference.ResourceId(server.EffectiveResourceId)],
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
        bool includeVolumeConsumer = true) =>
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
                        [
                            new("storage.volume:data", "App_Data")
                        ]))
                }
                : null);

    private static IReadOnlyList<ResourceReference>? ToReferences(
        IReadOnlyList<string>? resourceIds) =>
        resourceIds?.Select(resourceId => ResourceReference.ResourceId(resourceId)).ToArray();

    private static ResourceState CreateLocalVolumeState(
        string name = "data") =>
        new(
            name,
            LocalVolumeResourceTypeProvider.ResourceTypeId,
            ProviderId: LocalVolumeResourceTypeProvider.ProviderId);

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
}
