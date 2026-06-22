using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

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
            provider.ExecutedInstances.Select(instance => instance.Instance.ReplicaOrdinal));
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

    private static ResourceOrchestrationService CreateOrchestration(
        Resource resource,
        RecordingServiceProcedureProvider provider,
        IResourceEventSink? resourceEvents = null)
    {
        var registrations = new TestResourceRegistrationStore(
            new ResourceRegistration(resource.Id, provider.Id, null, DateTimeOffset.UtcNow, resource.DependsOn));
        return new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            new TestResourceManagerStore(resource, provider),
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            resourceEvents: resourceEvents);
    }

    private static Resource CreateResource() =>
        new(
            "application:api",
            "API",
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
        var service = new ResourceOrchestratorService(
            resourceId,
            "cloudshell-application-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: "ghcr.io/example/api:2",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1),
            Networks: ["cloudshell"]);
        return new ResourceOrchestratorDeployment(
            "deployment-application-api",
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

    private sealed class RecordingServiceProcedureProvider(Resource resource) :
        IResourceProvider,
        IResourceOrchestratorServiceProcedureProvider
    {
        public string Id => "applications.container-app";

        public string DisplayName => "Container App";

        public List<ResourceOrchestratorService> PreparedServices { get; } = [];

        public List<ResourceOrchestratorServiceInstanceContext> ExecutedInstances { get; } = [];

        public IReadOnlyList<Resource> GetResources() => [resource];

        public bool CanExecuteOrchestratorService(
            Resource resource,
            ResourceAction action) =>
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop;

        public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("The deployment spec should provide the service.");

        public Task PrepareOrchestratorServiceAsync(
            ResourceOrchestratorServiceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            PreparedServices.Add(context.Service);
            return Task.CompletedTask;
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

    private sealed class TestResourceManagerStore(
        Resource resource,
        IResourceProvider provider) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => [resource];

        public IReadOnlyList<Resource> GetResources() => [resource];

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            string.Equals(id, resource.Id, StringComparison.OrdinalIgnoreCase)
                ? resource
                : null;

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            string.Equals(resourceId, resource.Id, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestResourceRegistrationStore(ResourceRegistration registration) :
        IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [registration];

        public ResourceRegistration? GetRegistration(string resourceId) =>
            string.Equals(resourceId, registration.ResourceId, StringComparison.OrdinalIgnoreCase)
                ? registration
                : null;

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
