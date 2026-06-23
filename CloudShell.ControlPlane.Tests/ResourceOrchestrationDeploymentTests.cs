using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceOrchestrationDeploymentTests
{
    [Fact]
    public async Task ApplyDeploymentAsync_AppliesDeploymentServiceSpec()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var resourceEvents = new InMemoryResourceEventStore();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, resourceEvents, deploymentStore);
        var deployment = CreateDeployment(resource.Id, "default", replicas: 3);

        var result = await deployments.ApplyDeploymentAsync(
            resource,
            deployment,
            triggeredBy: "tests",
            cause: "Container app deployment.");

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.Equal(deployment.RevisionId, result.Revision.Id);
        Assert.Equal(deployment.Id, result.Revision.DeploymentId);
        Assert.Equal(deployment.SourceResourceId, result.Revision.SourceResourceId);
        Assert.Equal(deployment.ServiceId, result.Revision.ServiceId);
        Assert.Equal(1, result.Revision.RevisionNumber);
        Assert.Equal(ResourceOrchestratorRevisionStatus.Active, result.Revision.Status);
        Assert.NotNull(result.Revision.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", result.Revision.ReplicaGroup.Id);
        Assert.Equal(deployment.ServiceId, result.Revision.ReplicaGroup.ServiceId);
        Assert.Equal(deployment.RevisionId, result.Revision.ReplicaGroup.RuntimeRevisionId);
        Assert.Equal(3, result.Revision.ReplicaGroup.RequestedReplicas);
        Assert.Equal(3, result.Revision.ReplicaGroup.MaterializedReplicas);
        var preparedService = Assert.Single(provider.PreparedServices);
        Assert.Equal(deployment.Spec.Service.Name, preparedService.Name);
        Assert.Equal(deployment.RevisionId, preparedService.RuntimeRevisionId);
        var preparedContext = Assert.Single(provider.PreparedContexts);
        Assert.NotNull(preparedContext.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", preparedContext.ReplicaGroup.Id);
        Assert.Equal(deployment.ServiceId, preparedContext.ReplicaGroup.ServiceId);
        Assert.Equal(deployment.RevisionId, preparedContext.ReplicaGroup.RuntimeRevisionId);
        Assert.Equal(3, preparedContext.ReplicaGroup.RequestedReplicas);
        Assert.Equal(3, preparedContext.ReplicaGroup.MaterializedReplicas);
        Assert.Equal(
            [1, 2, 3],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.ReplicaOrdinal)
                .Order()
                .ToArray());
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2",
                "cloudshell-application-api-rev-2-replica-3"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(deployment.RevisionId, instance.Instance.RuntimeRevisionId));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(preparedContext.ReplicaGroup, instance.ReplicaGroup));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(deployment.Spec.Service.Name, instance.Service.Name));
        var events = resourceEvents
            .GetEvents(new ResourceEventQuery(ResourceId: resource.Id))
            .Reverse()
            .ToArray();
        Assert.Equal(
            [
                ResourceEventTypes.Events.Deployment.Applying,
                ResourceEventTypes.Events.Deployment.ServiceReconciling,
                ResourceEventTypes.Events.Deployment.ServiceReconciled,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                ResourceEventTypes.Events.Deployment.RoutingUpdating,
                ResourceEventTypes.Events.Deployment.RoutingUpdated,
                ResourceEventTypes.Events.Deployment.Applied
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
        Assert.All(
            events,
            resourceEvent => Assert.Equal("tests", resourceEvent.TriggeredBy));
        Assert.All(
            events.Where(resourceEvent =>
                resourceEvent.EventType != ResourceEventTypes.Events.Deployment.Applied),
            resourceEvent => Assert.Contains(
                "Cause: Container app deployment.",
                resourceEvent.Message,
                StringComparison.Ordinal));
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.ReplicaMaterializing &&
                resourceEvent.Message.Contains("replica 2/3", StringComparison.Ordinal));
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deploymentRecord.Status);
        Assert.Equal(result.Revision, deploymentRecord.Revision);
        Assert.Equal(result.Revision.ReplicaGroup, deploymentRecord.ReplicaGroup);
        Assert.Equal("tests", deploymentRecord.TriggeredBy);
        Assert.Equal("Container app deployment.", deploymentRecord.Cause);
        Assert.Equal(result.ProcedureResult.Message, deploymentRecord.Message);
        Assert.NotNull(deploymentRecord.CompletedAt);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RejectsProviderIdAsOrchestratorId()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deployments = CreateDeployments(resource, provider);
        var deployment = CreateDeployment(resource.Id, provider.Id, replicas: 1);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            deployments.ApplyDeploymentAsync(resource, deployment));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains(
            $"Orchestrator '{provider.Id}' is not registered for deployment '{deployment.Id}'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(provider.PreparedServices);
        Assert.Empty(provider.ExecutedInstances);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_UsesDefaultDeploymentServiceForOrchestratorWithoutNativeDeployments()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(
            resource,
            provider,
            deploymentStore: deploymentStore,
            orchestrators: [new DefaultResourceOrchestrator(), new PassiveResourceOrchestrator("passthrough")]);
        var deployment = CreateDeployment(resource.Id, "passthrough", replicas: 2);

        var result = await deployments.ApplyDeploymentAsync(resource, deployment);

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.Equal("passthrough", result.Deployment.OrchestratorId);
        Assert.Equal(2, result.Revision.ReplicaGroup?.RequestedReplicas);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal("passthrough", deploymentRecord.OrchestratorId);
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deploymentRecord.Status);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RecordsFailedDeployment()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource, failOnStart: true);
        var resourceEvents = new InMemoryResourceEventStore();
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, resourceEvents, deploymentStore);
        var deployment = CreateDeployment(resource.Id, "default", replicas: 1);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            deployments.ApplyDeploymentAsync(
                resource,
                deployment,
                triggeredBy: "tests",
                cause: "Container app deployment."));

        Assert.Equal("Replica execution failed.", exception.Message);
        var deploymentRecord = Assert.Single(deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: deployment.Id)));
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Failed, deploymentRecord.Status);
        Assert.Null(deploymentRecord.Revision);
        Assert.Null(deploymentRecord.ReplicaGroup);
        Assert.Equal("tests", deploymentRecord.TriggeredBy);
        Assert.Equal("Container app deployment.", deploymentRecord.Cause);
        Assert.Equal("Replica execution failed.", deploymentRecord.Error);
        Assert.NotNull(deploymentRecord.CompletedAt);
        Assert.Equal(
            [ResourceActionKind.Stop],
            provider.ExecutedActions.Select(action => action.Kind).ToArray());
        Assert.Equal(
            ["cloudshell-application-api-rev-2"],
            provider.ExecutedInstances.Select(instance => instance.Instance.Name).ToArray());
        var events = resourceEvents
            .GetEvents(new ResourceEventQuery(ResourceId: resource.Id))
            .Reverse()
            .ToArray();
        Assert.Equal(
            [
                ResourceEventTypes.Events.Deployment.Applying,
                ResourceEventTypes.Events.Deployment.ServiceReconciling,
                ResourceEventTypes.Events.Deployment.ServiceReconciled,
                ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                ResourceEventTypes.Events.Deployment.RollingBack,
                ResourceEventTypes.Events.Deployment.RolledBack,
                ResourceEventTypes.Events.Deployment.Failed
            ],
            events.Select(resourceEvent => resourceEvent.EventType).ToArray());
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.RollingBack &&
                resourceEvent.Severity == ResourceSignalSeverity.Warning);
        Assert.Contains(
            events,
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.Failed &&
                resourceEvent.Severity == ResourceSignalSeverity.Error);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_IncrementsOrchestratorRevisionNumberForSameDeployment()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments(resource, provider, deploymentStore: deploymentStore);
        var firstDeployment = CreateDeployment(resource.Id, "default", replicas: 1);
        var secondDeployment = firstDeployment with { RevisionId = "rev-3" };

        var firstResult = await deployments.ApplyDeploymentAsync(resource, firstDeployment);
        var secondResult = await deployments.ApplyDeploymentAsync(resource, secondDeployment);

        Assert.Equal(1, firstResult.Revision.RevisionNumber);
        Assert.Equal(2, secondResult.Revision.RevisionNumber);
        var records = deploymentStore.List(new ResourceOrchestratorDeploymentQuery(
            SourceResourceId: resource.Id,
            DeploymentId: firstDeployment.Id));
        Assert.Equal(2, records.Count);
        Assert.Contains(records, record =>
            record.RevisionId == "rev-2" &&
            record.Revision?.RevisionNumber == 1);
        Assert.Contains(records, record =>
            record.RevisionId == "rev-3" &&
            record.Revision?.RevisionNumber == 2);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_AllowsDifferentResourcesToApplyConcurrently()
    {
        var api = CreateResource("application:api", "API");
        var worker = CreateResource("application:worker", "Worker");
        var gate = new ConcurrentDeploymentGate(expectedCount: 2);
        var provider = new RecordingServiceProcedureProvider([api, worker], gate);
        var deploymentStore = new InMemoryResourceOrchestratorDeploymentStore();
        var deployments = CreateDeployments([api, worker], provider, deploymentStore: deploymentStore);

        var apiApply = deployments.ApplyDeploymentAsync(
            api,
            CreateDeployment(api.Id, "default", replicas: 1));
        var workerApply = deployments.ApplyDeploymentAsync(
            worker,
            CreateDeployment(worker.Id, "default", replicas: 1));

        var results = await Task.WhenAll(apiApply, workerApply)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(
            results,
            result => Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status));
        Assert.Equal(
            [api.Id, worker.Id],
            provider.PreparedServices
                .Select(service => service.ResourceId)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.Equal(
            [api.Id, worker.Id],
            deploymentStore.List()
                .Select(record => record.SourceResourceId)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    [Fact]
    public async Task TearDownServiceAsync_TearsDownProvidedServiceSpec()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment(resource.Id, "default", replicas: 2).Spec.Service with
        {
            RuntimeRevisionId = "rev-2"
        };

        var result = await orchestration.TearDownServiceAsync(
            resource,
            service,
            triggeredBy: "tests",
            cause: "Container app service cleanup.");

        Assert.Equal($"Tore down service '{service.Name}' for {resource.Name}.", result.Message);
        var preparedContext = Assert.Single(provider.PreparedContexts);
        Assert.Equal(ResourceActionKind.Stop, Assert.Single(provider.PreparedActions).Kind);
        Assert.Equal(service.Name, preparedContext.Service.Name);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", preparedContext.ReplicaGroup?.Id);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedActions,
            action => Assert.Equal(ResourceActionKind.Stop, action.Kind));
    }

    [Fact]
    public async Task TearDownServiceAsync_RejectsServiceForDifferentResource()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment("application:worker", "default", replicas: 1).Spec.Service;

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.TearDownServiceAsync(resource, service));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains(
            $"Service '{service.Name}' belongs to resource 'application:worker', not '{resource.Id}'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(provider.PreparedContexts);
        Assert.Empty(provider.ExecutedInstances);
    }

    [Fact]
    public async Task TearDownReplicaGroupAsync_TearsDownProvidedReplicaGroupWithoutServicePrepare()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var service = CreateDeployment(resource.Id, "default", replicas: 3).Spec.Service with
        {
            RuntimeRevisionId = "rev-2"
        };
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        var result = await orchestration.TearDownReplicaGroupAsync(
            resource,
            service,
            replicaGroup,
            triggeredBy: "tests",
            cause: "Container app replica group cleanup.");

        Assert.Equal($"Tore down replica group '{replicaGroup.Id}' for service '{service.Name}'.", result.Message);
        Assert.Empty(provider.PreparedContexts);
        Assert.Equal(
            [
                "cloudshell-application-api-rev-2-replica-1",
                "cloudshell-application-api-rev-2-replica-2",
                "cloudshell-application-api-rev-2-replica-3"
            ],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.Name)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray());
        Assert.All(
            provider.ExecutedActions,
            action => Assert.Equal(ResourceActionKind.Stop, action.Kind));
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(replicaGroup, instance.ReplicaGroup));
    }

    private static ResourceDeploymentService CreateDeployments(
        Resource resource,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null,
        IReadOnlyList<IResourceOrchestrator>? orchestrators = null) =>
        CreateDeployments([resource], provider, resourceEvents, deploymentStore, orchestrators);

    private static ResourceDeploymentService CreateDeployments(
        IReadOnlyList<Resource> resources,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null,
        IResourceOrchestratorDeploymentStore? deploymentStore = null,
        IReadOnlyList<IResourceOrchestrator>? orchestrators = null)
    {
        var registrations = new TestResourceRegistrationStore(
            resources.Select(resource =>
                new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn)));
        return new ResourceDeploymentService(
            orchestrators ?? [new DefaultResourceOrchestrator()],
            [new DefaultResourceDeploymentService(deploymentStore)],
            new TestResourceManagerStore(resources, provider),
            registrations,
            CreateSelectionStore(),
            resourceEvents: resourceEvents,
            deploymentStore: deploymentStore);
    }

    private static ResourceOrchestrationService CreateOrchestration(
        Resource resource,
        RecordingServiceProcedureProvider provider) =>
        CreateOrchestration([resource], provider);

    private static ResourceOrchestrationService CreateOrchestration(
        IReadOnlyList<Resource> resources,
        RecordingServiceProcedureProvider provider)
    {
        var registrations = new TestResourceRegistrationStore(
            resources.Select(resource =>
                new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn)));
        return new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            new TestResourceManagerStore(resources, provider),
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore());
    }

    private static Resource CreateResource(
        string id = "application:api",
        string name = "API") =>
        new(
            id,
            name,
            "container-app",
            "Container App",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "container-app",
            Actions: [ResourceAction.Start, ResourceAction.Stop]);

    private static ResourceOrchestratorDeployment CreateDeployment(
        string resourceId,
        string orchestratorId,
        int replicas)
    {
        var serviceName = $"cloudshell-{resourceId.Replace(':', '-').Replace('_', '-').ToLowerInvariant()}";
        var service = new ResourceOrchestratorService(
            resourceId,
            serviceName,
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: "ghcr.io/example/api:2",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1),
            Ports: [new ServicePort("http", 8080, Protocol: "http")],
            Networks: ["cloudshell"]);
        return new ResourceOrchestratorDeployment(
            $"{serviceName}-deployment",
            orchestratorId,
            resourceId,
            service.Name,
            "rev-2",
            new ResourceOrchestratorDeploymentSpec(service, "rev-2"),
            ResourceOrchestratorDeploymentStatus.Pending);
    }

    private static ResourceOrchestratorSelectionStore CreateSelectionStore() =>
        new(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));

    private sealed class RecordingServiceProcedureProvider(
        IReadOnlyList<Resource> resources,
        ConcurrentDeploymentGate? gate = null,
        bool failOnStart = false) :
        IResourceProvider,
        IResourceOrchestratorServiceProcedureProvider
    {
        public RecordingServiceProcedureProvider(Resource resource)
            : this([resource])
        {
        }

        public RecordingServiceProcedureProvider(Resource resource, bool failOnStart)
            : this([resource], failOnStart: failOnStart)
        {
        }

        public string Id => "applications.container-app";

        public string DisplayName => "Container App";

        public ConcurrentBag<ResourceOrchestratorService> PreparedServices { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceProcedureContext> PreparedContexts { get; } = [];

        public ConcurrentBag<ResourceAction> PreparedActions { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceInstanceContext> ExecutedInstances { get; } = [];

        public ConcurrentBag<ResourceAction> ExecutedActions { get; } = [];

        public IReadOnlyList<Resource> GetResources() => resources;

        public bool CanExecuteOrchestratorService(
            Resource resource,
            ResourceAction action) =>
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop;

        public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The deployment spec should provide the service.");

        public async Task PrepareOrchestratorServiceAsync(
            ResourceOrchestratorServiceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            PreparedActions.Add(action);
            PreparedContexts.Add(context);
            PreparedServices.Add(context.Service);
            if (gate is not null)
            {
                await gate.SignalAndWaitAsync(cancellationToken);
            }
        }

        public Task ExecuteOrchestratorServiceInstanceAsync(
            ResourceOrchestratorServiceInstanceContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            if (failOnStart &&
                action.Kind == ResourceActionKind.Start)
            {
                throw new InvalidOperationException("Replica execution failed.");
            }

            ExecutedActions.Add(action);
            ExecutedInstances.Add(context);
            return Task.CompletedTask;
        }

    }

    private sealed class PassiveResourceOrchestrator(string id) : IResourceOrchestrator
    {
        public string Id => id;

        public string DisplayName => id;

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
    }

    private sealed class ConcurrentDeploymentGate(int expectedCount)
    {
        private readonly TaskCompletionSource allArrived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrived;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref arrived) == expectedCount)
            {
                allArrived.TrySetResult();
            }

            await allArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private sealed class TestResourceManagerStore(
        IReadOnlyList<Resource> resources,
        IResourceProvider provider) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            resources.Any(resource =>
                string.Equals(resourceId, resource.Id, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestResourceRegistrationStore(IEnumerable<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        private readonly IReadOnlyList<ResourceRegistration> registrations = registrations.ToArray();

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(resourceId, registration.ResourceId, StringComparison.OrdinalIgnoreCase));

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
