using CloudShell.Abstractions.Authorization;
using System.Text.Json;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace CloudShell.ControlPlane.Client.Tests;

public sealed class RemoteControlPlaneContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RemoteControlPlane_ListsSeededResourcesAndGroups()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        var resources = await controlPlane.ListResourcesAsync();
        var groups = await controlPlane.ListResourceGroupsAsync();

        var network = Assert.Single(resources);
        Assert.Equal("network:contract", network.Id);
        Assert.Equal("contract", network.Name);
        Assert.Equal("Contract Network", network.DisplayName);
        Assert.Equal(PlatformResourceProvider.NetworkResourceType, network.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, network.ResourceClass);
        Assert.Equal("Default", network.ResourceAttributes[ResourceAttributeNames.NetworkKind]);
        Assert.True(network.HasCapability(ResourceCapabilityIds.NetworkingProvider));
        Assert.True(network.HasCapability(ResourceCapabilityIds.NetworkingEndpointProvider));
        Assert.True(network.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper));

        var remoteGroup = Assert.Single(groups);
        Assert.Equal(group.Id, remoteGroup.Id);
        Assert.Equal("Contract Group", remoteGroup.Name);
        Assert.Contains("network:contract", remoteGroup.ResourceIds);

        var resourceGroup = await controlPlane.GetResourceGroupForResourceAsync(network.Id);
        Assert.NotNull(resourceGroup);
        Assert.Equal(remoteGroup.Id, resourceGroup.Id);
    }

    [Fact]
    public async Task RemoteControlPlane_PreservesRuntimeManagedResourceMetadata()
    {
        await using var app = await CreateAppAsync(includeRuntimeResource: true);
        var controlPlane = CreateClient(app);

        var resource = await controlPlane.GetResourceAsync(ContractRuntimeResourceProvider.ResourceId);

        Assert.NotNull(resource);
        Assert.Equal(ResourceSource.RuntimeController, resource.Source);
        Assert.Equal(ResourceManagementMode.RuntimeManaged, resource.ManagementMode);
        Assert.Equal(ResourceVisibility.Hidden, resource.Visibility);
        Assert.Equal("network:contract", resource.OwnerResourceId);
        Assert.Equal(ResourceCleanupBehavior.DeleteWithOwner, resource.CleanupBehavior);
        Assert.False(resource.IsNormalResource);
        Assert.True(resource.IsRuntimeManaged);
    }

    [Fact]
    public async Task RemoteControlPlane_CreatesAndQueriesPlatformResources()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                PlatformResourceProvider.ProviderId,
                PlatformResourceProvider.ServiceResourceType,
                "service:contract",
                "Contract Service",
                JsonSerializer.SerializeToElement(
                    new ServiceResourceDefinition(
                        "service:contract",
                        "Contract Service",
                        [new ServiceTarget("network:contract")],
                        [new ServicePort("http", 8080, 5080, "http")],
                        ["network:contract"]),
                    SerializerOptions),
                group.Id));

        var services = await controlPlane.ListResourcesAsync(
            new ResourceQuery(
                ResourceGroupId: group.Id,
                ResourceType: PlatformResourceProvider.ServiceResourceType,
                ResourceClass: ResourceClass.Service));
        var service = Assert.Single(services);

        var networkResources = await controlPlane.ListResourcesAsync(
            new ResourceQuery(ResourceClass: ResourceClass.Network));
        Assert.Single(networkResources);

        Assert.Equal("service:contract", service.Id);
        Assert.Equal("contract", service.Name);
        Assert.Equal("Contract Service", service.DisplayName);
        Assert.Equal(["network:contract"], service.DependsOn);
        Assert.Equal("http://localhost:5080", service.PrimaryEndpoint);
        var endpoint = Assert.Single(service.Endpoints);
        Assert.Equal(ResourceExposureScope.Local, endpoint.Exposure);
        Assert.Equal(8080, endpoint.TargetPort);
        var endpointNetworkMapping = Assert.Single(service.ResourceEndpointNetworkMappings);
        Assert.Equal("http", endpointNetworkMapping.Name);
        Assert.Equal(new ResourceEndpointReference(service.Id, endpoint.Name), endpointNetworkMapping.Target);
        Assert.Equal("http://localhost:5080", endpointNetworkMapping.Address);
        Assert.Equal(ResourceClass.Service, service.ResourceClass);
        Assert.Equal("1", service.ResourceAttributes[ResourceAttributeNames.ServiceTargetCount]);
        Assert.Equal("1", service.ResourceAttributes[ResourceAttributeNames.ServicePortCount]);
        Assert.True(service.HasCapability(ResourceCapabilityIds.EndpointSource));

        var registration = await controlPlane.GetResourceRegistrationAsync(service.Id);
        Assert.NotNull(registration);
        Assert.Equal(group.Id, registration.ResourceGroupId);
        Assert.Equal(["network:contract"], registration.DependsOn);
    }

    [Fact]
    public async Task RemoteControlPlane_CreatesDnsZoneWithNameMapping()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                PlatformResourceProvider.ProviderId,
                PlatformResourceProvider.DnsZoneResourceType,
                "dns:contract",
                "Contract DNS",
                JsonSerializer.SerializeToElement(
                    new DnsZoneResourceDefinition(
                        "dns:contract",
                        "Contract DNS",
                        "contract.local",
                        Mappings:
                        [
                            new DnsNameMappingDefinition(
                                "dns:contract:name:api-contract-local",
                                "api.contract.local",
                                "api.contract.local",
                                "network:contract",
                                Exposure: ResourceExposureScope.Private)
                        ]),
                    SerializerOptions),
                group.Id));

        var zone = await controlPlane.GetResourceAsync("dns:contract");
        var mapping = await controlPlane.GetResourceAsync("dns:contract:name:api-contract-local");

        Assert.NotNull(zone);
        Assert.Equal(PlatformResourceProvider.DnsZoneResourceType, zone.EffectiveTypeId);
        Assert.Null(zone.State);
        Assert.Equal("contract.local", zone.ResourceAttributes[ResourceAttributeNames.DnsZoneName]);
        Assert.Equal("1", zone.ResourceAttributes[ResourceAttributeNames.DnsRecordCount]);

        Assert.NotNull(mapping);
        Assert.Equal(PlatformResourceProvider.NameMappingResourceType, mapping.EffectiveTypeId);
        Assert.Null(mapping.State);
        Assert.Equal(zone.Id, mapping.ParentResourceId);
        Assert.Equal(["network:contract"], mapping.DependsOn);
        Assert.Equal("api.contract.local", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingHostName]);
        Assert.Equal("network:contract", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Equal(ResourceExposureScope.Private.ToString(), mapping.ResourceAttributes[ResourceAttributeNames.NameMappingExposure]);
        Assert.Equal(
            "LogicalOnly",
            mapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatus]);
    }

    [Fact]
    public async Task RemoteControlPlane_CreatesNameMappingInExistingDnsZone()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                PlatformResourceProvider.ProviderId,
                PlatformResourceProvider.DnsZoneResourceType,
                "dns:contract",
                "Contract DNS",
                JsonSerializer.SerializeToElement(
                    new DnsZoneResourceDefinition(
                        "dns:contract",
                        "Contract DNS",
                        "contract.local"),
                    SerializerOptions),
                group.Id));

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                PlatformResourceProvider.ProviderId,
                PlatformResourceProvider.NameMappingResourceType,
                "dns:contract:name:api-contract-local",
                "api.contract.local",
                JsonSerializer.SerializeToElement(
                    new DnsNameMappingResourceDefinition(
                        "dns:contract",
                        "dns:contract:name:api-contract-local",
                        "api.contract.local",
                        "api.contract.local",
                        "network:contract",
                        Exposure: ResourceExposureScope.Private),
                    SerializerOptions),
                group.Id));

        var zone = await controlPlane.GetResourceAsync("dns:contract");
        var mapping = await controlPlane.GetResourceAsync("dns:contract:name:api-contract-local");
        var registration = await controlPlane.GetResourceRegistrationAsync("dns:contract");

        Assert.NotNull(zone);
        Assert.Equal("1", zone.ResourceAttributes[ResourceAttributeNames.DnsRecordCount]);

        Assert.NotNull(mapping);
        Assert.Equal(PlatformResourceProvider.NameMappingResourceType, mapping.EffectiveTypeId);
        Assert.Equal(zone.Id, mapping.ParentResourceId);
        Assert.Equal("api.contract.local", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingHostName]);
        Assert.Equal("network:contract", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Equal(ResourceExposureScope.Private.ToString(), mapping.ResourceAttributes[ResourceAttributeNames.NameMappingExposure]);

        Assert.NotNull(registration);
        Assert.Equal(group.Id, registration.ResourceGroupId);
        Assert.Equal(["network:contract"], registration.DependsOn);
    }

    [Fact]
    public async Task ControlPlaneApi_FiltersResourcesByClass()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/control-plane/v1/resources?resourceClass=Network");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("network:contract", resource.GetProperty("id").GetString());
        Assert.Equal((int)ResourceClass.Network, resource.GetProperty("resourceClass").GetInt32());
    }

    [Fact]
    public async Task RemoteControlPlane_MapsCapabilitiesAndDeleteResults()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(["network:contract"]);

        var networkCapabilities = Assert.Single(capabilities);
        Assert.Equal("network:contract", networkCapabilities.Key);
        Assert.True(networkCapabilities.Value.CanManage);
        Assert.True(networkCapabilities.Value.CanDelete);

        var result = await controlPlane.DeleteResourceAsync("network:contract");
        Assert.Contains("removed", result.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Null(await controlPlane.GetResourceAsync("network:contract"));
    }

    [Fact]
    public async Task RemoteControlPlane_MapsResourcesWithoutLifecycleState()
    {
        await using var app = await CreateAppAsync(includeStatelessResource: true);
        var controlPlane = CreateClient(app);

        var resource = await controlPlane.GetResourceAsync(ContractStatelessResourceProvider.ResourceId);

        Assert.NotNull(resource);
        Assert.Null(resource.State);

        var client = app.GetTestClient();
        var response = await client.GetAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(ContractStatelessResourceProvider.ResourceId)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task RemoteControlPlane_ListsAndEvaluatesResourcePermissionGrants()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var grants = await controlPlane.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "network:contract"));
        var allowed = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            NetworkResourceOperationPermissions.ReconcileEndpointMappings);
        var denied = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            LoadBalancerResourceOperationPermissions.ApplyConfiguration);

        var grant = Assert.Single(grants);
        Assert.Equal("network:contract", grant.Identity.ResourceId);
        Assert.Equal("network-service", grant.Identity.Name);
        Assert.Equal("network:contract", grant.TargetResourceId);
        Assert.Equal(NetworkResourceOperationPermissions.ReconcileEndpointMappings, grant.Permission);
        Assert.True(allowed.IsAllowed);
        Assert.NotNull(allowed.Grant);
        Assert.False(denied.IsAllowed);
        Assert.Null(denied.Grant);
    }

    [Fact]
    public async Task RemoteControlPlane_SetsUpResourceIdentityProvider()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var result = await controlPlane.SetupResourceIdentityProviderAsync("identity:contract");

        Assert.Equal("identity:contract", result.ProviderId);
        var diagnostic = Assert.Single(result.SetupDiagnostics);
        Assert.Equal(ResourceIdentityProvisioningDiagnosticSeverity.Information, diagnostic.Severity);
        Assert.Equal("identity:contract", diagnostic.ProviderId);
        var setupHandler = app.Services.GetRequiredService<ContractIdentityProviderSetupHandler>();
        var request = Assert.Single(setupHandler.Requests);
        Assert.Equal("identity:contract", request.Provider.Id);
    }

    [Fact]
    public async Task RemoteControlPlane_ExecutesResourceActionWithActingIdentityGrant()
    {
        await using var app = await CreateAppAsync(includeMappedNetwork: true);
        var controlPlane = CreateClient(app);

        var result = await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "network:contract",
                PlatformResourceProvider.ReconcileEndpointMappingsActionId,
                ActingIdentity: ResourceIdentityReference.ForResource("network:contract", "network-service")));
        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    "network:contract",
                    PlatformResourceProvider.ReconcileEndpointMappingsActionId,
                    ActingIdentity: ResourceIdentityReference.ForResource("network:contract", "other-service"))));

        Assert.Equal("Reconciled 1 endpoint mapping(s).", result.Message);
        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);

        var deniedEvent = Assert.Single(await controlPlane.ListResourceEventsAsync(
            new ResourceEventQuery(
                ResourceId: "network:contract",
                EventType: ResourceEventTypes.Actions.ForFailedAction(
                    PlatformResourceProvider.ReconcileEndpointMappingsActionId))));
        Assert.Equal("Warning", deniedEvent.Level);
        Assert.Equal(
            $"Reconcile endpoint mappings action was denied. The '{NetworkResourceOperationPermissions.ReconcileEndpointMappings}' or '{CloudShellPermissions.Resources.Manage}' permission is required for resource 'network:contract'.",
            deniedEvent.Message);
    }

    [Fact]
    public async Task RemoteControlPlane_MapsResourceIdentityAndActionPermissions()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var controlPlane = CreateClient(app);

        var resource = await controlPlane.GetResourceAsync(ContractLifecycleResourceProvider.ResourceId);

        Assert.NotNull(resource);
        Assert.NotNull(resource.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Provider, resource.IdentityBinding.Kind);
        Assert.Equal("contract-lifecycle-service", resource.IdentityBinding.Name);
        Assert.Equal("identity:dev", resource.IdentityBinding.ProviderId);
        Assert.Equal("contract-lifecycle", resource.IdentityBinding.Subject);
        Assert.Equal(["api://cloudshell-control-plane/.default"], resource.IdentityBinding.IdentityScopes);
        Assert.Equal("ContractLifecycle", resource.IdentityBinding.IdentityClaims["appRole"]);
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            ResourceActionPermissions.GetRequiredPermission(resource.StopAction!));
    }

    [Fact]
    public async Task RemoteControlPlane_ReconcilesNetworkEndpointMappings()
    {
        await using var app = await CreateAppAsync(includeMappedNetwork: true);
        var controlPlane = CreateClient(app);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        var network = await controlPlane.GetResourceAsync("network:contract");
        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(["network:contract"]);
        var result = await controlPlane.ExecuteResourceActionAsync(
            "network:contract",
            PlatformResourceProvider.ReconcileEndpointMappingsActionId);

        Assert.NotNull(network);
        var mapping = Assert.Single(network.ResourceEndpointMappings);
        Assert.Equal("mapping:api", mapping.Id);
        Assert.Equal("network:contract", mapping.Source.ResourceId);
        Assert.Equal("api", mapping.Source.EndpointName);
        Assert.Equal("contract:api", mapping.Target.ResourceId);
        Assert.Equal("http", mapping.Target.EndpointName);
        Assert.Equal("contract:proxy", mapping.ProviderResourceId);
        var action = network.GetAction(PlatformResourceProvider.ReconcileEndpointMappingsActionId);
        Assert.NotNull(action);
        Assert.Equal("Reconcile endpoint mappings", action.DisplayName);
        Assert.Equal(
            CloudShellPermissions.Network.Actions.ReconcileEndpointMappings,
            ResourceActionPermissions.GetRequiredPermission(action));
        Assert.True(capabilities["network:contract"].CanExecuteAction(
            PlatformResourceProvider.ReconcileEndpointMappingsActionId));
        Assert.Equal("Reconciled 1 endpoint mapping(s).", result.Message);
        var notification = Assert.Single(notifications);
        Assert.Equal(ResourceChangeKind.ResourceActionExecuted, notification.Kind);
        Assert.Equal("network:contract", notification.ResourceId);
        Assert.Equal(PlatformResourceProvider.ReconcileEndpointMappingsActionId, notification.ActionId);

        var api = await controlPlane.GetResourceAsync(ContractNetworkingResourceProvider.ApiResourceId);
        Assert.NotNull(api);
        Assert.NotNull(api.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, api.IdentityBinding.Kind);
        Assert.Null(api.IdentityBinding.ProviderId);
        Assert.Equal(["api.read"], api.IdentityBinding.IdentityScopes);
    }

    [Fact]
    public async Task RemoteControlPlane_ProjectsLoadBalancerRoutes()
    {
        await using var app = await CreateAppAsync(includeLoadBalancer: true);
        var controlPlane = CreateClient(app);

        var loadBalancer = await controlPlane.GetResourceAsync("load-balancer:public");

        Assert.NotNull(loadBalancer);
        Assert.Equal(PlatformResourceProvider.LoadBalancerResourceType, loadBalancer.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, loadBalancer.ResourceClass);
        Assert.True(loadBalancer.HasCapability(ResourceCapabilityIds.NetworkingLoadBalancer));
        Assert.Equal("traefik", loadBalancer.ResourceAttributes[ResourceAttributeNames.LoadBalancerProvider]);
        Assert.Equal("docker:engine", loadBalancer.ResourceAttributes[ResourceAttributeNames.LoadBalancerHostResourceId]);
        Assert.Contains("docker:engine", loadBalancer.DependsOn);
        var action = loadBalancer.GetAction(PlatformResourceProvider.ApplyLoadBalancerConfigurationActionId);
        Assert.NotNull(action);
        Assert.Equal(
            CloudShellPermissions.Network.Actions.ApplyLoadBalancerConfiguration,
            ResourceActionPermissions.GetRequiredPermission(action));
        Assert.Collection(
            loadBalancer.ResourceLoadBalancerRoutes.OrderBy(route => route.Name, StringComparer.OrdinalIgnoreCase),
            route =>
            {
                Assert.Equal(LoadBalancerRouteKind.Http, route.Kind);
                Assert.Equal("app.local", route.Match.Host);
                Assert.Equal("contract:app", route.Target.ResourceId);
                Assert.Equal("http", route.Target.EndpointName);
            },
            route =>
            {
                Assert.Equal(LoadBalancerRouteKind.Tcp, route.Kind);
                Assert.Equal(5432, route.Match.Port);
                Assert.Equal("contract:postgres", route.Target.ResourceId);
                Assert.Equal("postgres", route.Target.EndpointName);
            });
    }

    [Fact]
    public async Task RemoteControlPlane_UpdatesResourceImage()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        var controlPlane = CreateClient(app);

        var result = await controlPlane.UpdateResourceImageAsync(
            ContractImageResourceProvider.ResourceId,
            "example/api:20260608",
            restartIfRunning: false,
            triggeredBy: "build-server");
        var eventLogs = await controlPlane.ListLogsAsync(
            new LogQuery(ResourceId: ContractImageResourceProvider.ResourceId));
        var eventLog = Assert.Single(eventLogs, log => log.Name == "Activity");
        var events = await controlPlane.ReadLogAsync(eventLog.Id);
        var resourceEvents = await controlPlane.ListResourceEventsAsync(
            new ResourceEventQuery(
                ResourceId: ContractImageResourceProvider.ResourceId,
                EventType: ResourceEventTypes.Events.Deployment.ImageUpdated,
                TriggeredBy: "build-server"));

        Assert.Equal("Updated contract:container-app to example/api:20260608.", result.Message);
        var provider = app.Services.GetRequiredService<ContractImageResourceProvider>();
        Assert.Equal(["example/api:20260608:False:build-server"], provider.UpdatedImages);
        var resourceEvent = Assert.Single(resourceEvents);
        Assert.Equal(ContractImageResourceProvider.ResourceId, resourceEvent.ResourceId);
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, resourceEvent.EventType);
        Assert.Equal("build-server", resourceEvent.TriggeredBy);
        Assert.Equal("Information", resourceEvent.Level);
        Assert.False(string.IsNullOrWhiteSpace(resourceEvent.TraceId));
        Assert.False(string.IsNullOrWhiteSpace(resourceEvent.SpanId));
        Assert.Contains("example/api:20260608", resourceEvent.Message, StringComparison.Ordinal);
        var correlatedEvents = await controlPlane.ListResourceEventsAsync(
            new ResourceEventQuery(
                ResourceId: ContractImageResourceProvider.ResourceId,
                TraceId: resourceEvent.TraceId,
                SpanId: resourceEvent.SpanId,
                MaxEvents: 10));
        Assert.Contains(correlatedEvents, correlatedEvent =>
            correlatedEvent.EventType == ResourceEventTypes.Events.Deployment.ImageUpdated &&
            correlatedEvent.TraceId == resourceEvent.TraceId &&
            correlatedEvent.SpanId == resourceEvent.SpanId);
        Assert.Contains(events, entry =>
            entry.Source == "event" &&
            entry.EventId == ResourceEventTypes.Events.Deployment.ImageUpdated &&
            entry.Category == "CloudShell.ResourceEvents" &&
            HasAttribute(entry, "resourceId", ContractImageResourceProvider.ResourceId) &&
            HasAttribute(entry, "eventType", ResourceEventTypes.Events.Deployment.ImageUpdated) &&
            HasAttribute(entry, "triggeredBy", "build-server") &&
            entry.Message.Contains(ResourceEventTypes.Events.Deployment.ImageUpdated, StringComparison.Ordinal) &&
            entry.Message.Contains("build-server", StringComparison.Ordinal));

        var logResponse = await app.GetTestClient().GetAsync(
            $"/api/control-plane/v1/logs/{Uri.EscapeDataString(eventLog.Id)}/entries");
        Assert.Equal(HttpStatusCode.OK, logResponse.StatusCode);
        using var logDocument = JsonDocument.Parse(await logResponse.Content.ReadAsStringAsync());
        var logEntry = Assert.Single(logDocument.RootElement.EnumerateArray());
        Assert.Equal("Information", logEntry.GetProperty("severity").GetString());
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, logEntry.GetProperty("eventId").GetString());
        Assert.False(logEntry.TryGetProperty("level", out _));
    }

    [Fact]
    public async Task ControlPlaneApi_FiltersResourceEvents()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        var controlPlane = CreateClient(app);
        await controlPlane.UpdateResourceImageAsync(
            ContractImageResourceProvider.ResourceId,
            "example/api:20260609",
            restartIfRunning: false,
            triggeredBy: "build-server");

        var response = await app.GetTestClient().GetAsync(
            "/api/control-plane/v1/resource-events" +
            $"?resourceId={Uri.EscapeDataString(ContractImageResourceProvider.ResourceId)}" +
            $"&eventType={Uri.EscapeDataString(ResourceEventTypes.Events.Deployment.ImageUpdated)}" +
            "&triggeredBy=build-server&maxEvents=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resourceEvent = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(ContractImageResourceProvider.ResourceId, resourceEvent.GetProperty("resourceId").GetString());
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, resourceEvent.GetProperty("eventType").GetString());
        Assert.Equal("build-server", resourceEvent.GetProperty("triggeredBy").GetString());
        Assert.Equal("Information", resourceEvent.GetProperty("level").GetString());
        Assert.Contains("example/api:20260609", resourceEvent.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteControlPlane_UpdatesResourceReplicas()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        var controlPlane = CreateClient(app);

        var result = await controlPlane.UpdateResourceReplicasAsync(
            ContractImageResourceProvider.ResourceId,
            3,
            restartIfRunning: false,
            triggeredBy: "load-balancer");
        var eventLogs = await controlPlane.ListLogsAsync(
            new LogQuery(ResourceId: ContractImageResourceProvider.ResourceId));
        var eventLog = Assert.Single(eventLogs, log => log.Name == "Activity");
        var events = await controlPlane.ReadLogAsync(eventLog.Id);

        Assert.Equal("Updated contract:container-app to 3 replicas.", result.Message);
        var provider = app.Services.GetRequiredService<ContractImageResourceProvider>();
        Assert.Equal(["3:False:load-balancer"], provider.UpdatedReplicas);
        Assert.Contains(events, entry =>
            entry.Source == "event" &&
            entry.Message.Contains(ResourceEventTypes.Events.Deployment.ReplicasUpdated, StringComparison.Ordinal) &&
            entry.Message.Contains("load-balancer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ControlPlaneApi_ExposesResourceActionsAsHypermediaAffordances()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/control-plane/v1/resources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = document.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ContractLifecycleResourceProvider.ResourceId);
        var actions = resource.GetProperty("resourceActions");
        var stop = actions.GetProperty(ResourceActionIds.Stop);

        Assert.False(resource.TryGetProperty("actions", out _));
        Assert.Equal((int)ResourceClass.Executable, resource.GetProperty("resourceClass").GetInt32());
        Assert.Equal(JsonValueKind.Object, resource.GetProperty("attributes").ValueKind);
        Assert.Equal(JsonValueKind.Object, actions.ValueKind);
        Assert.Equal(ResourceActionIds.Stop, stop.GetProperty("id").GetString());
        Assert.Equal("Stop", stop.GetProperty("displayName").GetString());
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            stop.GetProperty("requiredPermission").GetString());
        Assert.Equal("POST", stop.GetProperty("method").GetString());
        Assert.Equal(
            "/api/control-plane/v1/resources/contract%3Alifecycle/actions/stop",
            stop.GetProperty("href").GetString());
        var identity = resource.GetProperty("identity");
        Assert.Equal(
            (int)ResourceIdentityBindingKind.Provider,
            identity.GetProperty("kind").GetInt32());
        Assert.Equal("contract-lifecycle-service", identity.GetProperty("name").GetString());
        Assert.Equal("identity:dev", identity.GetProperty("providerId").GetString());
        Assert.Equal("contract-lifecycle", identity.GetProperty("subject").GetString());
        Assert.Equal(
            "api://cloudshell-control-plane/.default",
            Assert.Single(identity.GetProperty("scopes").EnumerateArray()).GetString());
        Assert.Equal("ContractLifecycle", identity.GetProperty("claims").GetProperty("appRole").GetString());
    }

    [Fact]
    public async Task ControlPlaneOpenApi_DescribesDomainShapedResourceProjection()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/openapi/control-plane-v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(nameof(ResourceResponse), out var resourceSchema));
        Assert.True(schemas.TryGetProperty(nameof(ResourceActionResponse), out _));

        var resourceProperties = resourceSchema.GetProperty("properties");
        Assert.True(resourceProperties.TryGetProperty("resourceClass", out _));
        Assert.True(resourceProperties.TryGetProperty("identity", out _));
        Assert.True(resourceProperties.TryGetProperty("attributes", out var attributes));
        Assert.Equal("object", attributes.GetProperty("type").GetString());
        Assert.Equal(
            "string",
            attributes.GetProperty("additionalProperties").GetProperty("type").GetString());

        var resourceActions = resourceProperties.GetProperty("resourceActions");
        Assert.Equal("object", resourceActions.GetProperty("type").GetString());
        Assert.Equal(
            "#/components/schemas/ResourceActionResponse",
            resourceActions.GetProperty("additionalProperties").GetProperty("$ref").GetString());

        var listResources = root
            .GetProperty("paths")
            .GetProperty("/api/control-plane/v1/resources")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        Assert.Equal("array", listResources.GetProperty("type").GetString());
        Assert.Equal(
            "#/components/schemas/ResourceResponse",
            listResources.GetProperty("items").GetProperty("$ref").GetString());

        var paths = root.GetProperty("paths");
        Assert.False(paths.TryGetProperty("/api/control-plane/v1/resources/{resourceId}/image", out _));
        Assert.True(paths.TryGetProperty("/api/container-apps/v1/{containerAppId}/revisions", out _));
        Assert.True(paths.TryGetProperty("/api/container-apps/v1/{containerAppId}/replicas", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants/evaluate", out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePermissionGrantResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePermissionEvaluationResponse), out _));

        var createResource = schemas.GetProperty(nameof(CreateResourceRequest));
        Assert.Equal(
            "boolean",
            createResource
                .GetProperty("properties")
                .GetProperty("startAfterCreate")
                .GetProperty("type")
                .GetString());
    }

    [Fact]
    public async Task RemoteControlPlane_MapsLogsTracesAndMetrics()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var logs = await controlPlane.ListLogsAsync(new LogQuery(ResourceId: "network:contract"));
        var resourceEvents = Assert.Single(logs);
        Assert.Equal("Activity", resourceEvents.Name);
        Assert.Equal("network:contract", resourceEvents.ResourceId);

        var span = new TraceSpan(
            "trace-contract",
            "span-contract",
            null,
            "GET /contract",
            "network:contract",
            "contract-service",
            "server",
            "ok",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(42),
            new Dictionary<string, string>
            {
                ["http.method"] = "GET"
            });
        await controlPlane.IngestTraceSpansAsync([span]);

        var spans = await controlPlane.ListTraceSpansAsync(
            new TraceQuery(ResourceId: "network:contract", TraceId: "trace-contract", MaxSpans: 10));

        var remoteSpan = Assert.Single(spans);
        Assert.Equal(span.TraceId, remoteSpan.TraceId);
        Assert.Equal(span.SpanId, remoteSpan.SpanId);
        Assert.Equal(span.ResourceId, remoteSpan.ResourceId);
        Assert.Equal("GET", remoteSpan.SpanAttributes["http.method"]);

        var metric = new MetricPoint(
            "http.server.requests",
            "network:contract",
            "contract-service",
            1,
            DateTimeOffset.UtcNow,
            "count",
            new Dictionary<string, string>
            {
                ["http.method"] = "GET"
            });
        await controlPlane.IngestMetricPointsAsync([metric]);

        var points = await controlPlane.ListMetricPointsAsync(
            new MetricQuery(ResourceId: "network:contract", MetricName: "http.server.requests", MaxPoints: 10));

        var remotePoint = Assert.Single(points);
        Assert.Equal(metric.Name, remotePoint.Name);
        Assert.Equal(metric.ResourceId, remotePoint.ResourceId);
        Assert.Equal(metric.ServiceName, remotePoint.ServiceName);
        Assert.Equal(metric.Value, remotePoint.Value);
        Assert.Equal(metric.Unit, remotePoint.Unit);
        Assert.Equal("GET", remotePoint.MetricAttributes["http.method"]);
    }

    [Fact]
    public async Task RemoteControlPlane_MapsResourceMonitoring()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        var controlPlane = CreateClient(app);

        var hasMonitoring = await controlPlane.HasResourceMonitoringAsync(
            ContractImageResourceProvider.ResourceId);
        var snapshot = await controlPlane.GetResourceMonitoringAsync(
            ContractImageResourceProvider.ResourceId);
        var missingSnapshot = await controlPlane.GetResourceMonitoringAsync("missing:resource");

        Assert.True(hasMonitoring);
        Assert.NotNull(snapshot);
        Assert.Equal(ContractImageResourceProvider.ResourceId, snapshot.ResourceId);
        Assert.Equal("Contract Container App", snapshot.Provider);
        Assert.Equal("Available", snapshot.Status);
        var cpu = Assert.Single(snapshot.Metrics, metric => metric.Name == "resource.cpu.usage");
        Assert.Equal(12.5, cpu.Value);
        Assert.Equal("%", cpu.Unit);
        Assert.Equal("CPU usage", cpu.DisplayName);
        Assert.Null(missingSnapshot);
    }

    [Fact]
    public async Task RemoteSettingsProvider_ManagesEnvironmentSettingsWhenAuthenticationIsDisabled()
    {
        await using var app = await CreateAppAsync();
        var settingsProvider = CreateSettingsClient(app);

        await settingsProvider.SetSettingAsync(CloudShellUserSettingKeys.ThemeMode, "Dark");

        var setting = await settingsProvider.GetSettingAsync(CloudShellUserSettingKeys.ThemeMode);
        var settings = await settingsProvider.GetSettingsAsync();

        Assert.Equal("Dark", setting?.Value);
        Assert.Equal("Dark", settings[CloudShellUserSettingKeys.ThemeMode].Value);

        await settingsProvider.RemoveSettingAsync(CloudShellUserSettingKeys.ThemeMode);

        Assert.Null(await settingsProvider.GetSettingAsync(CloudShellUserSettingKeys.ThemeMode));
    }

    [Fact]
    public async Task RemoteControlPlane_ExportsAndImportsResourceGroupTemplates()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        var exported = await controlPlane.ExportResourceGroupTemplateAsync(group.Id);

        Assert.Equal("resourceGroup", exported.Template.Kind);
        Assert.Equal("Contract Group", exported.Template.Name);
        Assert.Empty(exported.Template.Resources);
        Assert.Contains(
            exported.Diagnostics,
            diagnostic => diagnostic.Severity == "Warning" &&
                diagnostic.ResourceName == "Contract Network");

        var imported = await controlPlane.ImportResourceGroupTemplateAsync(
            exported.Template with
            {
                Name = "Imported Contract Group",
                Description = "Imported through the remote control-plane contract."
            });

        Assert.NotNull(imported.ResourceGroup);
        Assert.Equal("Imported Contract Group", imported.ResourceGroup.Name);
        Assert.Empty(imported.ImportedResources);
        Assert.Empty(imported.Diagnostics);
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCreateResourceGroupRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resource-groups",
            new
            {
                name = " ",
                description = "Invalid group"
            });

        await AssertProblemAsync(response, "Name is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCreateResourceRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceType = PlatformResourceProvider.NetworkResourceType,
                resourceId = "network:invalid",
                name = "Invalid Network",
                configuration = (object?)null
            });

        await AssertProblemAsync(response, "Configuration is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForResourceClassMismatch()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceType = PlatformResourceProvider.ServiceResourceType,
                resourceId = "service:invalid",
                name = "Invalid Service",
                configuration = new ServiceResourceDefinition(
                    "service:invalid",
                    "Invalid Service",
                    [],
                    [],
                    []),
                resourceClass = ResourceClass.Network
            });

        await AssertProblemAsync(
            response,
            "Resource 'service:invalid' uses type 'cloudshell.service' which requires class 'Service', but creation request declares class 'Network'.",
            ControlPlaneErrorCodes.ResourceClassMismatch);
    }

    [Fact]
    public async Task RemoteControlPlane_ThrowsContractErrorForInvalidRequest()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.CreateResourceGroupAsync(
                new CreateResourceGroupCommand(" ", "Invalid group")));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Equal("Name is required.", exception.Message);
    }

    [Fact]
    public async Task RemoteControlPlane_ThrowsContractErrorForMissingResourceAction()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var controlPlane = CreateClient(app);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                ContractLifecycleResourceProvider.ResourceId,
                "missing"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionNotFound, exception.Error.Code);
        Assert.Equal(
            "Resource 'contract:lifecycle' does not expose action 'missing'.",
            exception.Message);
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForSettingReferenceResolutionFailure()
    {
        await using var app = await CreateAppAsync(includeResolutionFailureResource: true);
        var client = app.GetTestClient();

        var response = await client.PostAsync(
            "/api/control-plane/v1/resources/contract%3Aresolution-failure/actions/start",
            null);

        await AssertProblemAsync(
            response,
            "Could not resolve secret reference for setting 'SAMPLE_API_KEY'. Secret 'sample-api-key' was not found in Secrets Vault 'secrets-vault:app'.",
            ControlPlaneErrorCodes.ResourceActionUnavailable,
            expectedExtensions: new Dictionary<string, string>
            {
                ["settingName"] = "SAMPLE_API_KEY",
                ["referenceKind"] = "secret"
            });
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForMissingDeleteResource()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.DeleteAsync("/api/control-plane/v1/resources/missing");

        await AssertProblemAsync(
            response,
            "Resource 'missing' is not registered.",
            ControlPlaneErrorCodes.ResourceNotRegistered,
            HttpStatusCode.NotFound,
            "Resource not found");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCapabilitiesRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources/capabilities",
            new
            {
                resourceIds = (string[]?)null
            });

        await AssertProblemAsync(response, "ResourceIds is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidRegistrationDependencies()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/registrations",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceId = "network:contract",
                dependsOn = new[] { " " }
            });

        await AssertProblemAsync(response, "DependsOn cannot contain empty values.");
    }

    private static RemoteControlPlane CreateClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return new RemoteControlPlane(client);
    }

    private static RemoteCloudShellUserSettingsProvider CreateSettingsClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return new RemoteCloudShellUserSettingsProvider(client);
    }

    private static async Task<WebApplication> CreateAppAsync(
        bool includeLifecycleResource = false,
        bool includeImageResource = false,
        bool includeMappedNetwork = false,
        bool includeLoadBalancer = false,
        bool includeStatelessResource = false,
        bool includeResolutionFailureResource = false,
        bool includeRuntimeResource = false)
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Enabled"] = "false",
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-client-contract.db",
            ["Persistence:IdentityConnectionString"] = "Data Source=Data/identity-client-contract.db"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        builder.Services.AddSingleton<ContractIdentityProviderSetupHandler>();
        builder.Services.AddSingleton<IResourceIdentityProviderSetupHandler>(serviceProvider =>
            serviceProvider.GetRequiredService<ContractIdentityProviderSetupHandler>());
        if (includeLifecycleResource)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractLifecycleResourceProvider>();
        }
        if (includeImageResource)
        {
            builder.Services.AddSingleton<ContractImageResourceProvider>();
            builder.Services.AddSingleton<IResourceProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<ContractImageResourceProvider>());
            builder.Services.AddSingleton<IResourceMonitoringProvider>(serviceProvider =>
                serviceProvider.GetRequiredService<ContractImageResourceProvider>());
        }
        if (includeMappedNetwork)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractNetworkingResourceProvider>();
        }
        if (includeStatelessResource)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractStatelessResourceProvider>();
        }
        if (includeResolutionFailureResource)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractResolutionFailureProvider>();
        }
        if (includeRuntimeResource)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractRuntimeResourceProvider>();
        }

        controlPlane.Resources(resources =>
        {
            resources.AddIdentityProvider(
                "identity:contract",
                "Contract identity",
                ResourceIdentityProviderKind.Custom);
            var network = resources
                .AddNetwork("network:contract", isDefault: true)
                .WithDisplayName("Contract Network")
                .Persist()
                .WithIdentity("development", name: "network-service");
            network.Allow(
                network.Identity,
                NetworkResourceOperationPermissions.ReconcileEndpointMappings);
            if (includeMappedNetwork)
            {
                var api = resources.Declare(
                    ContractNetworkingResourceProvider.ProviderId,
                    ContractNetworkingResourceProvider.ApiResourceId);
                var proxy = resources.Declare(
                    ContractNetworkingResourceProvider.ProviderId,
                    ContractNetworkingResourceProvider.ProxyResourceId);
                var endpoint = network.RequestHttpEndpoint("api");

                network.MapEndpoint(
                    endpoint,
                    new ResourceEndpointReference(api.ResourceId, "http"),
                    proxy,
                    "mapping:api");
            }
            if (includeLoadBalancer)
            {
                var dockerHost = resources.Declare("docker", "docker:engine");
                var app = resources.Declare("applications", "contract:app");
                var postgres = resources.Declare("applications", "contract:postgres");
                resources
                    .AddLoadBalancer("public")
                    .UseProvider("traefik")
                    .UseContainerHost(dockerHost)
                    .ExposeHttp(8080)
                    .ExposeTcp(5432)
                    .MapHost("app.local", app, endpoint: "http")
                    .MapTcp(5432, postgres, endpoint: "postgres");
            }
        });

        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();

        if (includeLifecycleResource)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var registrations = scope.ServiceProvider.GetRequiredService<IResourceRegistrationStore>();
            await registrations.RegisterAsync(
                ContractLifecycleResourceProvider.ProviderId,
                ContractLifecycleResourceProvider.ResourceId);
        }
        if (includeImageResource)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var registrations = scope.ServiceProvider.GetRequiredService<IResourceRegistrationStore>();
            await registrations.RegisterAsync(
                ContractImageResourceProvider.ProviderId,
                ContractImageResourceProvider.ResourceId);
        }
        if (includeResolutionFailureResource)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var registrations = scope.ServiceProvider.GetRequiredService<IResourceRegistrationStore>();
            await registrations.RegisterAsync(
                ContractResolutionFailureProvider.ProviderId,
                ContractResolutionFailureProvider.ResourceId);
        }
        if (includeStatelessResource)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var registrations = scope.ServiceProvider.GetRequiredService<IResourceRegistrationStore>();
            await registrations.RegisterAsync(
                ContractStatelessResourceProvider.ProviderId,
                ContractStatelessResourceProvider.ResourceId);
        }

        return app;
    }

    private static async Task<ResourceGroup> CreateContractGroupAsync(IResourceManager resources)
    {
        var group = await resources.CreateResourceGroupAsync(
            new CreateResourceGroupCommand(
                "Contract Group",
                "Group used by remote contract tests"));
        await resources.AssignResourceGroupAsync(
            new AssignResourceGroupCommand("network:contract", group.Id));
        return group;
    }

    private static bool HasAttribute(LogEntry entry, string key, string value) =>
        entry.Attributes?.TryGetValue(key, out var actual) is true &&
        string.Equals(actual, value, StringComparison.Ordinal);

    private sealed class ContractIdentityProviderSetupHandler : IResourceIdentityProviderSetupHandler
    {
        public List<ResourceIdentityProviderSetupRequest> Requests { get; } = [];

        public string ProviderId => "contract";

        public bool CanSetup(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, "identity:contract", StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProviderSetupResult> SetupAsync(
            ResourceIdentityProviderSetupRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProviderSetupResult(
                request.Provider.Id,
                [
                    new ResourceIdentityProvisioningDiagnostic(
                        ResourceIdentityProvisioningDiagnosticSeverity.Information,
                        "Contract identity provider setup completed.",
                        ProviderId: request.Provider.Id)
                ]));
        }
    }

    private sealed class ContractNetworkingResourceProvider : IResourceProvider
    {
        public const string ProviderId = "contract.networking";
        public const string ApiResourceId = "contract:api";
        public const string ProxyResourceId = "contract:proxy";

        public string Id => ProviderId;

        public string DisplayName => "Contract Networking";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ApiResourceId,
                "Contract API",
                "Application",
                DisplayName,
                "local",
                ResourceState.Running,
                [ResourceEndpoint.Http("http", "localhost", 8080, ResourceExposureScope.Public)],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                Identity: ResourceIdentityBinding.RequireIdentity(["api.read"]),
                Capabilities: [new(ResourceCapabilityIds.EndpointSource)]),
            new(
                ProxyResourceId,
                "Contract Proxy",
                "Networking Provider",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                Capabilities: [new(ResourceCapabilityIds.NetworkingEndpointMapper)])
        ];
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        string expectedDetail,
        string expectedCode = ControlPlaneErrorCodes.InvalidRequest,
        HttpStatusCode expectedStatusCode = HttpStatusCode.BadRequest,
        string expectedTitle = "Control plane request failed",
        IReadOnlyDictionary<string, string>? expectedExtensions = null)
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedTitle, document.RootElement.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, document.RootElement.GetProperty("detail").GetString());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
        foreach (var extension in expectedExtensions ?? new Dictionary<string, string>())
        {
            Assert.Equal(extension.Value, document.RootElement.GetProperty(extension.Key).GetString());
        }
    }

    private sealed class ContractLifecycleResourceProvider : IResourceProvider
    {
        public const string ProviderId = "contract.lifecycle";
        public const string ResourceId = "contract:lifecycle";

        public string Id => ProviderId;

        public string DisplayName => "Contract Lifecycle";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Lifecycle",
                "Lifecycle",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "contract.lifecycle",
                ResourceClass: ResourceClass.Executable,
                Identity: new ResourceIdentityBinding(
                    "identity:dev",
                    "contract-lifecycle",
                    ["api://cloudshell-control-plane/.default"],
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["appRole"] = "ContractLifecycle"
                    },
                    Name: "contract-lifecycle-service"),
                Actions:
                [
                    ResourceAction.Stop,
                    ResourceAction.Restart
                ])
        ];
    }

    private sealed class ContractStatelessResourceProvider : IResourceProvider
    {
        public const string ProviderId = "contract.stateless";
        public const string ResourceId = "contract:stateless";

        public string Id => ProviderId;

        public string DisplayName => "Contract Stateless";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Stateless",
                "Logical Model",
                DisplayName,
                "local",
                null,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "contract.stateless",
                ResourceClass: ResourceClass.Infrastructure)
        ];
    }

    private sealed class ContractRuntimeResourceProvider : IResourceProvider
    {
        public const string ResourceId = "container:api-replica-1";

        public string Id => "contract.runtime";

        public string DisplayName => "Contract Runtime";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "API replica 1",
                "Container instance",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                ParentResourceId: "network:contract",
                TypeId: "runtime.container",
                ResourceClass: ResourceClass.Container,
                Source: ResourceSource.RuntimeController,
                ManagementMode: ResourceManagementMode.RuntimeManaged,
                Visibility: ResourceVisibility.Hidden,
                OwnerResourceId: "network:contract",
                CleanupBehavior: ResourceCleanupBehavior.DeleteWithOwner)
        ];
    }

    private sealed class ContractResolutionFailureProvider : IResourceProvider, IResourceProcedureProvider
    {
        public const string ProviderId = "contract.resolution-failure";
        public const string ResourceId = "contract:resolution-failure";

        public string Id => ProviderId;

        public string DisplayName => "Contract Resolution Failure";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Resolution Failure",
                "Application",
                DisplayName,
                "local",
                ResourceState.Stopped,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "application.executable",
                ResourceClass: ResourceClass.Executable,
                Actions: [ResourceAction.Start])
        ];

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed("Deleted."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default) =>
            throw new ResourceSettingResolutionException(
                "SAMPLE_API_KEY",
                "secret",
                "Secret 'sample-api-key' was not found in Secrets Vault 'secrets-vault:app'.");
    }

    private sealed class ContractImageResourceProvider :
        IResourceProvider,
        IResourceImageUpdateProvider,
        IResourceReplicaUpdateProvider,
        IResourceMonitoringProvider
    {
        public const string ProviderId = "contract.container-app";
        public const string ResourceId = "contract:container-app";

        public string Id => ProviderId;

        public string DisplayName => "Contract Container App";

        public List<string> UpdatedImages { get; } = [];

        public List<string> UpdatedReplicas { get; } = [];

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Container App",
                "Container app",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "example/api:latest",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "application.container-app",
                ResourceClass: ResourceClass.Container,
                Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ResourceAttributeNames.WorkloadKind] = ResourceWorkloadKind.ContainerImage.ToString(),
                    [ResourceAttributeNames.ContainerImage] = "example/api:latest",
                    [ResourceAttributeNames.ContainerReplicas] = "1"
                })
        ];

        public bool CanUpdateImage(Resource resource) =>
            string.Equals(resource.Id, ResourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceProcedureResult> UpdateImageAsync(
            ResourceProcedureContext context,
            string image,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
            UpdatedImages.Add($"{image}:{restartIfRunning}:{triggeredBy}");
            return Task.FromResult(ResourceProcedureResult.Completed(
                $"Updated {context.Resource.Id} to {image}."));
        }

        public bool CanUpdateReplicas(Resource resource) =>
            string.Equals(resource.Id, ResourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceProcedureResult> UpdateReplicasAsync(
            ResourceProcedureContext context,
            int replicas,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
            UpdatedReplicas.Add($"{replicas}:{restartIfRunning}:{triggeredBy}");
            return Task.FromResult(ResourceProcedureResult.Completed(
                $"Updated {context.Resource.Id} to {replicas} replicas."));
        }

        public bool CanMonitor(Resource resource) =>
            string.Equals(resource.Id, ResourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceMonitoringSnapshot?> GetMonitoringSnapshotAsync(
            Resource resource,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResourceMonitoringSnapshot?>(new ResourceMonitoringSnapshot(
                resource.Id,
                DisplayName,
                DateTimeOffset.UtcNow,
                [
                    new ResourceMetricSample(
                        "resource.cpu.usage",
                        12.5,
                        "%",
                        DateTimeOffset.UtcNow,
                        "CPU usage",
                        "Provider-observed CPU usage.")
                ],
                "Available",
                "Contract monitoring snapshot."));
    }
}
