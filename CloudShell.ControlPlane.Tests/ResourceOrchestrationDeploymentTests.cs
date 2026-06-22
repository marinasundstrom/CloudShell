using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
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
        var orchestration = CreateOrchestration(resource, provider, resourceEvents);
        var deployment = CreateDeployment(resource.Id, "default", replicas: 3);

        var result = await orchestration.ApplyDeploymentAsync(
            resource,
            deployment,
            triggeredBy: "tests",
            cause: "Container app deployment.");

        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, result.Deployment.Status);
        Assert.Equal(deployment.Spec.Service.Name, Assert.Single(provider.PreparedServices).Name);
        Assert.Equal(
            [1, 2, 3],
            provider.ExecutedInstances
                .Select(instance => instance.Instance.ReplicaOrdinal)
                .Order()
                .ToArray());
        Assert.All(
            provider.ExecutedInstances,
            instance => Assert.Equal(deployment.Spec.Service.Name, instance.Service.Name));
        Assert.Contains(
            resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: resource.Id)),
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.Applying &&
                resourceEvent.Message.Contains("Cause: Container app deployment.", StringComparison.Ordinal));
        Assert.Contains(
            resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: resource.Id)),
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Events.Deployment.Applied &&
                resourceEvent.TriggeredBy == "tests");
    }

    [Fact]
    public async Task ApplyDeploymentAsync_RejectsProviderIdAsOrchestratorId()
    {
        var resource = CreateResource();
        var provider = new RecordingServiceProcedureProvider(resource);
        var orchestration = CreateOrchestration(resource, provider);
        var deployment = CreateDeployment(resource.Id, provider.Id, replicas: 1);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.ApplyDeploymentAsync(resource, deployment));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains(
            $"Orchestrator '{provider.Id}' is not registered for deployment '{deployment.Id}'",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Empty(provider.PreparedServices);
        Assert.Empty(provider.ExecutedInstances);
    }

    [Fact]
    public async Task ApplyDeploymentAsync_AllowsDifferentResourcesToApplyConcurrently()
    {
        var api = CreateResource("application:api", "API");
        var worker = CreateResource("application:worker", "Worker");
        var gate = new ConcurrentDeploymentGate(expectedCount: 2);
        var provider = new RecordingServiceProcedureProvider([api, worker], gate);
        var orchestration = CreateOrchestration([api, worker], provider);

        var apiApply = orchestration.ApplyDeploymentAsync(
            api,
            CreateDeployment(api.Id, "default", replicas: 1));
        var workerApply = orchestration.ApplyDeploymentAsync(
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
    }

    private static ResourceOrchestrationService CreateOrchestration(
        Resource resource,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null) =>
        CreateOrchestration([resource], provider, resourceEvents);

    private static ResourceOrchestrationService CreateOrchestration(
        IReadOnlyList<Resource> resources,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null)
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
            CreateSelectionStore(),
            resourceEvents: resourceEvents);
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
        ConcurrentDeploymentGate? gate = null) :
        IResourceProvider,
        IResourceOrchestratorServiceProcedureProvider
    {
        public RecordingServiceProcedureProvider(Resource resource)
            : this([resource])
        {
        }

        public string Id => "applications.container-app";

        public string DisplayName => "Container App";

        public ConcurrentBag<ResourceOrchestratorService> PreparedServices { get; } = [];

        public ConcurrentBag<ResourceOrchestratorServiceInstanceContext> ExecutedInstances { get; } = [];

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
            ExecutedInstances.Add(context);
            return Task.CompletedTask;
        }
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
