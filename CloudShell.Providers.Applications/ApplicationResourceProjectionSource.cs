using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceProjectionSource(
    IApplicationResourceDefinitionSource definitions,
    ApplicationRuntimeStateStore runtimeStates,
    IApplicationResourceRunningStateOperations runningState,
    ApplicationWorkloadConfigurationProvider workloadConfigurations,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationProviderOptions options) : IApplicationResourceProjectionSource
{
    private static readonly TimeSpan StartingStateTimeout = TimeSpan.FromMinutes(5);
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    private static readonly ApplicationContainerRevisionService ContainerRevisionService = new();
    private static readonly ContainerApplicationRuntimeRevisionPolicy ContainerRuntimeRevisionPolicy = new();
    private readonly ApplicationResourcePortResolver ports = new(options);

    public IReadOnlyList<Resource> GetResources() => definitions
        .GetApplications()
        .Where(application => !IsHidden(application))
        .SelectMany(application => CreateResourceProjection(
            application,
            ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(application)))
        .ToArray();

    public IReadOnlyList<Resource> GetResources(ApplicationResourceProjection projection) => definitions
        .GetApplications()
        .Where(application => !IsHidden(application))
        .Where(projection.CanProject)
        .SelectMany(application => CreateResourceProjection(application, projection))
        .ToArray();

    private IEnumerable<Resource> CreateResourceProjection(
        ApplicationResourceDefinition application,
        ApplicationResourceProjection projection)
    {
        yield return CreateApplicationResourceProjector()
            .CreateResource(application, projection, "Applications");

        if (string.Equals(
                application.ResourceType,
                ApplicationResourceTypes.SqlServer,
                StringComparison.OrdinalIgnoreCase))
        {
            foreach (var database in SqlServerDatabaseResourceProjector.CreateResources(application))
            {
                yield return database;
            }
        }

        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            yield break;
        }

        foreach (var runtimeResource in CreateRuntimeContainerResources(application))
        {
            yield return runtimeResource;
        }
    }

    private ApplicationResourceProjector CreateApplicationResourceProjector() =>
        new(
            runtimeStates,
            GetState,
            CreateResourceObservability,
            CreateDefaultContainerOrchestratorDeployment,
            ResolveLocalPort);

    private ResourceState GetState(string applicationId)
        => new ApplicationRuntimeStateTracker(
            runtimeStates,
            runningState.IsRunning,
            transientStateTimeout: StartingStateTimeout)
            .GetState(applicationId);

    private ResourceObservability CreateResourceObservability(ApplicationResourceDefinition definition)
    {
        var observability = workloadConfigurations.GetEffectiveObservability(definition);
        if (!ApplicationResourceTypes.IsContainerApp(definition.ResourceType) ||
            !IsReplicaModeEnabled(definition) ||
            !observability.HasAnySignal)
        {
            return observability;
        }

        var deployment = CreateDefaultContainerOrchestratorDeployment(
            definition,
            GetState(definition.Id),
            runtimeRevisionScoped: true);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
        var scopes = replicaGroup.Instances
            .Select(instance =>
            {
                var scopeAttributes = CreateRuntimeContainerTelemetryAttributes(
                    definition,
                    instance,
                    deployment.RevisionId);
                return new TelemetryScopeDescriptor(
                    ApplicationResourceNames.CreateRuntimeContainerResourceId(definition.Id, instance.ReplicaOrdinal),
                    $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}",
                    "runtime",
                    $"Runtime container replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} of {instance.ReplicaCount.ToString(CultureInfo.InvariantCulture)}.",
                    deployment.RevisionId,
                    scopeAttributes);
            })
            .ToArray();

        return observability with
        {
            Scopes = observability.TelemetryScopes
                .Concat(scopes)
                .GroupBy(scope => scope.ScopeResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray()
        };
    }

    private IReadOnlyList<Resource> CreateRuntimeContainerResources(ApplicationResourceDefinition application)
    {
        if (!IsReplicaModeEnabled(application))
        {
            return [];
        }

        var parentState = GetState(application.Id);
        var deployment = CreateDefaultContainerOrchestratorDeployment(
            application,
            parentState,
            runtimeRevisionScoped: true);
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(deployment.Spec.Service);
        return replicaGroup.Instances
            .Select(instance => CreateRuntimeContainerResource(application, deployment, replicaGroup, instance, parentState))
            .ToArray();
    }

    private Resource CreateRuntimeContainerResource(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeployment deployment,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorServiceInstance instance,
        ResourceState state)
    {
        var service = deployment.Spec.Service;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentId] = deployment.Id,
            [ResourceAttributeNames.DeploymentServiceId] = deployment.ServiceId,
            [ResourceAttributeNames.DeploymentRevision] = deployment.RevisionId,
            [ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroup.Id,
            [ResourceAttributeNames.RuntimeKind] = "containerReplica",
            [ResourceAttributeNames.RuntimeContainerName] = instance.Name,
            [ResourceAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.RuntimeRevision] = deployment.RevisionId,
            [ResourceAttributeNames.RuntimeMaterialization] = "orchestratorMaterialized"
        };

        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerImage, service.Workload.Image);
        AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerHostId, service.Workload.ContainerHostId);
        AddIfNotEmpty(
            attributes,
            ResourceAttributeNames.DeploymentEnvironmentRevisionId,
            application.DeploymentEnvironmentRevisionId);

        return new Resource(
            ApplicationResourceNames.CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal),
            instance.Name,
            "Container replica",
            "Applications",
            "local",
            state,
            CreateRuntimeContainerEndpoints(application, service),
            deployment.RevisionId,
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: application.Id,
            TypeId: "runtime.container",
            HealthChecks: CreateRuntimeContainerHealthChecks(application),
            ResourceClass: ResourceClass.Container,
            Attributes: attributes,
            Capabilities:
            [
                new(ResourceCapabilityIds.Monitoring),
                new(ResourceCapabilityIds.LogSources)
            ],
            LogSources: ApplicationLogSources.CreateRuntimeContainerLogSources(
                application.Id,
                instance,
                ApplicationLogSources.GetPrimaryApplicationLogSource(application)),
            EndpointNetworkMappings: CreateRuntimeContainerEndpointNetworkMappings(application, service, instance, state),
            Source: ResourceSource.Orchestrator,
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: application.Id,
            CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner);
    }

    private ResourceOrchestratorDeployment CreateDefaultContainerOrchestratorDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        bool runtimeRevisionScoped = false)
    {
        var revision = ContainerRevisionService.GetEffectiveRevision(application);
        return ContainerOrchestratorDeploymentFactory.CreateDeployment(
            application,
            state,
            workloadConfigurations.Create(application),
            runtimeRevisionScoped &&
                ContainerRuntimeRevisionPolicy.ShouldUseRevisionScopedRuntimeInstances(
                    application,
                    revision,
                    containerDeployments.ListRevisions(application.Id)));
    }

    private static IReadOnlyList<ResourceHealthCheck> CreateRuntimeContainerHealthChecks(
        ApplicationResourceDefinition application)
    {
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType) ||
            !IsReplicaModeEnabled(application) ||
            application.HealthChecks.Count == 0)
        {
            return [];
        }

        return application.HealthChecks;
    }

    private static IReadOnlyList<ResourceEndpoint> CreateRuntimeContainerEndpoints(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service)
    {
        if (!ShouldProjectRuntimeContainerProbeTargets(application))
        {
            return [];
        }

        return GetRuntimeContainerProbePorts(application, service)
            .Select(port => ResourceEndpoint.Contract(
                port.Name,
                NormalizeProtocol(port.Protocol),
                ResourceExposureScope.Local,
                Math.Max(1, port.TargetPort)))
            .ToArray();
    }

    private IReadOnlyList<ResourceEndpointNetworkMapping> CreateRuntimeContainerEndpointNetworkMappings(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceState state)
    {
        if (!ShouldProjectActiveRuntimeContainerProbeTargets(application, state))
        {
            return [];
        }

        var resourceId = ApplicationResourceNames.CreateRuntimeContainerResourceId(application.Id, instance.ReplicaOrdinal);
        return GetRuntimeContainerProbePorts(application, service)
            .Select(port => ResourceEndpointNetworkMapping.ForEndpoint(
                resourceId,
                port.Name,
                CreateRuntimeContainerProbeEndpointAddress(application.Id, port, instance),
                ResourceExposureScope.Local,
                networkResourceId: NormalizeNullable(port.NetworkResourceId),
                sourceEndpointName: port.Name))
            .ToArray();
    }

    private string CreateRuntimeContainerProbeEndpointAddress(
        string resourceId,
        ServicePort port,
        ResourceOrchestratorServiceInstance instance)
    {
        var protocol = NormalizeProtocol(port.Protocol);
        var host = ApplicationResourceProjectionSupport.FirstNonEmpty(port.IPAddress, port.Host, "localhost")!;
        return $"{protocol}://{host}:{ports.ResolveReplicaProbeLocalPort(resourceId, port, instance).ToString(CultureInfo.InvariantCulture)}";
    }

    private static IReadOnlyList<ServicePort> GetRuntimeContainerProbePorts(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service)
    {
        if (!ShouldProjectRuntimeContainerProbeTargets(application))
        {
            return [];
        }

        var namedEndpoints = application.HealthChecks
            .Select(check => NormalizeNullable(check.HttpSource?.EndpointName))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasUnnamedHttpProbe = application.HealthChecks.Any(check =>
            check.HttpSource is not null &&
            string.IsNullOrWhiteSpace(check.HttpSource.EndpointName));

        return service.ServicePorts
            .Where(IsHttpProbePort)
            .Where(port =>
                namedEndpoints.Contains(port.Name) ||
                hasUnnamedHttpProbe)
            .DistinctBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldProjectRuntimeContainerProbeTargets(
        ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        IsReplicaModeEnabled(application) &&
        application.HealthChecks.Any(check => check.HttpSource is not null);

    private static bool ShouldProjectActiveRuntimeContainerProbeTargets(
        ApplicationResourceDefinition application,
        ResourceState state) =>
        ShouldProjectRuntimeContainerProbeTargets(application) &&
        state is ResourceState.Running or ResourceState.Starting or ResourceState.Degraded;

    private int ResolveLocalPort(string resourceId, ServicePort port)
        => ports.ResolveLocalPort(resourceId, port);

    private static IReadOnlyDictionary<string, string> CreateRuntimeContainerTelemetryAttributes(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorServiceInstance instance,
        string revision)
    {
        var resourceId = ApplicationResourceNames.CreateRuntimeContainerResourceId(definition.Id, instance.ReplicaOrdinal);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TelemetryAttributeNames.ScopeResourceId] = resourceId,
            [TelemetryAttributeNames.ScopeName] = $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}",
            [TelemetryAttributeNames.ScopeKind] = "runtime",
            [TelemetryAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeContainerName] = instance.Name,
            [TelemetryAttributeNames.DeploymentRevision] = revision
        };
    }

    private static bool IsHidden(ApplicationResourceDefinition application) =>
        application.EnvironmentVariables.Any(variable =>
            string.Equals(variable.Name, ApplicationResourceService.HiddenResourceEnvironmentVariable, StringComparison.OrdinalIgnoreCase) &&
            bool.TryParse(variable.Value, out var hidden) &&
            hidden);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static bool IsHttpProbePort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "https";

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddIfNotEmpty(
        IDictionary<string, string> values,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[name] = value.Trim();
        }
    }
}
