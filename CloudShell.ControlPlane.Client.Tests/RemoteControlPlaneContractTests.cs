using CloudShell.Abstractions.Authorization;
using System.Text.Json;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.Abstractions.Usage;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using ResourceDefinitionApplyMode = CloudShell.ResourceModel.ResourceDefinitionApplyMode;
using ResourceDefinitionDiagnostic = CloudShell.ResourceModel.ResourceDefinitionDiagnostic;
using ResourceDefinitionTemplate = CloudShell.ResourceModel.ResourceTemplate;
using ResourceDefinitionValidationResult = CloudShell.ResourceModel.ResourceDefinitionValidationResult;
using ResourceTypeId = CloudShell.ResourceModel.ResourceTypeId;

namespace CloudShell.ControlPlane.Client.Tests;

public sealed class RemoteControlPlaneContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private const string ProviderDiagnosticsLogSourceId = "contract:provider-diagnostics";
    private const string ProviderDiagnosticsMessage = "Provider diagnostics are available.";

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

        var principals = await controlPlane.QueryResourcePrincipalsAsync(
            new ResourcePrincipalQuery(
                Kinds: new HashSet<ResourcePrincipalKind>
                {
                    ResourcePrincipalKind.ResourceIdentity
                }));
        var grants = await controlPlane.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "network:contract"));
        var grantStatuses = await controlPlane.ListResourcePermissionGrantStatusesAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "network:contract"));
        var allowed = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            NetworkResourceOperationPermissions.ReconcileEndpointMappings);
        var denied = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            LoadBalancerResourceOperationPermissions.ApplyConfiguration);
        await controlPlane.GrantResourcePermissionAsync(
            new GrantResourcePermissionCommand(
                ResourcePrincipalReference.ForResourceIdentity("network:contract", "network-service"),
                "network:contract",
                LoadBalancerResourceOperationPermissions.ApplyConfiguration));
        var granted = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            LoadBalancerResourceOperationPermissions.ApplyConfiguration);
        await controlPlane.RevokeResourcePermissionAsync(
            new RevokeResourcePermissionCommand(
                ResourcePrincipalReference.ForResourceIdentity("network:contract", "network-service"),
                "network:contract",
                LoadBalancerResourceOperationPermissions.ApplyConfiguration));
        var revoked = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("network:contract", "network-service"),
            "network:contract",
            LoadBalancerResourceOperationPermissions.ApplyConfiguration);

        var grant = Assert.Single(grants);
        Assert.Equal("network:contract", grant.Principal.SourceResourceId);
        Assert.Equal("network-service", grant.Principal.SourceIdentityName);
        Assert.Equal("network:contract", grant.TargetResourceId);
        Assert.Equal(NetworkResourceOperationPermissions.ReconcileEndpointMappings, grant.Permission);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("network:contract/identities/network-service", grant.Principal.Id);
        var grantStatus = Assert.Single(grantStatuses);
        Assert.Equal(grant, grantStatus.Grant);
        Assert.Equal(ResourcePermissionGrantEffectivenessState.Unknown, grantStatus.State);

        var resourcePrincipal = Assert.Single(principals, principal =>
            principal.Reference.SourceResourceId == "network:contract");
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, resourcePrincipal.Reference.Kind);
        Assert.Equal("network:contract/identities/network-service", resourcePrincipal.Reference.Id);
        Assert.Equal("network-service", resourcePrincipal.Reference.SourceIdentityName);
        Assert.Equal("Contract Network", resourcePrincipal.DisplayName);
        Assert.Equal("network:contract", resourcePrincipal.PrincipalAttributes["resourceId"]);

        var httpClient = app.GetTestClient();
        var principalsResponse = await httpClient.GetAsync(
            "/api/control-plane/v1/resource-principals?kinds=ResourceIdentity&searchText=network-service");
        principalsResponse.EnsureSuccessStatusCode();
        using var principalsDocument = JsonDocument.Parse(await principalsResponse.Content.ReadAsStringAsync());
        var responsePrincipal = Assert.Single(principalsDocument.RootElement.EnumerateArray());
        Assert.Equal(
            (int)ResourcePrincipalKind.ResourceIdentity,
            responsePrincipal.GetProperty("reference").GetProperty("kind").GetInt32());
        Assert.Equal(
            "network:contract/identities/network-service",
            responsePrincipal.GetProperty("reference").GetProperty("id").GetString());

        var response = await httpClient.GetAsync(
            "/api/control-plane/v1/resource-permission-grants?targetResourceId=network%3Acontract");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var responseGrant = Assert.Single(document.RootElement.EnumerateArray());
        var principal = responseGrant.GetProperty("principal");
        Assert.Equal((int)ResourcePrincipalKind.ResourceIdentity, principal.GetProperty("kind").GetInt32());
        Assert.Equal("network:contract/identities/network-service", principal.GetProperty("id").GetString());

        var statusResponse = await httpClient.GetAsync(
            "/api/control-plane/v1/resource-permission-grants/status?targetResourceId=network%3Acontract");
        statusResponse.EnsureSuccessStatusCode();
        using var statusDocument = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
        var responseGrantStatus = Assert.Single(statusDocument.RootElement.EnumerateArray());
        Assert.Equal(
            (int)ResourcePermissionGrantEffectivenessState.Unknown,
            responseGrantStatus.GetProperty("state").GetInt32());
        Assert.Equal(
            NetworkResourceOperationPermissions.ReconcileEndpointMappings,
            responseGrantStatus.GetProperty("grant").GetProperty("permission").GetString());
        Assert.Equal("network:contract", principal.GetProperty("sourceResourceId").GetString());
        Assert.Equal("network-service", principal.GetProperty("sourceIdentityName").GetString());

        Assert.True(allowed.IsAllowed);
        Assert.NotNull(allowed.Grant);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, allowed.Principal.Kind);
        Assert.False(denied.IsAllowed);
        Assert.Null(denied.Grant);
        Assert.True(granted.IsAllowed);
        Assert.NotNull(granted.Grant);
        Assert.False(revoked.IsAllowed);
        Assert.Null(revoked.Grant);
    }

    [Fact]
    public async Task RemoteControlPlane_ListsProviderDirectoryPrincipals()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var principals = await controlPlane.QueryResourcePrincipalsAsync(
            new ResourcePrincipalQuery(SearchText: "platform", ProviderId: "identity:contract"));

        var principal = Assert.Single(principals, item => item.Reference.Kind == ResourcePrincipalKind.User);
        Assert.Equal("platform-user", principal.Reference.Id);
        Assert.Equal("identity:contract", principal.Reference.ProviderId);
        Assert.Equal("platform@example.local", principal.PrincipalAttributes["mail"]);

        var directoryProvider = app.Services.GetRequiredService<ContractIdentityDirectoryProvider>();
        var request = Assert.Single(directoryProvider.Requests);
        Assert.Equal("identity:contract", request.Provider.Id);
        Assert.Equal("platform", request.Query.SearchText);
    }

    [Fact]
    public async Task RemoteControlPlane_SetsResourceIdentity()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var controlPlane = CreateClient(app);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        await controlPlane.SetResourceIdentityAsync(
            new SetResourceIdentityCommand(
                ContractLifecycleResourceProvider.ResourceId,
                ResourceIdentityBinding.RequireIdentity() with { Name = "contract-resource" }));

        var registration = await controlPlane.GetResourceRegistrationAsync(
            ContractLifecycleResourceProvider.ResourceId);

        Assert.NotNull(registration);
        Assert.NotNull(registration.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, registration.IdentityBinding.Kind);
        Assert.Equal("contract-resource", registration.IdentityBinding.Name);
        Assert.Contains(notifications, notification =>
            notification.Kind == ResourceChangeKind.ResourceIdentityChanged &&
            notification.ResourceId == ContractLifecycleResourceProvider.ResourceId);

        await controlPlane.SetResourceIdentityAsync(
            new SetResourceIdentityCommand(ContractLifecycleResourceProvider.ResourceId, null));
        registration = await controlPlane.GetResourceRegistrationAsync(
            ContractLifecycleResourceProvider.ResourceId);

        Assert.NotNull(registration);
        Assert.Null(registration.IdentityBinding);
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
        Assert.Equal(ResourceSignalSeverity.Warning, deniedEvent.Severity);
        Assert.Equal(
            $"Reconcile endpoint mappings action was denied. The '{NetworkResourceOperationPermissions.ReconcileEndpointMappings}' or '{CloudShellPermissions.Resources.Manage}' permission is required for resource 'contract'.",
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
            PlatformResourceProvider.ReconcileEndpointMappingsActionId,
            cause: "Topology drift detected.");

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
        var resourceEvents = await controlPlane.ListResourceEventsAsync(new ResourceEventQuery(
            ResourceId: "network:contract",
            EventType: ResourceEventTypes.Actions.ForAction(
                PlatformResourceProvider.ReconcileEndpointMappingsActionId)));
        Assert.Contains(resourceEvents, resourceEvent =>
            resourceEvent.Message.Contains("Cause: Topology drift detected.", StringComparison.Ordinal));
        var networkLogSources = await controlPlane.ListLogSourcesAsync(new LogQuery(ResourceId: "network:contract"));
        var activityLog = Assert.Single(networkLogSources, log => log.Name == "Activity");
        Assert.Equal(ResourceLogSourceKind.Activity, activityLog.Kind);
        Assert.Equal(LogFormat.ResourceEvent, activityLog.Format);
        Assert.True(activityLog.Capabilities.HasFlag(LogSourceCapabilities.Read));
        Assert.True(activityLog.Capabilities.HasFlag(LogSourceCapabilities.Query));
        Assert.True(activityLog.Capabilities.HasFlag(LogSourceCapabilities.StructuredFields));
        Assert.Equal(ResourceLogSourceOrigin.ProviderProjected, activityLog.Origin);

        var api = await controlPlane.GetResourceAsync(ContractNetworkingResourceProvider.ApiResourceId);
        Assert.NotNull(api);
        Assert.NotNull(api.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, api.IdentityBinding.Kind);
        Assert.Null(api.IdentityBinding.ProviderId);
        Assert.Equal(["api.read"], api.IdentityBinding.IdentityScopes);
        Assert.True(api.EffectiveObservability.Traces);
        Assert.True(api.EffectiveObservability.Metrics);
        var logSource = Assert.Single(api.ResourceLogSources);
        Assert.Equal("console", logSource.Id);
        Assert.Equal("Console logs", logSource.Name);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, logSource.Kind);
        Assert.Equal(LogFormat.JsonConsole, logSource.Format);
        Assert.Equal(LogStorageKind.InMemory, logSource.Storage.Kind);
        Assert.True(logSource.Capabilities.HasFlag(LogSourceCapabilities.Read));
        Assert.True(logSource.Capabilities.HasFlag(LogSourceCapabilities.Stream));
        Assert.True(logSource.Capabilities.HasFlag(LogSourceCapabilities.StructuredFields));
        Assert.Equal(ResourceLogSourceOrigin.ProviderDefault, logSource.Origin);
        Assert.Equal(ResourceLogSourcePurpose.Default, logSource.Purpose);
        Assert.Equal(LogSourceAvailability.ResourceRunning, logSource.Availability);
        var projectedLogSource = Assert.Single(
            await controlPlane.ListLogSourcesAsync(new LogQuery(ResourceId: api.Id)),
            source => source.Name == "Console logs");
        Assert.Equal("contract:api:log-source:console", projectedLogSource.Id);
        Assert.Equal(api.Id, projectedLogSource.ResourceId);
        Assert.Equal("Contract API", projectedLogSource.SourceName);
        Assert.Equal(ResourceLogSourceKind.ProcessOutput, projectedLogSource.Kind);
        Assert.Equal(LogFormat.JsonConsole, projectedLogSource.Format);
        Assert.Equal(LogSourceAvailability.ResourceRunning, projectedLogSource.Availability);
        Assert.True(projectedLogSource.SupportsStreaming);
        var source = Assert.Single(api.EffectiveObservability.TelemetrySources);
        Assert.Equal("contract-api-otlp", source.Id);
        Assert.Equal(TelemetrySourceKind.Exporter, source.Kind);
        Assert.True(source.Signals.HasFlag(TelemetrySignalKind.Traces));
        Assert.True(source.Signals.HasFlag(TelemetrySignalKind.Metrics));
        var scopes = api.EffectiveObservability.TelemetryScopes;
        Assert.Equal(2, scopes.Count);
        Assert.Contains(scopes, scope =>
            scope.ScopeResourceId == "contract:api:replica:0" &&
            scope.Name == "Replica 0" &&
            scope.Kind == "containerReplica");
        Assert.Contains(scopes, scope =>
            scope.ScopeResourceId == "contract:api:replica:1" &&
            scope.Name == "Replica 1" &&
            scope.Kind == "containerReplica");
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
    public async Task RemoteControlPlane_ReadsProviderProjectedLogSource()
    {
        await using var app = await CreateAppAsync(includeProviderLogSource: true);
        var controlPlane = CreateClient(app);

        var source = await controlPlane.GetLogSourceAsync(ProviderDiagnosticsLogSourceId);
        var sources = await controlPlane.ListLogSourcesAsync(new LogQuery(SourceKind: LogSourceKind.Provider));
        var entries = await controlPlane.ReadLogSourceAsync(ProviderDiagnosticsLogSourceId);
        var streamedEntries = new List<LogEntry>();
        await foreach (var entry in controlPlane.StreamLogSourceAsync(
            ProviderDiagnosticsLogSourceId,
            new StreamLogOptions(InitialEntries: 1)))
        {
            streamedEntries.Add(entry);
        }
        using var httpClient = app.GetTestClient();
        var response = await httpClient.GetAsync(
            $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(ProviderDiagnosticsLogSourceId)}/entries");

        Assert.NotNull(source);
        Assert.Equal(ProviderDiagnosticsLogSourceId, source.Id);
        Assert.Equal(LogSourceKind.Provider, source.SourceKind);
        Assert.True(source.SupportsStreaming);
        Assert.Contains(sources, candidate => candidate.Id == ProviderDiagnosticsLogSourceId);
        Assert.Equal(ProviderDiagnosticsMessage, Assert.Single(entries).Message);
        Assert.Equal(ProviderDiagnosticsMessage, Assert.Single(streamedEntries).Message);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawEntries = await response.Content.ReadFromJsonAsync<IReadOnlyList<LogEntryResponse>>(SerializerOptions);
        Assert.NotNull(rawEntries);
        Assert.Equal(ProviderDiagnosticsMessage, Assert.Single(rawEntries).Message);
    }

    [Fact]
    public async Task RemoteControlPlane_HidesDeniedResourceScopedLogSources()
    {
        await using var app = await CreateAppAsync(
            includeMappedNetwork: true,
            includeProviderLogSource: true,
            authorization: new PermissionAndResourceAuthorizationService(
                [CloudShellPermissions.Observability.Logs.Read],
                ("network:contract", CloudShellPermissions.Resources.Read)));
        var controlPlane = CreateClient(app);
        var apiLogSourceId = $"{ContractNetworkingResourceProvider.ApiResourceId}:log-source:console";

        var allSources = await controlPlane.ListLogSourcesAsync();
        var apiSources = await controlPlane.ListLogSourcesAsync(
            new LogQuery(ResourceId: ContractNetworkingResourceProvider.ApiResourceId));
        var providerSources = await controlPlane.ListLogSourcesAsync(
            new LogQuery(SourceKind: LogSourceKind.Provider));
        var source = await controlPlane.GetLogSourceAsync(apiLogSourceId);
        using var httpClient = app.GetTestClient();
        var sourceResponse = await httpClient.GetAsync(
            $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(apiLogSourceId)}");
        var entriesResponse = await httpClient.GetAsync(
            $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(apiLogSourceId)}/entries");
        var streamResponse = await httpClient.GetAsync(
            $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(apiLogSourceId)}/stream");

        Assert.DoesNotContain(allSources, candidate => candidate.Id == apiLogSourceId);
        Assert.Empty(apiSources);
        Assert.Contains(providerSources, candidate => candidate.Id == ProviderDiagnosticsLogSourceId);
        Assert.Null(source);
        Assert.Equal(HttpStatusCode.NotFound, sourceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, entriesResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, streamResponse.StatusCode);
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
            triggeredBy: "build-server",
            requestedReplicas: 2);
        var eventLogs = await controlPlane.ListLogSourcesAsync(
            new LogQuery(ResourceId: ContractImageResourceProvider.ResourceId));
        var eventLog = Assert.Single(eventLogs, log => log.Name == "Activity");
        var events = await controlPlane.ReadLogSourceAsync(eventLog.Id);
        var resourceEvents = await controlPlane.ListResourceEventsAsync(
            new ResourceEventQuery(
                ResourceId: ContractImageResourceProvider.ResourceId,
                EventType: ResourceEventTypes.Events.Deployment.ImageUpdated,
                TriggeredBy: "user"));

        Assert.Equal("Updated contract:container-app to example/api:20260608.", result.Message);
        var provider = app.Services.GetRequiredService<ContractImageResourceProvider>();
        Assert.Equal(["example/api:20260608:False:user:2"], provider.UpdatedImages);
        var resourceEvent = Assert.Single(resourceEvents);
        Assert.Equal(ContractImageResourceProvider.ResourceId, resourceEvent.ResourceId);
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, resourceEvent.EventType);
        Assert.Equal("user", resourceEvent.TriggeredBy);
        Assert.Equal(ResourceSignalSeverity.Success, resourceEvent.Severity);
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
            HasAttribute(entry, "triggeredBy", "user") &&
            entry.Message.Contains(ResourceEventTypes.Events.Deployment.ImageUpdated, StringComparison.Ordinal) &&
            entry.Message.Contains("user", StringComparison.Ordinal));

        var logResponse = await app.GetTestClient().GetAsync(
            $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(eventLog.Id)}/entries");
        Assert.Equal(HttpStatusCode.OK, logResponse.StatusCode);
        using var logDocument = JsonDocument.Parse(await logResponse.Content.ReadAsStringAsync());
        var logEntry = Assert.Single(
            logDocument.RootElement.EnumerateArray(),
            entry => entry.GetProperty("eventId").GetString() == ResourceEventTypes.Events.Deployment.ImageUpdated);
        Assert.Equal("Success", logEntry.GetProperty("severity").GetString());
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
            "&triggeredBy=user&maxEvents=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resourceEvent = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(ContractImageResourceProvider.ResourceId, resourceEvent.GetProperty("resourceId").GetString());
        Assert.Equal(ResourceEventTypes.Events.Deployment.ImageUpdated, resourceEvent.GetProperty("eventType").GetString());
        Assert.Equal("user", resourceEvent.GetProperty("triggeredBy").GetString());
        Assert.Equal("Success", resourceEvent.GetProperty("severity").GetString());
        Assert.False(resourceEvent.TryGetProperty("level", out _));
        Assert.Contains("example/api:20260609", resourceEvent.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoteControlPlane_ManagesNotifications()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var created = await controlPlane.CreateNotificationAsync(
            new CreateCloudShellNotificationCommand(
                "operator",
                "Resource created",
                "The API resource is ready.",
                ResourceSignalSeverity.Success,
                CloudShellNotificationStatus.Succeeded,
                Source: "Control Plane",
                ResourceId: "application:api",
                EventType: "resource.created",
                EventId: "event-1",
                CorrelationId: "trace-1",
                TemplateKey: "cloudshell.resource-event",
                Actions:
                [
                    new CloudShellNotificationAction(
                        "open",
                        "Open resource",
                        new CloudShellNotificationTarget("/resources/application%3Aapi", "Open"),
                        IsPrimary: true)
                ],
                Attributes: new Dictionary<string, string>
                {
                    ["traceId"] = "trace-1"
                }));

        Assert.NotEmpty(created.Id);
        Assert.Equal("operator", created.RecipientKey);
        Assert.Equal(ResourceSignalSeverity.Success, created.Severity);
        Assert.Equal(CloudShellNotificationStatus.Succeeded, created.Status);

        var listed = Assert.Single(await controlPlane.ListNotificationsAsync(
            new CloudShellNotificationQuery(RecipientKey: "operator", MaxNotifications: 10)));
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("application:api", listed.ResourceId);
        Assert.Equal("cloudshell.resource-event", listed.TemplateKey);
        Assert.Equal("trace-1", listed.Attributes!["traceId"]);
        var action = Assert.Single(listed.Actions!);
        Assert.Equal("open", action.Id);
        Assert.True(action.IsPrimary);
        Assert.Equal("/resources/application%3Aapi", action.Target!.Href);

        await controlPlane.HandleNotificationActionAsync(created.Id, "open");
        await controlPlane.AcknowledgeNotificationAsync(created.Id);
        await controlPlane.DismissNotificationAsync(created.Id);

        Assert.Empty(await controlPlane.ListNotificationsAsync(
            new CloudShellNotificationQuery(RecipientKey: "operator", MaxNotifications: 10)));
        var dismissed = Assert.Single(await controlPlane.ListNotificationsAsync(
            new CloudShellNotificationQuery(
                RecipientKey: "operator",
                IncludeDismissed: true,
                MaxNotifications: 10)));
        Assert.NotNull(dismissed.AcknowledgedAt);
        Assert.NotNull(dismissed.DismissedAt);
    }

    [Fact]
    public async Task RemoteControlPlane_ListsResourceDeployments()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        await SeedDeploymentRecordAsync(app);
        var controlPlane = CreateClient(app);

        var deployments = await controlPlane.ListResourceDeploymentsAsync(
            new ResourceDeploymentQuery(
                SourceResourceId: ContractImageResourceProvider.ResourceId,
                DeploymentId: "contract-container-app-deployment",
                MaxRecords: 10));
        var deployment = Assert.Single(deployments);

        Assert.Equal("contract-container-app-deployment", deployment.DeploymentId);
        Assert.Equal("default", deployment.OrchestratorId);
        Assert.Equal(ContractImageResourceProvider.ResourceId, deployment.SourceResourceId);
        Assert.Equal("contract-container-app", deployment.ServiceId);
        Assert.Equal("revision-2", deployment.RuntimeRevisionId);
        Assert.Equal(ResourceOrchestratorDeploymentStatus.Active, deployment.Status);
        Assert.Equal("build-server", deployment.TriggeredBy);
        Assert.Equal("build-server", deployment.ProvisionedBy);
        Assert.Equal("env-contract-container-app-contract-container-app-1", deployment.EnvironmentRevisionId);
        Assert.Equal(1, deployment.EnvironmentRevisionNumber);
        Assert.Equal(ResourceOrchestratorRevisionStatus.Active, deployment.EnvironmentRevisionStatus);
        Assert.NotNull(deployment.ReplicaGroup);
        Assert.Equal(2, deployment.ReplicaGroup.MaterializedReplicas);
        Assert.NotNull(deployment.Definition);

        var response = await app.GetTestClient().GetAsync(
            "/api/control-plane/v1/deployments" +
            $"?sourceResourceId={Uri.EscapeDataString(ContractImageResourceProvider.ResourceId)}" +
            "&deploymentId=contract-container-app-deployment&maxRecords=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var deploymentResponse = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("contract-container-app-deployment", deploymentResponse.GetProperty("deploymentId").GetString());
        Assert.Equal(
            (int)ResourceOrchestratorDeploymentStatus.Active,
            deploymentResponse.GetProperty("status").GetInt32());
        Assert.Equal("env-contract-container-app-contract-container-app-1", deploymentResponse.GetProperty("environmentRevisionId").GetString());
        Assert.Equal(1, deploymentResponse.GetProperty("environmentRevisionNumber").GetInt32());
    }

    [Fact]
    public async Task RemoteControlPlane_ListsReplicaSlotStates()
    {
        await using var app = await CreateAppAsync(includeImageResource: true);
        var store = app.Services.GetRequiredService<IResourceReplicaGroupReconciliationStore>();
        store.SetRuntimeState(new ResourceReplicaSlotRuntimeState(
            ContractImageResourceProvider.ResourceId,
            2,
            ResourceReplicaSlotRuntimeStatus.RepairFailed,
            "Container exited.",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            ServiceId: "contract-container-app",
            ReplicaGroupId: "contract-container-app-revision-2-replicas",
            RuntimeRevisionId: "revision-2",
            LastAttemptedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
            LastCompletedAt: DateTimeOffset.UtcNow,
            AttemptCount: 1,
            TriggeredBy: "contract-test",
            LastResult: "Replica execution failed."));
        var controlPlane = CreateClient(app);

        var states = await controlPlane.ListReplicaSlotStatesAsync(new ResourceReplicaSlotStateQuery(
            ResourceId: ContractImageResourceProvider.ResourceId,
            SlotOrdinal: 2,
            ReplicaGroupId: "contract-container-app-revision-2-replicas",
            Status: ResourceReplicaSlotReconciliationStatus.RepairFailed,
            MaxRecords: 10));
        var state = Assert.Single(states);

        Assert.Equal(ContractImageResourceProvider.ResourceId, state.ResourceId);
        Assert.Equal(2, state.SlotOrdinal);
        Assert.Equal("contract-container-app", state.ServiceId);
        Assert.Equal("contract-container-app-revision-2-replicas", state.ReplicaGroupId);
        Assert.Equal("revision-2", state.RuntimeRevisionId);
        Assert.Equal(ResourceReplicaSlotReconciliationStatus.RepairFailed, state.Status);
        Assert.Equal("Container exited.", state.Detail);
        Assert.Equal(1, state.AttemptCount);
        Assert.Equal("contract-test", state.TriggeredBy);
        Assert.Equal("Replica execution failed.", state.LastResult);

        var response = await app.GetTestClient().GetAsync(
            "/api/control-plane/v1/replica-slot-states" +
            $"?resourceId={Uri.EscapeDataString(ContractImageResourceProvider.ResourceId)}" +
            "&slotOrdinal=2&replicaGroupId=contract-container-app-revision-2-replicas&status=RepairFailed&maxRecords=10");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var stateResponse = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(ContractImageResourceProvider.ResourceId, stateResponse.GetProperty("resourceId").GetString());
        Assert.Equal(2, stateResponse.GetProperty("slotOrdinal").GetInt32());
        Assert.Equal("contract-container-app", stateResponse.GetProperty("serviceId").GetString());
        Assert.Equal("contract-container-app-revision-2-replicas", stateResponse.GetProperty("replicaGroupId").GetString());
        Assert.Equal("revision-2", stateResponse.GetProperty("runtimeRevisionId").GetString());
        Assert.Equal(
            (int)ResourceReplicaSlotReconciliationStatus.RepairFailed,
            stateResponse.GetProperty("status").GetInt32());
        Assert.Equal(1, stateResponse.GetProperty("attemptCount").GetInt32());
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
        var eventLogs = await controlPlane.ListLogSourcesAsync(
            new LogQuery(ResourceId: ContractImageResourceProvider.ResourceId));
        var eventLog = Assert.Single(eventLogs, log => log.Name == "Activity");
        var events = await controlPlane.ReadLogSourceAsync(eventLog.Id);

        Assert.Equal("Updated contract:container-app to 3 replicas.", result.Message);
        var provider = app.Services.GetRequiredService<ContractImageResourceProvider>();
        Assert.Equal(["3:False:user"], provider.UpdatedReplicas);
        Assert.Contains(events, entry =>
            entry.Source == "event" &&
            entry.Message.Contains(ResourceEventTypes.Events.Deployment.ReplicasUpdated, StringComparison.Ordinal) &&
            entry.Message.Contains("user", StringComparison.Ordinal));
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
        Assert.True(resourceProperties.TryGetProperty("logSources", out _));

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
        Assert.False(paths.TryGetProperty("/api/control-plane/v1/logs", out _));
        var listLogs = paths
            .GetProperty("/api/control-plane/v1/log-sources")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        var logSchemaRef = listLogs.GetProperty("items").GetProperty("$ref").GetString();
        Assert.NotNull(logSchemaRef);
        var logSchemaName = logSchemaRef[(logSchemaRef.LastIndexOf('/') + 1)..];
        Assert.True(schemas.TryGetProperty(logSchemaName, out var logSchema));
        var logProperties = logSchema.GetProperty("properties");
        Assert.True(logProperties.TryGetProperty("kind", out _));
        Assert.True(logProperties.TryGetProperty("format", out _));
        Assert.True(logProperties.TryGetProperty("capabilities", out _));
        Assert.True(logProperties.TryGetProperty("availability", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/log-sources/{logSourceId}", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/log-sources/{logSourceId}/entries", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/log-sources/{logSourceId}/stream", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/replica-slot-states", out _));
        Assert.False(paths.TryGetProperty("/api/control-plane/v1/resources/{resourceId}/image", out _));
        Assert.True(paths.TryGetProperty("/api/container-apps/v1/{containerAppId}/deployments", out _));
        Assert.True(paths.TryGetProperty("/api/container-apps/v1/{containerAppId}/replicas", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-principals", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants/status", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants/revoke", out _));
        Assert.True(paths.TryGetProperty("/api/control-plane/v1/resource-permission-grants/evaluate", out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePermissionGrantResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePermissionGrantStatusResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePrincipalResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(ResourcePrincipalReferenceResponse), out _));
        Assert.True(schemas.TryGetProperty(nameof(GrantResourcePermissionRequest), out _));
        Assert.True(schemas.TryGetProperty(nameof(RevokeResourcePermissionRequest), out _));
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

        var logs = await controlPlane.ListLogSourcesAsync(new LogQuery(ResourceId: "network:contract"));
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
                ["http.method"] = "GET",
                [TelemetryAttributeNames.ScopeResourceId] = "runtime:orders-1",
                [TelemetryAttributeNames.ScopeName] = "Instance 1",
                [TelemetryAttributeNames.ScopeKind] = "containerReplica",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = "1",
                [TelemetryAttributeNames.RuntimeContainerName] = "orders-api-replica-1",
                [TelemetryAttributeNames.DeploymentRevision] = "rev-a"
            });
        var otherSpan = span with
        {
            SpanId = "span-other",
            Attributes = new Dictionary<string, string>
            {
                ["http.method"] = "GET",
                [TelemetryAttributeNames.ScopeResourceId] = "runtime:orders-2",
                [TelemetryAttributeNames.ScopeName] = "Instance 2",
                [TelemetryAttributeNames.ScopeKind] = "containerReplica",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = "2",
                [TelemetryAttributeNames.RuntimeContainerName] = "orders-api-replica-2",
                [TelemetryAttributeNames.DeploymentRevision] = "rev-a"
            }
        };
        await controlPlane.IngestTraceSpansAsync([span, otherSpan]);

        var spans = await controlPlane.ListTraceSpansAsync(
            new TraceQuery(
                ResourceId: "network:contract",
                TraceId: "trace-contract",
                MaxSpans: 10,
                Scope: new TelemetryScope(
                    ScopeResourceId: "runtime:orders-1",
                    ScopeName: "Instance 1",
                    ScopeKind: "containerReplica",
                    DeploymentRevision: "rev-a")));

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
                ["http.method"] = "GET",
                [TelemetryAttributeNames.ScopeResourceId] = "runtime:orders-1",
                [TelemetryAttributeNames.ScopeName] = "Instance 1",
                [TelemetryAttributeNames.ScopeKind] = "containerReplica",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = "1",
                [TelemetryAttributeNames.RuntimeContainerName] = "orders-api-replica-1",
                [TelemetryAttributeNames.DeploymentRevision] = "rev-a"
            });
        var otherMetric = metric with
        {
            Attributes = new Dictionary<string, string>
            {
                ["http.method"] = "GET",
                [TelemetryAttributeNames.ScopeResourceId] = "runtime:orders-2",
                [TelemetryAttributeNames.ScopeName] = "Instance 2",
                [TelemetryAttributeNames.ScopeKind] = "containerReplica",
                [TelemetryAttributeNames.RuntimeReplicaOrdinal] = "2",
                [TelemetryAttributeNames.RuntimeContainerName] = "orders-api-replica-2",
                [TelemetryAttributeNames.DeploymentRevision] = "rev-a"
            }
        };
        await controlPlane.IngestMetricPointsAsync([metric, otherMetric]);

        var points = await controlPlane.ListMetricPointsAsync(
            new MetricQuery(
                ResourceId: "network:contract",
                MetricName: "http.server.requests",
                MaxPoints: 10,
                Scope: new TelemetryScope(
                    ScopeResourceId: "runtime:orders-1",
                    ScopeName: "Instance 1",
                    ScopeKind: "containerReplica",
                    DeploymentRevision: "rev-a")));

        var remotePoint = Assert.Single(points);
        Assert.Equal(metric.Name, remotePoint.Name);
        Assert.Equal(metric.ResourceId, remotePoint.ResourceId);
        Assert.Equal(metric.ServiceName, remotePoint.ServiceName);
        Assert.Equal(metric.Value, remotePoint.Value);
        Assert.Equal(metric.Unit, remotePoint.Unit);
        Assert.Equal("GET", remotePoint.MetricAttributes["http.method"]);

        var usageSample = new UsageSample(
            "resource.cpu.seconds",
            "network:contract",
            12.5,
            DateTimeOffset.UtcNow,
            "seconds",
            new Dictionary<string, string>
            {
                ["usage.scope"] = "contract"
            });
        var otherUsageSample = usageSample with
        {
            ResourceId = "network:other",
            Name = "resource.memory.bytes",
            Value = 256,
            Unit = "bytes",
            Attributes = new Dictionary<string, string>
            {
                ["usage.scope"] = "other"
            }
        };
        await controlPlane.RecordUsageSamplesAsync([usageSample, otherUsageSample]);

        var usageSamples = await controlPlane.ListUsageSamplesAsync(
            new UsageQuery(
                ResourceId: "network:contract",
                UsageName: "resource.cpu.seconds",
                MaxSamples: 10));
        var remoteUsageSample = Assert.Single(usageSamples);
        Assert.Equal(usageSample.Name, remoteUsageSample.Name);
        Assert.Equal(usageSample.ResourceId, remoteUsageSample.ResourceId);
        Assert.Equal(usageSample.Value, remoteUsageSample.Value);
        Assert.Equal(usageSample.Unit, remoteUsageSample.Unit);
        Assert.Equal("contract", remoteUsageSample.UsageAttributes["usage.scope"]);

        var usageStatistics = await controlPlane.ListUsageStatisticsAsync(
            new UsageStatisticsQuery(
                ResourceId: "network:contract",
                UsageName: "resource.cpu.seconds"));
        var usageStatistic = Assert.Single(usageStatistics);
        Assert.Equal(usageSample.Name, usageStatistic.Name);
        Assert.Equal(usageSample.ResourceId, usageStatistic.ResourceId);
        Assert.Equal(1, usageStatistic.Count);
        Assert.Equal(usageSample.Value, usageStatistic.Sum);
        Assert.Equal(usageSample.Value, usageStatistic.LatestValue);
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
    public async Task RemoteControlPlane_AppliesAndExportsResourceTemplates()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var applied = await controlPlane.ApplyResourceTemplateAsync(
            new ResourceDefinitionTemplate("Contract template", []));
        var appliedRequest = await controlPlane.ApplyResourceTemplateAsync(
            new ResourceTemplateApplyRequest(
                new ResourceDefinitionTemplate("Contract template request", []),
                ResourceDefinitionApplyMode.CreateOnly));

        Assert.True(applied.IsCommitted);
        Assert.Empty(applied.Diagnostics);
        Assert.True(appliedRequest.IsCommitted);
        Assert.Empty(appliedRequest.Diagnostics);

        var exported = await controlPlane.ExportResourceTemplateAsync(
            new ResourceTemplateExportRequest("Contract template export"));

        Assert.Equal("Contract template export", exported.Template.Name);
        Assert.Empty(exported.Template.Resources);
        Assert.Empty(exported.Diagnostics);
    }

    [Fact]
    public async Task RemoteControlPlane_UploadsDeploymentArtifactToConfiguredHostStore()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        const string resourceId = "application.dotnet-web-app:api";

        var status = await controlPlane.GetDeploymentArtifactStoreStatusAsync(resourceId);
        var layouts = await controlPlane.ListDeploymentArtifactLayoutsAsync(
            resourceId,
            new DeploymentArtifactLayoutQuery("application.dotnet-web-app"));

        Assert.True(status.IsEnabled);
        Assert.Equal(["tar.gz", "zip"], status.AllowedPackageKinds);
        var layout = Assert.Single(layouts);
        Assert.Equal("application.dotnet-web-app", layout.ResourceTypeId.ToString());
        Assert.Equal("dotnetPublishedOutput", layout.Kind);
        Assert.Equal(["zip"], layout.PackageKinds);

        var package = "compiled output package"u8.ToArray();
        var upload = await controlPlane.CreateDeploymentArtifactUploadSessionAsync(
            new CreateDeploymentArtifactUploadSessionCommand(
                "application.dotnet-web-app",
                "api",
                "zip",
                FileName: "api.zip",
                ContentLength: package.Length,
                ArtifactLayoutKind: layout.Kind,
                ResourceId: resourceId));

        await controlPlane.UploadDeploymentArtifactContentAsync(
            resourceId,
            upload.UploadId,
            new MemoryStream(package));
        var revision = await controlPlane.CompleteDeploymentArtifactUploadAsync(resourceId, new(upload.UploadId));
        var loaded = await controlPlane.GetDeploymentArtifactRevisionAsync(
            resourceId,
            revision.ArtifactId,
            revision.RevisionId);
        var revisions = await controlPlane.ListDeploymentArtifactRevisionsAsync(
            resourceId,
            revision.ArtifactId);
        var validation = await controlPlane.ValidateDeploymentArtifactAsync(
            resourceId,
            new ValidateDeploymentArtifactCommand(
                "application.dotnet-web-app",
                "api",
                revision.ArtifactId,
                revision.RevisionId,
                EntryPath: ".",
                ArtifactLayoutKind: layout.Kind));

        Assert.Equal("deployment-artifact:application.dotnet-web-app:api", revision.ArtifactId);
        Assert.Equal("zip", revision.PackageKind);
        Assert.Equal("dotnetPublishedOutput", revision.ArtifactLayoutKind);
        Assert.Equal(DeploymentArtifactSourceKinds.UploadedArtifact, revision.SourceKind);
        Assert.Null(revision.SourceVersion);
        Assert.False(revision.CanRehydrate);
        Assert.Equal(package.Length, revision.SizeBytes);
        Assert.False(string.IsNullOrWhiteSpace(revision.ContentSha256));
        Assert.Equal(revision, loaded);
        Assert.Equal(revision, Assert.Single(revisions));
        Assert.Empty(validation.Diagnostics);
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
        bool includeRuntimeResource = false,
        bool includeProviderLogSource = false,
        ICloudShellAuthorizationService? authorization = null)
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
            ["Identity:BuiltIn:Persistence:Provider"] = "Sqlite",
            ["Identity:BuiltIn:Persistence:ConnectionString"] = "Data Source=Data/identity-client-contract.db",
            ["DeploymentArtifacts:Enabled"] = "true",
            ["DeploymentArtifacts:Store:Kind"] = "FileSystem",
            ["DeploymentArtifacts:Store:RootPath"] = "Data/deployment-artifacts"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        if (authorization is not null)
        {
            builder.Services.Replace(ServiceDescriptor.Singleton(authorization));
        }

        builder.Services.AddSingleton<ContractIdentityDirectoryProvider>();
        builder.Services.AddSingleton<IResourceIdentityDirectoryProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<ContractIdentityDirectoryProvider>());
        builder.Services.AddSingleton<ContractIdentityProviderSetupHandler>();
        builder.Services.AddSingleton<IResourceIdentityProviderSetupHandler>(serviceProvider =>
            serviceProvider.GetRequiredService<ContractIdentityProviderSetupHandler>());
        builder.Services.AddSingleton<IDeploymentArtifactLayoutProvider, ContractDeploymentArtifactLayoutProvider>();
        builder.Services.AddSingleton<IDeploymentArtifactValidationProvider, ContractDeploymentArtifactValidationProvider>();
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
        if (includeProviderLogSource)
        {
            builder.Services.AddSingleton<ILogProvider, ContractProviderLogProvider>();
        }

        controlPlane.AddIdentityProvider(
            "identity:contract",
            "Contract identity",
            ResourceIdentityProviderKind.Custom);

        controlPlane.DefineResources(resources =>
        {
            var declarations = resources.Declarations;
            var network = declarations
                .AddNetwork("network:contract", isDefault: true)
                .WithDisplayName("Contract Network")
                .Persist()
                .WithIdentity("development", name: "network-service");
            network.Allow(
                network.Principal,
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
                declarations
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

    private static async Task SeedDeploymentRecordAsync(WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IResourceOrchestratorDeploymentStore>();
        var service = new ResourceOrchestratorService(
            ContractImageResourceProvider.ResourceId,
            "contract-container-app",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "Contract Container App",
                Image: "example/api:20260623",
                Replicas: 2,
                ReplicasEnabled: true));
        var deployment = new ResourceOrchestratorDeployment(
            "contract-container-app-deployment",
            "default",
            ContractImageResourceProvider.ResourceId,
            service.Name,
            "revision-2",
            new ResourceOrchestratorDeploymentSpec(service, "revision-2"),
            ResourceOrchestratorDeploymentStatus.Applying);
        store.RecordApplying(
            deployment,
            DateTimeOffset.UtcNow.AddSeconds(-1),
            "build-server",
            "image update");
        var revision = store.CreateRevision(
            deployment,
            DateTimeOffset.UtcNow,
            ResourceOrchestratorRevisionStatus.Active,
            ResourceOrchestratorReplicaGroups.CreateRevisionReplicaGroup(service, "revision-2"),
            "build-server");
        store.RecordApplied(
            deployment with { Status = ResourceOrchestratorDeploymentStatus.Active },
            revision,
            DateTimeOffset.UtcNow,
            "Applied deployment.",
            "build-server",
            "image update");
    }

    private sealed class ContractProviderLogProvider : ILogProvider
    {
        public string Id => "contract.provider-logs";

        public string DisplayName => "Contract Provider Logs";

        public IReadOnlyList<LogSource> GetLogSources() =>
        [
            new(
                ProviderDiagnosticsLogSourceId,
                "Provider diagnostics",
                DisplayName,
                "Contract provider",
                LogSourceKind.Provider,
                ResourceLogSourceKind.ProviderDefined,
                Capabilities: LogSourceCapabilities.Read | LogSourceCapabilities.Stream,
                Origin: ResourceLogSourceOrigin.ProviderProjected,
                Availability: LogSourceAvailability.Always)
        ];

        public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
            string logSourceId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ILogSourceSession?>(
                string.Equals(logSourceId, ProviderDiagnosticsLogSourceId, StringComparison.OrdinalIgnoreCase)
                    ? new ContractProviderLogSourceSession()
                    : null);

        public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    private sealed class ContractProviderLogSourceSession : ILogSourceSession
    {
        private static readonly LogEntry Entry = new(
            DateTimeOffset.Parse("2026-06-21T12:00:00Z"),
            ProviderDiagnosticsMessage,
            "Information",
            "Contract provider");

        public string Id { get; } = Guid.NewGuid().ToString("N");

        public string SourceId => ProviderDiagnosticsLogSourceId;

        public LogSourceSessionStatus Status { get; private set; } = LogSourceSessionStatus.Active;

        public Task<IReadOnlyList<LogEntry>> ReadAsync(
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([Entry]);

        public async IAsyncEnumerable<LogEntry> StreamAsync(
            int initialEntries = 50,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (initialEntries <= 0)
            {
                yield break;
            }

            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return Entry;
        }

        public ValueTask DisposeAsync()
        {
            Status = LogSourceSessionStatus.Closed;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PermissionAndResourceAuthorizationService(
        IReadOnlyList<string> permissions,
        params (string ResourceId, string Permission)[] grants) : ICloudShellAuthorizationService
    {
        private readonly HashSet<string> permissions = new(permissions, StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> grants = new(
            grants.Select(grant => CreateKey(grant.ResourceId, grant.Permission)),
            StringComparer.OrdinalIgnoreCase);

        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => permissions.Contains(permission);

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) =>
            HasPermission(permission);

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            grants.Contains(CreateKey(resourceId, permission));

        private static string CreateKey(string resourceId, string permission) =>
            $"{resourceId}\n{permission}";
    }

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

    private sealed class ContractIdentityDirectoryProvider : IResourceIdentityDirectoryProvider
    {
        public List<ResourceIdentityDirectoryRequest> Requests { get; } = [];

        public string ProviderId => "identity:contract";

        public bool CanQueryDirectory(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityDirectoryResult> QueryDirectoryAsync(
            ResourceIdentityDirectoryRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var principals = new[]
            {
                new ResourcePrincipal(
                    new ResourcePrincipalReference(
                        ResourcePrincipalKind.User,
                        "platform-user",
                        "Platform User",
                        ProviderId),
                    "Platform User",
                    "Directory user",
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["mail"] = "platform@example.local"
                    }),
                new ResourcePrincipal(
                    new ResourcePrincipalReference(
                        ResourcePrincipalKind.Group,
                        "platform-operators",
                        "Platform Operators",
                        ProviderId),
                    "Platform Operators",
                    "Directory group")
            };

            return Task.FromResult(new ResourceIdentityDirectoryResult(
                ProviderId,
                principals
                    .Where(principal =>
                        request.Query.PrincipalKinds.Count == 0 ||
                        request.Query.PrincipalKinds.Contains(principal.Reference.Kind))
                    .Where(principal =>
                        string.IsNullOrWhiteSpace(request.Query.SearchText) ||
                        principal.DisplayName.Contains(request.Query.SearchText, StringComparison.OrdinalIgnoreCase) ||
                        principal.Reference.Id.Contains(request.Query.SearchText, StringComparison.OrdinalIgnoreCase))
                    .ToArray()));
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
                Observability: new ResourceObservability(
                    Logs: true,
                    Traces: true,
                    Metrics: true,
                    ServiceName: "contract-api",
                    Sources:
                    [
                        new TelemetrySourceDescriptor(
                            "contract-api-otlp",
                            "Contract API OTLP",
                            TelemetrySignalKind.Traces | TelemetrySignalKind.Metrics,
                            TelemetrySourceKind.Exporter,
                            Endpoint: "http://localhost:4317",
                            Protocol: "otlp",
                            Scopes:
                            [
                                new TelemetryScopeDescriptor(
                                    "contract:api:replica:0",
                                    "Replica 0",
                                    "containerReplica"),
                                new TelemetryScopeDescriptor(
                                    "contract:api:replica:1",
                                    "Replica 1",
                                    "containerReplica")
                            ])
                    ]),
                Capabilities: [new(ResourceCapabilityIds.EndpointSource)],
                LogSources:
                [
                    new ResourceLogSource(
                        "console",
                        "Console logs",
                        ResourceLogSourceKind.ProcessOutput,
                        Format: LogFormat.JsonConsole,
                        Capabilities: LogSourceCapabilities.Read |
                            LogSourceCapabilities.Stream |
                            LogSourceCapabilities.StructuredFields,
                        Origin: ResourceLogSourceOrigin.ProviderDefault,
                        Purpose: ResourceLogSourcePurpose.Default,
                        Availability: LogSourceAvailability.ResourceRunning)
                ]),
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

    private sealed class ContractDeploymentArtifactLayoutProvider : IDeploymentArtifactLayoutProvider
    {
        public ResourceTypeId TypeId => "application.dotnet-web-app";

        public ValueTask<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> GetDeploymentArtifactLayoutsAsync(
            DeploymentArtifactLayoutQuery query,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<DeploymentArtifactLayoutDescriptor>>(
            [
                new(
                    TypeId,
                    "dotnetPublishedOutput",
                    ".NET published output",
                    "A compiled .NET app package that is ready to run on the host.",
                    ["zip"],
                    DefaultPackageKind: "zip",
                    DefaultEntryPath: ".",
                    IsDefault: true)
            ]);
    }

    private sealed class ContractDeploymentArtifactValidationProvider : IDeploymentArtifactValidationProvider
    {
        public string Id => "contract.deployment-artifacts.validation";

        public bool CanValidate(DeploymentArtifactValidationContext context) =>
            string.Equals(context.ResourceType, "application.dotnet-web-app", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(context.ArtifactLayoutKind, "dotnetPublishedOutput", StringComparison.OrdinalIgnoreCase);

        public async ValueTask<ResourceDefinitionValidationResult> ValidateDeploymentArtifactAsync(
            DeploymentArtifactValidationContext context,
            Stream artifactContent,
            CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(artifactContent);
            var content = await reader.ReadToEndAsync(cancellationToken);
            if (content.Contains("compiled output", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceDefinitionValidationResult.Success;
            }

            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "contract.deploymentArtifact.invalidPackage",
                    "The uploaded package did not contain compiled output.",
                    context.ArtifactId)
            ]);
        }
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
            CancellationToken cancellationToken = default,
            int? requestedReplicas = null)
        {
            UpdatedImages.Add($"{image}:{restartIfRunning}:{triggeredBy}:{requestedReplicas?.ToString(CultureInfo.InvariantCulture) ?? "unchanged"}");
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
