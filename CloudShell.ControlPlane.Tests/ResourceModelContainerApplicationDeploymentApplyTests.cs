using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceModelContainerApplicationDeploymentApplyTests
{
    [Fact]
    public async Task ApplyDefinitionsAsync_ReconcilesContainerAppImageAndReplicaChangesThroughDeploymentCoordinator()
    {
        var runtimeHandler = new RecordingContainerApplicationOrchestratorRuntimeHandler();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var replicaStore = new InMemoryResourceReplicaGroupReconciliationStore();
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddResourceModelGraphServices();
        services.AddContainerHostResourceType();
        services.AddSingleton<IContainerApplicationRuntimeHandler>(runtimeHandler);
        services.AddSingleton<IContainerApplicationOrchestratorRuntimeHandler>(runtimeHandler);
        services.AddContainerApplicationResourceType();
        services.AddBuiltInProviderResourceManagerIntegration("resource-model", "Resource model");
        services.AddSingleton<IResourceRegistrationStore>(new EmptyResourceRegistrationStore());
        services.AddSingleton<IResourceOrchestratorDeploymentStore>(deploymentStore);
        services.AddSingleton<IResourceReplicaGroupReconciliationStore>(replicaStore);
        services.AddSingleton<ResourceOrchestratorSelectionStore>(_ =>
            new ResourceOrchestratorSelectionStore(
                new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
                new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions())));
        services.AddScoped<IResourceManagerStore>(serviceProvider =>
            new ProjectedResourceManagerStore(() =>
                serviceProvider.GetServices<IResourceProvider>().ToArray()));
        services.AddScoped<IResourceOrchestratorDeploymentApplier>(serviceProvider =>
            new DefaultResourceDeploymentService(
                serviceProvider.GetRequiredService<IResourceOrchestratorDeploymentStore>(),
                serviceProvider.GetRequiredService<IResourceReplicaGroupReconciliationStore>()));
        services.AddScoped<IResourceOrchestrator, DefaultResourceOrchestrator>();
        services.AddScoped<ResourceDeploymentService>();
        services.AddScoped<IResourceOrchestratorDeploymentCoordinator>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceDeploymentService>());
        services.AddScoped<IResourceOrchestratorDeploymentCleanupCoordinator, ResourceOrchestratorDeploymentCleanupCoordinator>();

        await using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceDefinitionGraphBuilder();
        var host = graph
            .AddContainerHost("docker")
            .UseDocker();
        var container = graph
            .AddContainerApplication("api")
            .UseContainerHost(host)
            .WithImage("ghcr.io/example/api:latest")
            .WithReplicas(2)
            .WithCookieSessionAffinity("CloudShellReplica", durationSeconds: 3600)
            .WithHttpEndpoint(targetPort: 8080, port: 8080);

        var initialApply = await apply.ApplyTemplateAsync(
            graph.BuildTemplate("container-app", environmentId: "local"),
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(initialApply.HasErrors, FormatDiagnostics(initialApply.Diagnostics));
        Assert.Empty(runtimeHandler.Events);
        Assert.Empty(deploymentStore.List());

        var initialDeployment = await ApplyContainerDefinitionAsync(
            apply,
            deploymentStore,
            container,
            "ghcr.io/example/api:v2",
            replicas: 2);
        Assert.Equal(
            ["prepare:Start:1/2", "instance:Start:1/2", "instance:Start:2/2", "routing:2/2"],
            runtimeHandler.DrainEventSummary());

        await ApplyContainerDefinitionAsync(
            apply,
            deploymentStore,
            container,
            image: null,
            replicas: 4);
        Assert.Equal(
            ["prepare:Start:1/4", "instance:Start:3/4", "instance:Start:4/4", "routing:4/4"],
            runtimeHandler.DrainEventSummary());

        await ApplyContainerDefinitionAsync(
            apply,
            deploymentStore,
            container,
            image: null,
            replicas: 2);
        Assert.Equal(
            ["routing:2/2", "instance:Stop:4/4", "instance:Stop:3/4"],
            runtimeHandler.DrainEventSummary());

        var replacementDeployment = await ApplyContainerDefinitionAsync(
            apply,
            deploymentStore,
            container,
            "ghcr.io/example/api:v3",
            replicas: 3);
        Assert.NotEqual(initialDeployment.RevisionId, replacementDeployment.RevisionId);
        AssertDeploymentCookieSessionAffinity(replacementDeployment);
        Assert.Equal(
            [
                "prepare:Start:1/3",
                "instance:Start:1/3",
                "instance:Start:2/3",
                "instance:Start:3/3",
                "routing:3/3",
                "instance:Stop:1/2",
                "instance:Stop:2/2"
            ],
            runtimeHandler.DrainEventSummary());

        var deploymentRecords = deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: container.EffectiveResourceId,
                MaxRecords: 10))
            .OrderBy(record => record.CompletedAt ?? record.StartedAt)
            .ToArray();
        Assert.Equal([2, 4, 2, 3], deploymentRecords.Select(record =>
            record.ReplicaGroup?.RequestedReplicaSlots ?? 0).ToArray());
    }

    [Fact]
    public async Task ReconcileNameMappingsAsync_PublishesContainerAppVirtualNetworkEndpointAsStableAppName()
    {
        var publisher = new RecordingNamePublishingProvider("test-dns");
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddResourceModelGraphServices();
        services.AddContainerApplicationResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddDnsZoneResourceType();
        services.AddNameMappingResourceType();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddSingleton<INamePublishingProvider>(publisher);
        services.AddSingleton<IDnsZoneNameMappingReconciler, ResourceModelGraphDnsZoneNameMappingReconciler>();

        await using var serviceProvider = services.BuildServiceProvider();
        var apply = serviceProvider.GetRequiredService<ResourceModelGraphDefinitionApplyService>();
        var graph = new ResourceDefinitionGraphBuilder();
        var network = graph.AddVirtualNetwork("apps");
        var app = graph
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
        var zone = graph
            .AddDnsZone("apps-internal", "internal.cloudshell.test")
            .WithProvider(publisher.ProviderName)
            .MapHost(
                "api.internal.cloudshell.test",
                app,
                endpointName: "vnet-http",
                exposure: "Private");

        var result = await apply.ApplyTemplateAsync(
            graph.BuildTemplate("container-app-dns", environmentId: "local"),
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        var zoneResource = await ResolveGraphResourceAsync(
            serviceProvider,
            zone.EffectiveResourceId);
        var diagnostics = await serviceProvider
            .GetRequiredService<IDnsZoneNameMappingReconciler>()
            .ReconcileNameMappingsAsync(
                zoneResource,
                new ResourceProjectionExecutionContext(zoneResource));

        Assert.DoesNotContain(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error);
        var context = Assert.Single(publisher.Contexts);
        var mapping = Assert.Single(context.Mappings);
        Assert.Equal("api.internal.cloudshell.test", mapping.Mapping.HostName);
        Assert.Equal(app.EffectiveResourceId, mapping.TargetResource.Id);
        Assert.Equal("vnet-http", mapping.TargetEndpoint?.Name);
        Assert.Equal("http://10.42.0.20:80", mapping.TargetEndpointNetworkMapping?.Address);
        Assert.Equal(network.EffectiveResourceId, mapping.TargetEndpointNetworkMapping?.NetworkResourceId);
    }

    private static async Task<ResourceOrchestratorDeployment> ApplyContainerDefinitionAsync(
        ResourceModelGraphDefinitionApplyService apply,
        IResourceOrchestratorDeploymentStore deploymentStore,
        ContainerApplicationResourceDefinitionBuilder container,
        string? image,
        int replicas)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = replicas
        };
        if (!string.IsNullOrWhiteSpace(image))
        {
            attributes[ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image;
        }

        var result = await apply.ApplyDefinitionsAsync(
            [
                new ResourceDefinition(
                    container.Name,
                    ContainerApplicationResourceTypeProvider.ResourceTypeId,
                    ResourceId: container.EffectiveResourceId,
                    Attributes: new ResourceAttributeValueMap(attributes))
            ],
            new ResourceGraphCommitContext(
                EnvironmentId: "local",
                PrincipalId: "developer"));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        return deploymentStore
            .List(new ResourceOrchestratorDeploymentQuery(
                SourceResourceId: container.EffectiveResourceId,
                MaxRecords: 1))
            .Single()
            .Deployment;
    }

    private static void AssertDeploymentCookieSessionAffinity(
        ResourceOrchestratorDeployment deployment)
    {
        var routingBinding = deployment.Spec.Definition
            ?.DeploymentServices
            .SelectMany(service => service.RoutingBindingDefinitions)
            .Single();

        Assert.NotNull(routingBinding);
        Assert.NotNull(routingBinding.SessionAffinity);
        Assert.Equal(ResourceOrchestratorSessionAffinityMode.Cookie, routingBinding.SessionAffinity.Mode);
        Assert.Equal("CloudShellReplica", routingBinding.SessionAffinity.CookieName);
        Assert.Equal(3600, routingBinding.SessionAffinity.DurationSeconds);
    }

    private static async Task<ResourceModelResource> ResolveGraphResourceAsync(
        IServiceProvider serviceProvider,
        string resourceId)
    {
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var state = snapshot.Resources.Single(resource =>
            string.Equals(resource.EffectiveResourceId, resourceId, StringComparison.OrdinalIgnoreCase));
        return serviceProvider
            .GetRequiredService<ResourceResolver>()
            .Resolve(state);
    }

    private static string FormatDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
            $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}"));

    private sealed class RecordingContainerApplicationOrchestratorRuntimeHandler :
        IContainerApplicationRuntimeHandler,
        IContainerApplicationOrchestratorRuntimeHandler
    {
        private readonly List<RuntimeEvent> _events = [];

        public IReadOnlyList<RuntimeEvent> Events => _events;

        public ContainerApplicationRuntimeStatus GetStatus(ResourceModelResource resource) =>
            ContainerApplicationRuntimeStatus.Running;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            ResourceModelResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> PrepareOrchestratorServiceAsync(
            ResourceModelResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            _events.Add(RuntimeEvent.ForGroup("prepare", ResourceActionKind.Start, replicaGroup));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ReconcileOrchestratorServiceRoutingAsync(
            ResourceModelResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            _events.Add(RuntimeEvent.ForGroup("routing", null, replicaGroup));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> TearDownOrchestratorServiceRoutingAsync(
            ResourceModelResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> routingBindings,
            CancellationToken cancellationToken = default)
        {
            _events.Add(RuntimeEvent.ForGroup("routing-teardown", ResourceActionKind.Stop, replicaGroup));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteOrchestratorServiceInstanceAsync(
            ResourceModelResource resource,
            ResourceOrchestratorService service,
            ResourceOrchestratorServiceInstance instance,
            ResourceAction action,
            ResourceOrchestratorReplicaGroup? replicaGroup,
            CancellationToken cancellationToken = default)
        {
            _events.Add(RuntimeEvent.ForInstance("instance", action.Kind, instance));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public IReadOnlyList<string> DrainEventSummary()
        {
            var summary = _events.Select(@event => @event.ToSummary()).ToArray();
            _events.Clear();
            return summary;
        }
    }

    private sealed record RuntimeEvent(
        string Stage,
        ResourceActionKind? Action,
        int ReplicaOrdinal,
        int ReplicaCount)
    {
        public static RuntimeEvent ForGroup(
            string stage,
            ResourceActionKind? action,
            ResourceOrchestratorReplicaGroup? replicaGroup)
        {
            var firstInstance = replicaGroup?.Instances.FirstOrDefault();
            return new RuntimeEvent(
                stage,
                action,
                firstInstance?.ReplicaOrdinal ?? 0,
                replicaGroup?.RequestedReplicaSlots ?? 0);
        }

        public static RuntimeEvent ForInstance(
            string stage,
            ResourceActionKind action,
            ResourceOrchestratorServiceInstance instance) =>
            new(stage, action, instance.ReplicaOrdinal, instance.ReplicaCount);

        public string ToSummary() =>
            Action is null
                ? $"{Stage}:{ReplicaCount}/{ReplicaCount}"
                : $"{Stage}:{Action}:{ReplicaOrdinal}/{ReplicaCount}";
    }

    private sealed class RecordingNamePublishingProvider(string providerName) : INamePublishingProvider
    {
        private readonly List<DnsNamePublishingContext> _contexts = [];

        public string ProviderName { get; } = providerName;

        public IReadOnlyList<DnsNamePublishingContext> Contexts => _contexts;

        public bool CanPublish(DnsNamePublishingContext context) =>
            string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceProcedureResult> ReconcileAsync(
            DnsNamePublishingContext context,
            CancellationToken cancellationToken = default)
        {
            _contexts.Add(context);
            return Task.FromResult(ResourceProcedureResult.Completed(
                $"Published {context.Mappings.Count} name mapping(s)."));
        }
    }

    private sealed class ProjectedResourceManagerStore(
        Func<IReadOnlyList<IResourceProvider>> providers) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => providers();

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<ResourceManagerResource> GetAvailableResources() => GetResources();

        public IReadOnlyList<ResourceManagerResource> GetResources() =>
            Providers
                .SelectMany(provider => provider.GetResources())
                .ToArray();

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceManagerClass? GetResourceTypeClass(string resourceType) => null;

        public ResourceManagerResource? GetResource(string id) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<ResourceManagerResource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            GetResource(resourceId) is not null;
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) :
        IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue => currentValue;

        public TOptions Get(string? name) => currentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
