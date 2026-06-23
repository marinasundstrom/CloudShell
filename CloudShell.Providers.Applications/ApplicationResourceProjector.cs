using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationResourceProjector(
    IApplicationRuntimeStateStore runtimeStates,
    Func<string, ResourceState> getState,
    Func<ApplicationResourceDefinition, ResourceObservability> createObservability,
    Func<ApplicationResourceDefinition, ResourceState, bool, ResourceOrchestratorDeployment> createContainerDeployment,
    Func<string, ServicePort, int> resolveLocalPort)
{
    public Resource CreateResource(
        ApplicationResourceDefinition application,
        ApplicationResourceProjection projection,
        string providerDisplayName)
    {
        var state = getState(application.Id);
        var endpoints = CreateEndpoints(application);
        return new Resource(
            application.Id,
            GetResourceName(application.Id),
            projection.GetResourceKind(application),
            providerDisplayName,
            "local",
            state,
            endpoints,
            projection.GetResourceVersion(application),
            DateTimeOffset.UtcNow,
            application.DependsOn,
            TypeId: application.ResourceType,
            Actions: CreateActions(application, state),
            HealthChecks: CreateHealthChecks(application),
            RecoveryPolicies: application.RecoveryPolicies,
            Observability: createObservability(application),
            ResourceClass: projection.GetResourceClass(application),
            Attributes: CreateProjectionFactory().CreateAttributes(application, state, projection),
            Capabilities: ApplicationResourceProjectionFactory.CreateCapabilities(application, endpoints),
            EndpointNetworkMappings: CreateEndpointNetworkMappings(application),
            DisplayName: application.Name,
            LogSources: ApplicationLogSources.GetApplicationLogSources(application));
    }

    public IReadOnlyList<ResourceEndpointNetworkMapping> CreateEndpointNetworkMappings(
        ApplicationResourceDefinition application)
    {
        if (application.EndpointPorts.Count > 0)
        {
            return application.EndpointPorts
                .Select(port => ResourceEndpointNetworkMapping.ForEndpoint(
                    application.Id,
                    port.Name,
                    CreateServiceEndpointAddress(application.Id, port),
                    port.Exposure,
                    networkResourceId: NormalizeNullable(port.NetworkResourceId),
                    sourceEndpointName: port.Name))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(application.Endpoint))
        {
            return [];
        }

        return
        [
            ResourceEndpointNetworkMapping.ForEndpoint(
                application.Id,
                "application",
                application.Endpoint,
                ResourceExposureScope.Public,
                sourceEndpointName: "application")
        ];
    }

    public string CreateServiceEndpointAddress(string resourceId, ServicePort port)
    {
        var protocol = NormalizeProtocol(port.Protocol);
        var host = FirstNonEmpty(port.IPAddress, port.Host, "localhost")!;
        return $"{protocol}://{host}:{resolveLocalPort(resourceId, port).ToString(CultureInfo.InvariantCulture)}";
    }

    public IReadOnlyList<ResourceEndpoint> CreateEndpoints(ApplicationResourceDefinition application)
    {
        if (application.EndpointPorts.Count > 0)
        {
            return application.EndpointPorts
                .Select(port => ResourceEndpoint.Contract(
                    port.Name,
                    NormalizeProtocol(port.Protocol),
                    port.Exposure,
                    Math.Max(1, port.TargetPort)))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(application.Endpoint))
        {
            if (IsContainerBacked(application))
            {
                return [];
            }

            return [ResourceEndpoint.Logical("process", $"process://{application.Id}", "process")];
        }

        var endpoint = application.Endpoint;
        var protocol = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Scheme
            : "tcp";

        return [ResourceEndpoint.Contract(
            "application",
            protocol,
            ResourceExposureScope.Public,
            ResourceEndpoint.TryGetPort(endpoint, out var port) ? port : null)];
    }

    private ApplicationResourceProjectionFactory CreateProjectionFactory() =>
        new(
            runtimeStates,
            createContainerDeployment);

    private static IReadOnlyList<ResourceHealthCheck> CreateHealthChecks(
        ApplicationResourceDefinition application)
    {
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType) ||
            !IsReplicaModeEnabled(application))
        {
            return application.HealthChecks;
        }

        return [];
    }

    private static IReadOnlyList<ResourceAction> CreateActions(
        ApplicationResourceDefinition application,
        ResourceState state)
    {
        var lifecycleActions = state is ResourceState.Running or ResourceState.Starting or ResourceState.Stopping
            ? new[] { ResourceAction.Stop, ResourceAction.Restart }
            : [ResourceAction.Start];

        if (!string.Equals(application.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return lifecycleActions;
        }

        return lifecycleActions
            .Append(new ResourceAction(
                ApplicationResourceService.ReconcileSqlServerAccessActionId,
                "Reconcile database access",
                Description: "Create or update SQL Server database users and roles for CloudShell database grants.",
                RequiredPermission: DatabaseResourceOperationPermissions.ReconcileAccess))
            .ToArray();
    }

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceProjectionSupport.IsContainerBacked(application);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static string GetResourceName(string resourceId) =>
        resourceId.StartsWith("application:", StringComparison.OrdinalIgnoreCase)
            ? resourceId["application:".Length..]
            : resourceId;

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
