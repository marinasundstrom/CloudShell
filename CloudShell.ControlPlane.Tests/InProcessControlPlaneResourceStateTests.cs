using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;

namespace CloudShell.ControlPlane.Tests;

public sealed class InProcessControlPlaneResourceStateTests
{
    public static TheoryData<ResourceState, string[]> StateResourceActionCapabilities =>
        new()
        {
            { ResourceState.Running, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Starting, [ResourceActionIds.Stop, ResourceActionIds.Restart] },
            { ResourceState.Stopping, [ResourceActionIds.Stop] },
            { ResourceState.Paused, [ResourceActionIds.Start, ResourceActionIds.Stop] },
            { ResourceState.Degraded, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Stopped, [ResourceActionIds.Start] },
            { ResourceState.Unknown, [ResourceActionIds.Start] }
        };

    [Theory]
    [MemberData(nameof(StateResourceActionCapabilities))]
    public async Task GetResourceOperationCapabilities_ReturnsStateSpecificResourceActionCapabilities(
        ResourceState state,
        string[] expectedExecutableActionIds)
    {
        var resource = CreateResource("target", state);
        var controlPlane = CreateControlPlane([resource]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.True(capability.CanManage);
        Assert.True(capability.CanDelete);
        Assert.Equal(
            expectedExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase),
            capability.ExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            resource.ResourceActions.Select(action => action.Id).Order(StringComparer.OrdinalIgnoreCase),
            capability.ResourceActionCapabilities.Select(action => action.ActionId).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(
            capability.ResourceActionCapabilities.Where(action => !action.CanExecute),
            action => Assert.False(string.IsNullOrWhiteSpace(action.Reason)));
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_UsesActionSpecificPermissions()
    {
        var resource = CreateResource(
            "target",
            ResourceState.Running,
            actions:
            [
                ResourceAction.Start,
                ResourceAction.Stop,
                ResourceAction.Pause,
                ResourceAction.Restart,
                new ResourceAction("custom", "Custom")
            ]);
        var controlPlane = CreateControlPlane(
            [resource],
            authorization: new PermissionAuthorizationService(
                CloudShellPermissions.Resources.Actions.Lifecycle));

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanManage);
        Assert.False(capability.CanDelete);
        Assert.Equal(
            [
                ResourceActionIds.Pause,
                ResourceActionIds.Restart,
                ResourceActionIds.Stop
            ],
            capability.ExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(capability.CanExecuteAction(ResourceActionIds.Stop));
        Assert.True(capability.CanExecuteAction(ResourceActionIds.Pause));
        Assert.False(capability.CanExecuteAction("custom"));
        Assert.Equal(
            "The 'CloudShell.Resources/resources/actions/execute/action' or 'resources.manage' permission is required for resource 'target'.",
            capability.GetActionUnavailableReason("custom"));
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_UsesUserResourcePermissionClaims()
    {
        var resource = CreateResource("target", ResourceState.Running);
        var controlPlane = CreateControlPlane(
            [resource],
            authorization: CreateClaimsAuthorization(
                CreateResourcePermissionClaim(
                    resource.Id,
                    CloudShellPermissions.Resources.Actions.Lifecycle)));

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanManage);
        Assert.False(capability.CanDelete);
        Assert.Equal(
            [
                ResourceActionIds.Pause,
                ResourceActionIds.Restart,
                ResourceActionIds.Stop
            ],
            capability.ExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.True(capability.CanExecuteAction(ResourceActionIds.Stop));
        Assert.True(capability.CanExecuteAction(ResourceActionIds.Pause));
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Start));
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_ReturnsMissingContainerHostReason()
    {
        var resource = CreateResource("target", ResourceState.Stopped);
        var controlPlane = CreateControlPlane(
            [resource],
            descriptorProviders:
            [
                new StaticWorkloadDescriptorProvider(
                    resource.Id,
                    new ResourceWorkloadConfiguration(
                        ResourceWorkloadKind.ContainerImage,
                        "api",
                        Image: "example/api:latest",
                        ContainerHostId: "docker:missing"))
            ]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Start));
        Assert.Equal(
            "Container host 'docker:missing' is not registered.",
            capability.GetActionUnavailableReason(ResourceActionIds.Start));
        Assert.DoesNotContain(ResourceActionIds.Start, capability.ExecutableActionIds);
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_ReturnsMissingContainerHostCapabilityReason()
    {
        var resource = CreateResource("target", ResourceState.Stopped);
        var hostResource = CreateResource("docker:local", ResourceState.Running);
        var controlPlane = CreateControlPlane(
            [resource, hostResource],
            descriptorProviders:
            [
                new StaticWorkloadDescriptorProvider(
                    resource.Id,
                    new ResourceWorkloadConfiguration(
                        ResourceWorkloadKind.ContainerBuild,
                        "api",
                        BuildContext: ".",
                        ContainerHostId: hostResource.Id)),
                new StaticContainerHostDescriptorProvider(
                    hostResource.Id,
                    new ContainerHostDescriptor(
                        hostResource.Id,
                        "Local Docker",
                        ContainerHostKind.Docker,
                        "unix:///var/run/docker.sock",
                        Capabilities: [ContainerHostCapabilityIds.ContainerImage]))
            ]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Start));
        Assert.Equal(
            "Container host 'docker:local' does not advertise required capability 'container.build'.",
            capability.GetActionUnavailableReason(ResourceActionIds.Start));
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_ReturnsProviderUnavailableReason()
    {
        var resource = CreateResource("target", ResourceState.Stopped);
        var provider = new TestResourceProvider();
        var availability = new TestActionAvailabilityProvider(
            resource.Id,
            ResourceActionIds.Start,
            "Reference target missing.");
        var controlPlane = CreateControlPlane(
            [resource],
            provider,
            actionAvailabilityProviders: [availability]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Start));
        Assert.Equal(availability.Reason, capability.GetActionUnavailableReason(ResourceActionIds.Start));
        Assert.DoesNotContain(ResourceActionIds.Start, capability.ExecutableActionIds);
        Assert.Empty(provider.ExecutedActions);
    }

    [Theory]
    [InlineData(ResourceState.Running, ResourceActionIds.Start)]
    [InlineData(ResourceState.Stopping, ResourceActionIds.Start)]
    [InlineData(ResourceState.Stopping, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Stopped, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Paused, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Unknown, ResourceActionIds.Pause)]
    public async Task ExecuteResourceActionAsync_RejectsStateInvalidActions(
        ResourceState state,
        string actionId)
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", state)], provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", actionId)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnavailable, exception.Error.Code);
        Assert.Contains("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsMissingContainerHostBeforeDispatch()
    {
        var provider = new TestResourceProvider();
        var resource = CreateResource("target", ResourceState.Stopped);
        var controlPlane = CreateControlPlane(
            [resource],
            provider,
            descriptorProviders:
            [
                new StaticWorkloadDescriptorProvider(
                    resource.Id,
                    new ResourceWorkloadConfiguration(
                        ResourceWorkloadKind.ContainerImage,
                        "api",
                        Image: "example/api:latest",
                        ContainerHostId: "docker:missing"))
            ]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Start)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnavailable, exception.Error.Code);
        Assert.Equal("Container host 'docker:missing' is not registered.", exception.Message);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsProviderUnavailableReasonBeforeDispatch()
    {
        var provider = new TestResourceProvider();
        var resource = CreateResource("target", ResourceState.Stopped);
        var availability = new TestActionAvailabilityProvider(
            resource.Id,
            ResourceActionIds.Start,
            "Reference target missing.");
        var controlPlane = CreateControlPlane(
            [resource],
            provider,
            actionAvailabilityProviders: [availability]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Start)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnavailable, exception.Error.Code);
        Assert.Equal(availability.Reason, exception.Message);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("missing", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnknownAction()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", "missing")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionNotFound, exception.Error.Code);
        Assert.Equal("Resource 'target' does not expose action 'missing'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnsupportedProviderActions()
    {
        var provider = new TestReadOnlyResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support actions.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            "The 'CloudShell.Resources/resources/lifecycle/action' or 'resources.manage' permission is required for resource 'target'.",
            exception.Message);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_LogsDeniedActionEvent()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService(),
            resourceEvents: resourceEvents);

        await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop,
                TriggeredBy: "operator")));

        Assert.Empty(provider.ExecutedActions);
        var resourceEvent = Assert.Single(resourceEvents.GetEvents(new ResourceEventQuery(
            ResourceId: "target",
            EventType: ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Stop))));
        Assert.Equal("operator", resourceEvent.TriggeredBy);
        Assert.Equal(ResourceSignalSeverity.Warning, resourceEvent.Severity);
        Assert.Equal(
            "Stop action was denied. The 'CloudShell.Resources/resources/lifecycle/action' or 'resources.manage' permission is required for resource 'target'.",
            resourceEvent.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_AllowsActionSpecificPermission()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new PermissionAuthorizationService(
                CloudShellPermissions.Resources.Actions.Lifecycle));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_AllowsUserResourcePermissionClaim()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: CreateClaimsAuthorization(
                CreateResourcePermissionClaim(
                    "target",
                    CloudShellPermissions.Resources.Actions.Lifecycle)));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_KeepsManagePermissionAsActionSuperset()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new PermissionAuthorizationService(
                CloudShellPermissions.Resources.Manage));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_UsesCustomActionPermissionWhenDeclared()
    {
        const string actionId = "apply";
        const string permission = "CloudShell.Network/loadBalancers/apply/action";
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateResource(
                    "target",
                    ResourceState.Running,
                    actions:
                    [
                        new ResourceAction(
                            actionId,
                            "Apply",
                            RequiredPermission: permission)
                    ])
            ],
            provider,
            authorization: new PermissionAuthorizationService(permission));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand("target", actionId));

        Assert.Equal(["target:apply"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_AllowsActingResourceIdentityWithGrant()
    {
        var provider = new TestResourceProvider();
        var identity = ResourceIdentityReference.ForResource("caller", "caller-service");
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService(),
            permissionGrants:
            [
                new ResourcePermissionGrant(
                    identity.ToPrincipal(),
                    "target",
                    CloudShellPermissions.Resources.Actions.Lifecycle)
            ]);

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop,
                ActingIdentity: identity));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_DoesNotFallBackToUserPermissionForActingResourceIdentity()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    "target",
                    ResourceActionIds.Stop,
                    ActingIdentity: ResourceIdentityReference.ForResource("caller", "caller-service"))));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task GrantResourcePermissionAsync_AddsDeclaredGrant()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateIdentityResource("caller", "caller-service"),
                CreateResource("target", ResourceState.Running)
            ]);

        await controlPlane.GrantResourcePermissionAsync(
            new GrantResourcePermissionCommand(
                ResourcePrincipalReference.ForResourceIdentity("caller", "caller-service"),
                "target",
                CloudShellPermissions.Resources.Actions.Lifecycle));

        var grants = await controlPlane.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "target"));
        var grant = Assert.Single(grants);
        Assert.Equal("caller", grant.Principal.SourceResourceId);
        Assert.Equal("caller-service", grant.Principal.SourceIdentityName);
        Assert.Equal(CloudShellPermissions.Resources.Actions.Lifecycle, grant.Permission);

        var evaluation = await controlPlane.EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference.ForResource("caller", "caller-service"),
            "target",
            CloudShellPermissions.Resources.Actions.Lifecycle);
        Assert.True(evaluation.IsAllowed);
    }

    [Fact]
    public async Task ListResourcePermissionGrantStatusesAsync_UsesProviderStatus()
    {
        var grant = new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("caller", "caller-service"),
            "target",
            CloudShellPermissions.Resources.Actions.Lifecycle);
        var controlPlane = CreateControlPlane(
            [
                CreateIdentityResource("caller", "caller-service"),
                CreateResource("target", ResourceState.Running)
            ],
            permissionGrants: [grant],
            permissionGrantStatusProviders:
            [
                new TestPermissionGrantStatusProvider(
                    "test.status",
                    ResourcePermissionGrantEffectivenessState.Applied,
                    "Provider applied the grant.")
            ]);

        var statuses = await controlPlane.ListResourcePermissionGrantStatusesAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "target"));

        var status = Assert.Single(statuses);
        Assert.Equal(grant, status.Grant);
        Assert.Equal(ResourcePermissionGrantEffectivenessState.Applied, status.State);
        Assert.Equal("Provider applied the grant.", status.Detail);
        Assert.Equal("test.status", status.ProviderId);
    }

    [Fact]
    public async Task GrantResourcePermissionAsync_AddsUserPrincipalGrant()
    {
        var principal = new ResourcePrincipalReference(
            ResourcePrincipalKind.User,
            "alice",
            "Alice Local Developer",
            "identity:dev");
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)]);

        await controlPlane.GrantResourcePermissionAsync(
            new GrantResourcePermissionCommand(
                principal,
                "target",
                CloudShellPermissions.Resources.Actions.Lifecycle));

        var grants = await controlPlane.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(Principal: principal));
        var grant = Assert.Single(grants);
        Assert.Equal(ResourcePrincipalKind.User, grant.Principal.Kind);
        Assert.Equal("alice", grant.Principal.Id);
        Assert.Equal("Alice Local Developer", grant.Principal.DisplayName);
        Assert.Equal("identity:dev", grant.Principal.ProviderId);
        Assert.Equal("target", grant.TargetResourceId);
        Assert.Equal(CloudShellPermissions.Resources.Actions.Lifecycle, grant.Permission);
        Assert.Null(grant.ResourceIdentity);
    }

    [Fact]
    public async Task RevokeResourcePermissionAsync_RemovesDeclaredGrant()
    {
        var identity = ResourceIdentityReference.ForResource("caller", "caller-service");
        var controlPlane = CreateControlPlane(
            [
                CreateIdentityResource("caller", "caller-service"),
                CreateResource("target", ResourceState.Running)
            ],
            permissionGrants:
            [
                new ResourcePermissionGrant(
                    identity.ToPrincipal(),
                    "target",
                    CloudShellPermissions.Resources.Actions.Lifecycle)
            ]);

        await controlPlane.RevokeResourcePermissionAsync(
            new RevokeResourcePermissionCommand(
                ResourcePrincipalReference.ForResourceIdentity("caller", "caller-service"),
                "target",
                CloudShellPermissions.Resources.Actions.Lifecycle));

        Assert.Empty(await controlPlane.ListResourcePermissionGrantsAsync(
            new ResourcePermissionGrantQuery(TargetResourceId: "target")));
        var evaluation = await controlPlane.EvaluateResourcePermissionGrantAsync(
            identity,
            "target",
            CloudShellPermissions.Resources.Actions.Lifecycle);
        Assert.False(evaluation.IsAllowed);
    }

    [Fact]
    public async Task QueryResourcePrincipalsAsync_ReturnsResourceIdentitiesAndDirectoryPrincipals()
    {
        var directoryProvider = new TestResourceIdentityDirectoryProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateIdentityResource("caller", "caller-service"),
                CreateResource("target", ResourceState.Running)
            ],
            identityProviders:
            [
                new ResourceIdentityProviderDefinition(
                    "identity:dev",
                    "Development Identity",
                    ResourceIdentityProviderKind.BuiltIn)
            ],
            identityDirectoryProviders: [directoryProvider]);

        var principals = await controlPlane.QueryResourcePrincipalsAsync(
            new ResourcePrincipalQuery(SearchText: "platform", Limit: 10));

        var user = Assert.Single(principals, principal =>
            principal.Reference.Kind == ResourcePrincipalKind.User);
        Assert.Equal("identity:dev", user.Reference.ProviderId);
        Assert.Equal("platform-user", user.Reference.Id);
        Assert.Equal("Platform User", user.DisplayName);
        Assert.Equal("identity:dev", directoryProvider.Requests.Single().Provider.Id);
        Assert.Equal("platform", directoryProvider.Requests.Single().Query.SearchText);
    }

    [Fact]
    public async Task QueryResourcePrincipalsAsync_CanLimitToResourceIdentityPrincipals()
    {
        var directoryProvider = new TestResourceIdentityDirectoryProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateIdentityResource("caller", "caller-service"),
                CreateIdentityResource("target", "target-service")
            ],
            identityProviders:
            [
                new ResourceIdentityProviderDefinition(
                    "identity:dev",
                    "Development Identity",
                    ResourceIdentityProviderKind.BuiltIn)
            ],
            identityDirectoryProviders: [directoryProvider]);

        var principals = await controlPlane.QueryResourcePrincipalsAsync(
            new ResourcePrincipalQuery(
                Kinds: new HashSet<ResourcePrincipalKind>
                {
                    ResourcePrincipalKind.ResourceIdentity
                }));

        Assert.Equal(2, principals.Count);
        Assert.All(principals, principal =>
            Assert.Equal(ResourcePrincipalKind.ResourceIdentity, principal.Reference.Kind));
        Assert.Empty(directoryProvider.Requests);
    }

    [Fact]
    public async Task GrantResourcePermissionAsync_RequiresIdentityBinding()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("caller", ResourceState.Running),
                CreateResource("target", ResourceState.Running)
            ]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.GrantResourcePermissionAsync(
                new GrantResourcePermissionCommand(
                    ResourcePrincipalReference.ForResourceIdentity("caller"),
                    "target",
                    CloudShellPermissions.Resources.Actions.Lifecycle)));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Contains("does not have an identity binding", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProvisionResourceIdentityAsync_RequiresPermissionOnProvisioningResource()
    {
        var provisioner = new TestResourceIdentityProvisioner("identity:dev");
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: new ResourceScopedAuthorizationService(
                ("api", CloudShellPermissions.Resources.Manage)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            identityProvisioners: [provisioner],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ProvisionResourceIdentityAsync("api"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            "The 'CloudShell.Identity/provisioningServices/identities/provision/action' or 'resources.manage' permission is required for resource 'identity:dev'.",
            exception.Message);
        Assert.Empty(provisioner.Requests);
    }

    [Fact]
    public async Task ProvisionResourceIdentityAsync_AllowsProvisioningResourcePermission()
    {
        var provisioner = new TestResourceIdentityProvisioner("identity:dev");
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: new ResourceScopedAuthorizationService(
                ("api", CloudShellPermissions.Resources.Manage),
                ("identity:dev", ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            identityProvisioners: [provisioner],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var result = await controlPlane.ProvisionResourceIdentityAsync("api");

        Assert.Equal("identity:dev", result.ProviderId);
        var request = Assert.Single(provisioner.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        Assert.Equal("api", Assert.Single(request.Identities).Identity.ResourceId);
    }

    [Fact]
    public async Task ProvisionResourceIdentityAsync_AllowsUserResourcePermissionClaims()
    {
        var provisioner = new TestResourceIdentityProvisioner("identity:dev");
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: CreateClaimsAuthorization(
                CreateResourcePermissionClaim(
                    "api",
                    CloudShellPermissions.Resources.Manage),
                CreateResourcePermissionClaim(
                    "identity:dev",
                    ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            identityProvisioners: [provisioner],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var result = await controlPlane.ProvisionResourceIdentityAsync("api");

        Assert.Equal("identity:dev", result.ProviderId);
        var request = Assert.Single(provisioner.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
        Assert.Equal("api", Assert.Single(request.Identities).Identity.ResourceId);
    }

    [Fact]
    public async Task SetupResourceIdentityProviderAsync_RequiresPermissionOnProvisioningResource()
    {
        var setupHandler = new TestResourceIdentityProviderSetupHandler("identity:dev");
        var controlPlane = CreateControlPlane(
            [CreateResource("identity:dev", ResourceState.Running)],
            authorization: new ResourceScopedAuthorizationService(
                ("identity:dev", CloudShellPermissions.Resources.Read)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            identityProviderSetupHandlers: [setupHandler]);

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.SetupResourceIdentityProviderAsync("identity:dev"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            "The 'CloudShell.Identity/provisioningServices/identities/provision/action' or 'resources.manage' permission is required for resource 'identity:dev'.",
            exception.Message);
        Assert.Empty(setupHandler.Requests);
    }

    [Fact]
    public async Task SetupResourceIdentityProviderAsync_AllowsProvisioningResourcePermission()
    {
        var setupHandler = new TestResourceIdentityProviderSetupHandler("identity:dev");
        var controlPlane = CreateControlPlane(
            [CreateResource("identity:dev", ResourceState.Running)],
            authorization: new ResourceScopedAuthorizationService(
                ("identity:dev", ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            identityProviderSetupHandlers: [setupHandler]);

        var result = await controlPlane.SetupResourceIdentityProviderAsync("identity:dev");

        Assert.Equal("identity:dev", result.ProviderId);
        var request = Assert.Single(setupHandler.Requests);
        Assert.Equal("identity:dev", request.Provider.Id);
    }

    [Fact]
    public async Task GetResourceIdentityProvisioningStatusAsync_RequiresReadPermissionOnTargetResource()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: new ResourceScopedAuthorizationService(
                ("identity:dev", CloudShellPermissions.Resources.Read)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.GetResourceIdentityProvisioningStatusAsync("api"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            "The 'resources.read' or 'resources.manage' permission is required for resource 'api'.",
            exception.Message);
    }

    [Fact]
    public async Task GetResourceIdentityProvisioningStatusAsync_RequiresReadPermissionOnProvisioningResource()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: new ResourceScopedAuthorizationService(
                ("api", CloudShellPermissions.Resources.Read)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.GetResourceIdentityProvisioningStatusAsync("api"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            "The 'resources.read' or 'resources.manage' permission is required for resource 'identity:dev'.",
            exception.Message);
    }

    [Fact]
    public async Task GetResourceIdentityProvisioningStatusAsync_AllowsReadOnTargetAndProvisioningResource()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Running),
                CreateResource("identity:dev", ResourceState.Running)
            ],
            authorization: new ResourceScopedAuthorizationService(
                ("api", CloudShellPermissions.Resources.Read),
                ("identity:dev", CloudShellPermissions.Resources.Read)),
            identityProviders:
            [
                new(
                    "identity:dev",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    ProvisioningResourceId: "identity:dev")
            ],
            configureDeclarations: declarations => declarations.Declare(
                new TestCloudShellBuilder(),
                "test",
                "api",
                identity: new ResourceIdentityBinding("identity:dev", Name: "api-service")));

        var result = await controlPlane.GetResourceIdentityProvisioningStatusAsync("api");

        Assert.Equal("identity:dev", result.ProviderId);
        var status = Assert.Single(result.Statuses);
        Assert.Equal("api", status.Identity.ResourceId);
        Assert.Equal("api-service", status.Identity.Name);
        Assert.Equal(ResourceIdentityProvisioningState.Unknown, status.State);
    }

    [Theory]
    [InlineData(ResourceState.Starting, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Stopping, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Paused, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Unknown, ResourceActionIds.Start)]
    public async Task ExecuteResourceActionAsync_AllowsStateValidActions(
        ResourceState state,
        string actionId)
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", state)], provider);

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", actionId));

        Assert.Equal([$"target:{actionId}"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_NotifiesResourceChanges()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)], provider);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Collection(
            notifications,
            notification =>
            {
                Assert.Equal(ResourceChangeKind.ResourceActionStarted, notification.Kind);
                Assert.Equal("target", notification.ResourceId);
                Assert.Equal(ResourceActionIds.Stop, notification.ActionId);
                Assert.Contains("target", notification.Resources);
            },
            notification =>
            {
                Assert.Equal(ResourceChangeKind.ResourceActionExecuted, notification.Kind);
                Assert.Equal("target", notification.ResourceId);
                Assert.Equal(ResourceActionIds.Stop, notification.ActionId);
                Assert.Contains("target", notification.Resources);
            });
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_LogsActionAndLifecycleEventsForStop()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents);

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop,
                TriggeredBy: "operator"));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "operator");
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Stopping &&
            resourceEvent.TriggeredBy == "operator");
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Stopped &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("Executed stop", StringComparison.Ordinal));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Provider.ForEvent(provider.Id, "action.executed") &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("Test provider executed action 'stop'.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_UsesAuthenticatedActorWhenTriggeredByIsOmitted()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents,
            httpContextAccessor: CreateHttpContextAccessor(
                new Claim(ClaimTypes.Name, "alice"),
                new Claim(ClaimTypes.NameIdentifier, "alice-id")));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "alice");
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Stopped &&
            resourceEvent.TriggeredBy == "alice");
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_AuthenticatedActorOverridesClientSuppliedTriggeredBy()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents,
            httpContextAccessor: CreateHttpContextAccessor(new Claim(ClaimTypes.Name, "alice")));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop,
                TriggeredBy: "system"));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "alice");
        Assert.DoesNotContain(events, resourceEvent => resourceEvent.TriggeredBy == "system");
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_UsesUserActorForUnauthenticatedHttpRequestWhenTriggeredByIsOmitted()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents,
            httpContextAccessor: CreateUnauthenticatedHttpContextAccessor());

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "user");
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Stopped &&
            resourceEvent.TriggeredBy == "user");
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_UnauthenticatedHttpRequestActorOverridesClientSuppliedTriggeredBy()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents,
            httpContextAccessor: CreateUnauthenticatedHttpContextAccessor());

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop,
                TriggeredBy: "system"));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "user");
        Assert.DoesNotContain(events, resourceEvent => resourceEvent.TriggeredBy == "system");
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_LeavesActorEmptyForSystemWorkWithoutRequestContext()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents);

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy is null);
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Stopped &&
            resourceEvent.TriggeredBy is null);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_PrefersPrincipalIdentifierClaimsOverDisplayName()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents,
            httpContextAccessor: CreateHttpContextAccessor(
                new Claim(ClaimTypes.Name, "Alice Local Developer"),
                new Claim(ClaimTypes.Email, "alice@example.test")));

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "target",
                ResourceActionIds.Stop));

        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "target"));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
            resourceEvent.TriggeredBy == "alice@example.test");
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_LogsDependencyLifecycleEventsOnDependencyResource()
    {
        var provider = new TestResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Stopped, dependsOn: ["vault"]),
                CreateResource("vault", ResourceState.Stopped)
            ],
            provider,
            resourceEvents: resourceEvents);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "api",
                ResourceActionIds.Start,
                StartDependencies: true,
                TriggeredBy: "operator"));

        Assert.Equal(["vault:start", "api:start"], provider.ExecutedActions);
        Assert.Equal(
            [
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionExecuted,
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionExecuted
            ],
            notifications.Select(notification => notification.Kind).ToArray());
        Assert.Equal(
            ["vault", "vault", "api", "api"],
            notifications.Select(notification => notification.ResourceId!).ToArray());
        Assert.All(notifications, notification =>
        {
            Assert.Equal(ResourceActionIds.Start, notification.ActionId);
            Assert.Contains(notification.ResourceId!, notification.Resources);
        });

        var dependencyEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "vault"));
        Assert.Contains(dependencyEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Start) &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("Dependency auto-start for api", StringComparison.Ordinal));
        Assert.Contains(dependencyEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Starting &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("Dependency auto-start for api", StringComparison.Ordinal));
        Assert.Contains(dependencyEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Started &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("Executed start", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Dependency auto-start for api", StringComparison.Ordinal));

        var rootEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "api"));
        Assert.Contains(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Start) &&
            resourceEvent.TriggeredBy == "operator");
        Assert.Contains(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.Started &&
            resourceEvent.TriggeredBy == "operator");
        Assert.DoesNotContain(rootEvents, resourceEvent =>
            resourceEvent.Message.Contains("Dependency auto-start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_NotifiesFailedDependencyAutoStart()
    {
        var provider = new TestResourceProvider
        {
            FailedActionResourceId = "vault",
            FailureMessage = "Docker is unavailable."
        };
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Stopped, dependsOn: ["vault"]),
                CreateResource("vault", ResourceState.Stopped)
            ],
            provider,
            resourceEvents: resourceEvents);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    "api",
                    ResourceActionIds.Start,
                    StartDependencies: true,
                    TriggeredBy: "operator")));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains("Reason: Docker is unavailable.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["vault:start"], provider.ExecutedActions);
        Assert.Equal(
            [
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionFailed,
                ResourceChangeKind.ResourceActionFailed
            ],
            notifications.Select(notification => notification.Kind).ToArray());
        Assert.Equal(["vault", "vault", "api"], notifications.Select(notification => notification.ResourceId!).ToArray());
        Assert.All(notifications, notification => Assert.Equal(ResourceActionIds.Start, notification.ActionId));
        Assert.All(notifications, notification => Assert.Contains(notification.ResourceId!, notification.Resources));

        var rootEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "api"));
        Assert.Contains(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.StartFailed &&
            resourceEvent.Severity == ResourceSignalSeverity.Error &&
            resourceEvent.Message.Contains("A dependency could not start", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_NotifiesIntermediateResourceWhenTransitiveDependencyFails()
    {
        var provider = new TestResourceProvider
        {
            FailedActionResourceId = "sql",
            FailureMessage = "Docker is unavailable."
        };
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("frontend", ResourceState.Stopped, dependsOn: ["api"]),
                CreateResource("api", ResourceState.Stopped, dependsOn: ["sql"]),
                CreateResource("sql", ResourceState.Stopped)
            ],
            provider,
            resourceEvents: resourceEvents);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    "frontend",
                    ResourceActionIds.Start,
                    StartDependencies: true,
                    TriggeredBy: "operator")));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains("Dependency path: frontend -> api -> sql.", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Reason: Docker is unavailable.", exception.Message, StringComparison.Ordinal);
        Assert.Equal(["sql:start"], provider.ExecutedActions);
        Assert.Equal(
            [
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionFailed,
                ResourceChangeKind.ResourceActionFailed,
                ResourceChangeKind.ResourceActionFailed
            ],
            notifications.Select(notification => notification.Kind).ToArray());
        Assert.Equal(
            ["sql", "sql", "api", "frontend"],
            notifications.Select(notification => notification.ResourceId!).ToArray());
        Assert.All(notifications, notification => Assert.Equal(ResourceActionIds.Start, notification.ActionId));

        var apiEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "api"));
        Assert.Contains(apiEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForFailedAction(ResourceActionIds.Start) &&
            resourceEvent.Severity == ResourceSignalSeverity.Error &&
            resourceEvent.Message.Contains("A dependency could not start", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));
        Assert.Contains(apiEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.StartFailed &&
            resourceEvent.Severity == ResourceSignalSeverity.Error &&
            resourceEvent.Message.Contains("A dependency could not start", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));

        var frontendEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "frontend"));
        Assert.Contains(frontendEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.StartFailed &&
            resourceEvent.Severity == ResourceSignalSeverity.Error &&
            resourceEvent.Message.Contains("A dependency could not start", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_UsesDisplayNamesInDependencyAutoStartFailures()
    {
        var provider = new TestResourceProvider
        {
            FailedActionResourceId = "application:sql",
            FailureMessage = "Docker is unavailable."
        };
        var controlPlane = CreateControlPlane(
            [
                CreateResource(
                    "application:frontend",
                    ResourceState.Stopped,
                    dependsOn: ["application:api"],
                    name: "frontend",
                    displayName: "Frontend"),
                CreateResource(
                    "application:api",
                    ResourceState.Stopped,
                    dependsOn: ["application:sql"],
                    name: "api",
                    displayName: "API"),
                CreateResource(
                    "application:sql",
                    ResourceState.Stopped,
                    name: "sql",
                    displayName: "SQL Server")
            ],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    "application:frontend",
                    ResourceActionIds.Start,
                    StartDependencies: true,
                    TriggeredBy: "operator")));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains(
            "Could not auto-start dependency 'SQL Server (sql)' for resource 'Frontend (frontend)'.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependency path: Frontend (frontend) -> API (api) -> SQL Server (sql).",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_CanWarnAndContinueWhenDependencyAutoStartFails()
    {
        var provider = new TestResourceProvider
        {
            FailedActionResourceId = "vault",
            FailureMessage = "Docker is unavailable."
        };
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("api", ResourceState.Stopped, dependsOn: ["vault"]),
                CreateResource("vault", ResourceState.Stopped)
            ],
            provider,
            resourceEvents: resourceEvents);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        var result = await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "api",
                ResourceActionIds.Start,
                StartDependencies: true,
                TriggeredBy: "operator",
                DependencyStartFailureBehavior: DependencyStartFailureBehavior.WarnAndContinue));

        Assert.Equal(["vault:start", "api:start"], provider.ExecutedActions);
        Assert.Contains("Executed start.", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Dependency auto-start warning", result.Message, StringComparison.Ordinal);
        var signal = Assert.Single(result.Signals);
        Assert.Equal(ResourceSignalSeverity.Warning, signal.Severity);
        Assert.Contains("Dependency auto-start warning", signal.Message, StringComparison.Ordinal);
        Assert.Contains("Docker is unavailable", signal.Message, StringComparison.Ordinal);
        Assert.Equal(
            [
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionFailed,
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionExecuted
            ],
            notifications.Select(notification => notification.Kind).ToArray());
        Assert.Equal(["vault", "vault", "api", "api"], notifications.Select(notification => notification.ResourceId!).ToArray());

        var rootEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "api"));
        Assert.Contains(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Start) &&
            resourceEvent.Severity == ResourceSignalSeverity.Warning &&
            resourceEvent.Message.Contains("Dependency auto-start warning", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.StartFailed);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_WarnsWhenTransitiveDependencyOfRunningDependencyFails()
    {
        var provider = new TestResourceProvider
        {
            FailedActionResourceId = "sql",
            FailureMessage = "Docker is unavailable."
        };
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("frontend", ResourceState.Stopped, dependsOn: ["api"]),
                CreateResource("api", ResourceState.Running, dependsOn: ["sql"]),
                CreateResource("sql", ResourceState.Stopped)
            ],
            provider,
            resourceEvents: resourceEvents);
        var notifications = new List<ResourceChangeNotification>();
        controlPlane.ResourcesChanged += (_, notification) => notifications.Add(notification);

        var result = await controlPlane.ExecuteResourceActionAsync(
            new ExecuteResourceActionCommand(
                "frontend",
                ResourceActionIds.Start,
                StartDependencies: true,
                TriggeredBy: "operator",
                DependencyStartFailureBehavior: DependencyStartFailureBehavior.WarnAndContinue));

        Assert.Equal(["sql:start", "frontend:start"], provider.ExecutedActions);
        Assert.Contains("Executed start.", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Dependency auto-start warning", result.Message, StringComparison.Ordinal);
        var signal = Assert.Single(result.Signals);
        Assert.Equal(ResourceSignalSeverity.Warning, signal.Severity);
        Assert.Contains("Dependency auto-start warning", signal.Message, StringComparison.Ordinal);
        Assert.Contains("Docker is unavailable", signal.Message, StringComparison.Ordinal);
        Assert.Equal(
            [
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionFailed,
                ResourceChangeKind.ResourceActionStarted,
                ResourceChangeKind.ResourceActionExecuted
            ],
            notifications.Select(notification => notification.Kind).ToArray());
        Assert.Equal(
            ["sql", "sql", "frontend", "frontend"],
            notifications.Select(notification => notification.ResourceId!).ToArray());

        var rootEvents = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "frontend"));
        Assert.Contains(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Actions.ForAction(ResourceActionIds.Start) &&
            resourceEvent.Severity == ResourceSignalSeverity.Warning &&
            resourceEvent.Message.Contains("Dependency auto-start warning", StringComparison.Ordinal) &&
            resourceEvent.Message.Contains("Docker is unavailable", StringComparison.Ordinal));
        Assert.DoesNotContain(rootEvents, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Lifecycle.StartFailed);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_BlocksStopWhenRunningDependentsExist()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependent", ResourceState.Running, dependsOn: ["target"])
            ],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.DependentResourcesRunning, exception.Error.Code);
        Assert.Contains("depend on this resource", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_DoesNotBlockStopForPausedDependents()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependent", ResourceState.Paused, dependsOn: ["target"])
            ],
            provider);

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.DeleteResourceAsync("missing"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.DeleteResourceAsync("target"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal("The 'resources.manage' permission is required for resource 'target'.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsUnsupportedProviderDelete()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            new TestReadOnlyResourceProvider());

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.DeleteResourceAsync("target"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceDeleteUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support delete.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_ReturnsProviderResult()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var result = await controlPlane.DeleteResourceAsync("target");

        Assert.Equal("Deleted target.", result.Message);
    }

    [Fact]
    public async Task UpdateResourceImageAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceImageAsync(new UpdateResourceImageCommand("missing", "example/api:1")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
    }

    [Fact]
    public async Task UpdateResourceImageAsync_RejectsUnsupportedProvider()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            new TestReadOnlyResourceProvider());

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceImageAsync(new UpdateResourceImageCommand("target", "example/api:1")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceImageUpdateUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support image updates.", exception.Message);
    }

    [Fact]
    public async Task UpdateResourceImageAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestImageUpdateResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.UpdateResourceImageAsync(new UpdateResourceImageCommand("target", "example/api:1")));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Empty(provider.UpdatedImages);
    }

    [Fact]
    public async Task UpdateResourceImageAsync_ReturnsUnavailableWhenProviderPreflightFails()
    {
        var provider = new TestImageUpdateResourceProvider
        {
            FailureMessage = "Container app resource 'target' cannot update image and restart because restart is blocked."
        };
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceImageAsync(new UpdateResourceImageCommand("target", "example/api:1")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceImageUpdateUnavailable, exception.Error.Code);
        Assert.Equal(
            "Container app resource 'target' cannot update image and restart because restart is blocked.",
            exception.Message);
        Assert.Empty(provider.UpdatedImages);
    }

    [Fact]
    public async Task UpdateResourceImageAsync_DispatchesToImageUpdateProvider()
    {
        var provider = new TestImageUpdateResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents);

        using var activity = new Activity("update-image-test");
        activity.Start();

        var result = await controlPlane.UpdateResourceImageAsync(
            new UpdateResourceImageCommand(
                "target",
                "example/api:20260608",
                RestartIfRunning: false,
                TriggeredBy: "build-server"));

        Assert.Equal("Updated target.", result.Message);
        Assert.Equal(["target:example/api:20260608:False:build-server"], provider.UpdatedImages);
        var resourceEvent = Assert.Single(resourceEvents.GetEvents(new ResourceEventQuery(
            ResourceId: "target",
            EventType: ResourceEventTypes.Events.Deployment.ImageUpdated,
            TriggeredBy: "build-server",
            TraceId: activity.TraceId.ToString())));
        Assert.Contains("example/api:20260608", resourceEvent.Message, StringComparison.Ordinal);
        Assert.Equal(activity.TraceId.ToString(), resourceEvent.TraceId);
        Assert.Equal(activity.SpanId.ToString(), resourceEvent.SpanId);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_RejectsInvalidReplicaCount()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceReplicasAsync(new UpdateResourceReplicasCommand("target", 0)));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Equal("Replicas must be greater than or equal to 1.", exception.Message);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceReplicasAsync(new UpdateResourceReplicasCommand("missing", 2)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_RejectsUnsupportedProvider()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            new TestReadOnlyResourceProvider());

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceReplicasAsync(new UpdateResourceReplicasCommand("target", 2)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceReplicasUpdateUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support replica updates.", exception.Message);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestReplicaUpdateResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.UpdateResourceReplicasAsync(new UpdateResourceReplicasCommand("target", 2)));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Empty(provider.UpdatedReplicas);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_ReturnsUnavailableWhenProviderPreflightFails()
    {
        var provider = new TestReplicaUpdateResourceProvider
        {
            FailureMessage = "Container app resource 'target' cannot update replicas and restart because restart is blocked."
        };
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.UpdateResourceReplicasAsync(new UpdateResourceReplicasCommand("target", 3)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceReplicasUpdateUnavailable, exception.Error.Code);
        Assert.Equal(
            "Container app resource 'target' cannot update replicas and restart because restart is blocked.",
            exception.Message);
        Assert.Empty(provider.UpdatedReplicas);
    }

    [Fact]
    public async Task UpdateResourceReplicasAsync_DispatchesToReplicaUpdateProvider()
    {
        var provider = new TestReplicaUpdateResourceProvider();
        var resourceEvents = new InMemoryResourceEventStore();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            resourceEvents: resourceEvents);

        var result = await controlPlane.UpdateResourceReplicasAsync(
            new UpdateResourceReplicasCommand(
                "target",
                3,
                RestartIfRunning: false,
                TriggeredBy: "load-balancer"));

        Assert.Equal("Updated target.", result.Message);
        Assert.Equal(["target:3:False:load-balancer"], provider.UpdatedReplicas);
        var resourceEvent = Assert.Single(resourceEvents.GetEvents(new ResourceEventQuery(
            ResourceId: "target",
            EventType: ResourceEventTypes.Events.Deployment.ReplicasUpdated,
            TriggeredBy: "load-balancer")));
        Assert.Contains("3", resourceEvent.Message, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownProvider()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("missing", "target")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceProviderNotFound, exception.Error.Code);
        Assert.Equal("Resource provider 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("test", "missing")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotAvailable, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not available.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownResourceGroup()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("test", "target", "missing-group")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceGroupNotFound, exception.Error.Code);
        Assert.Equal("Resource group 'missing-group' could not be found.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsSelfDependencies()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(
                new RegisterResourceCommand("test", "target", DependsOn: [" TARGET "])));

        Assert.Equal(ControlPlaneErrorCodes.ResourceSelfDependency, exception.Error.Code);
        Assert.Equal("Resource 'target' cannot depend on itself.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_NormalizesDependencies()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependency", ResourceState.Running)
            ]);

        await controlPlane.RegisterResourceAsync(
            new RegisterResourceCommand("test", "target", DependsOn: [" dependency ", "DEPENDENCY"]));

        var registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.Equal(["dependency"], registration.DependsOn);
    }

    [Fact]
    public async Task AssignResourceGroupAsync_NormalizesGroupAndDependencies()
    {
        var group = new ResourceGroup("group-one", "Group One", "Test group", []);
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependency", ResourceState.Running)
            ],
            groups: [group]);

        await controlPlane.AssignResourceGroupAsync(
            new AssignResourceGroupCommand(" target ", " group-one ", [" dependency ", "DEPENDENCY"]));

        var registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.Equal("group-one", registration.ResourceGroupId);
        Assert.Equal(["dependency"], registration.DependsOn);
    }

    [Fact]
    public async Task SetResourceDependenciesAsync_RejectsUnknownDependencies()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.SetResourceDependenciesAsync(
                new SetResourceDependenciesCommand("target", ["missing"])));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotAvailable, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not available.", exception.Message);
    }

    [Fact]
    public async Task SetResourceIdentityAsync_StoresIdentityBinding()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        await controlPlane.SetResourceIdentityAsync(
            new SetResourceIdentityCommand(
                " target ",
                ResourceIdentityBinding.RequireIdentity()));

        var registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.NotNull(registration.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, registration.IdentityBinding.Kind);

        await controlPlane.SetResourceIdentityAsync(new SetResourceIdentityCommand("target", null));
        registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.Null(registration.IdentityBinding);
    }

    [Fact]
    public async Task SetResourceIdentityAsync_RejectsDeniedManagePermission()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            authorization: new PermissionAuthorizationService(CloudShellPermissions.Resources.Read));

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.SetResourceIdentityAsync(
                new SetResourceIdentityCommand("target", ResourceIdentityBinding.RequireIdentity())));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
    }

    [Fact]
    public async Task CreateResourceAsync_RejectsMissingConfiguration()
    {
        var controlPlane = CreateControlPlane([]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.CreateResourceAsync(
                new CreateResourceCommand("test", "test.resource", "target", "Target", default)));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Equal("Configuration is required.", exception.Message);
    }

    [Fact]
    public async Task CreateResourceAsync_PassesClassAndAttributesToCreationProvider()
    {
        var provider = new TestResourceCreationProvider();
        var controlPlane = CreateControlPlane(
            [],
            provider,
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                ["test.resource"] = ResourceClass.Project
            });

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                "test",
                "test.resource",
                "target",
                "Target",
                JsonSerializer.SerializeToElement(new { enabled = true }),
                ResourceClass: ResourceClass.Project,
                Attributes: new Dictionary<string, string>
                {
                    [" project.path "] = " src/API/API.csproj ",
                    ["empty"] = ""
                }));

        Assert.NotNull(provider.CreatedRequest);
        Assert.Equal(ResourceClass.Project, provider.CreatedRequest.ResourceClass);
        Assert.Equal("src/API/API.csproj", provider.CreatedRequest.ResourceAttributes["project.path"]);
        Assert.False(provider.CreatedRequest.ResourceAttributes.ContainsKey("empty"));
    }

    [Fact]
    public async Task CreateResourceAsync_UsesResourceTypeClassWhenCreationClassIsOmitted()
    {
        var provider = new TestResourceCreationProvider();
        var controlPlane = CreateControlPlane(
            [],
            provider,
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                ["test.resource"] = ResourceClass.Project
            });

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                "test",
                "test.resource",
                "target",
                "Target",
                JsonSerializer.SerializeToElement(new { enabled = true })));

        Assert.NotNull(provider.CreatedRequest);
        Assert.Equal(ResourceClass.Project, provider.CreatedRequest.ResourceClass);
    }

    [Fact]
    public async Task CreateResourceAsync_StartsResourceWhenRequested()
    {
        var provider = new TestResourceCreationProvider();
        var controlPlane = CreateControlPlane([], provider);

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                "test",
                "test.resource",
                "target",
                "Target",
                JsonSerializer.SerializeToElement(new { enabled = true }),
                StartAfterCreate: true));

        Assert.NotNull(provider.CreatedRequest);
        Assert.Equal(["target:start"], provider.ExecutedActions);
    }

    [Fact]
    public async Task CreateResourceAsync_RejectsStorageOwnedVolumeWithoutStorageManagePermission()
    {
        var options = new PlatformResourceOptions();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var storage = CreateStorageResource("storage:local");
        var controlPlane = CreateControlPlane(
            [storage],
            provider,
            authorization: new ResourceScopedAuthorizationService(),
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                [PlatformResourceProvider.VolumeResourceType] = ResourceClass.Storage
            });

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.CreateResourceAsync(CreateStorageOwnedVolumeCommand()));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal(
            $"The '{CloudShellPermissions.Resources.Manage}' permission is required for resource 'storage:local'.",
            exception.Message);
    }

    [Fact]
    public async Task CreateResourceAsync_AllowsStorageOwnedVolumeWithStorageManagePermission()
    {
        var options = new PlatformResourceOptions();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var storage = CreateStorageResource("storage:local");
        var controlPlane = CreateControlPlane(
            [storage],
            provider,
            authorization: new ResourceScopedAuthorizationService(
                ("storage:local", CloudShellPermissions.Resources.Manage)),
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                [PlatformResourceProvider.VolumeResourceType] = ResourceClass.Storage
            });

        await controlPlane.CreateResourceAsync(CreateStorageOwnedVolumeCommand());

        var volume = Assert.Single(provider.GetResources(), resource => resource.Id == "volume:data");
        Assert.Equal("storage:local", volume.ParentResourceId);
        Assert.Equal(ResourceVisibility.Hidden, volume.Visibility);
    }

    [Fact]
    public async Task CreateResourceAsync_AllowsStorageOwnedVolumeWithUserResourcePermissionClaim()
    {
        var options = new PlatformResourceOptions();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var storage = CreateStorageResource("storage:local");
        var controlPlane = CreateControlPlane(
            [storage],
            provider,
            authorization: CreateClaimsAuthorization(
                CreateResourcePermissionClaim(
                    storage.Id,
                    CloudShellPermissions.Resources.Manage)),
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                [PlatformResourceProvider.VolumeResourceType] = ResourceClass.Storage
            });

        await controlPlane.CreateResourceAsync(CreateStorageOwnedVolumeCommand());

        var volume = Assert.Single(provider.GetResources(), resource => resource.Id == "volume:data");
        Assert.Equal("storage:local", volume.ParentResourceId);
        Assert.Equal(ResourceVisibility.Hidden, volume.Visibility);
    }

    [Fact]
    public async Task DefaultOrchestrator_ExecutesOrchestratorServiceInstancesForReplicas()
    {
        var resource = CreateResource("application:api", ResourceState.Stopped);
        var provider = new TestOrchestratorServiceProcedureProvider();
        var resourceManager = new TestResourceManagerStore(
            [resource],
            [provider],
            [],
            new Dictionary<string, ResourceClass>());
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    provider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var orchestrator = new DefaultResourceOrchestrator();

        var result = await orchestrator.ExecuteActionAsync(
            new ResourceOrchestrationContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                resourceManager,
                registrations),
            ResourceAction.Start);

        Assert.Equal("Started application:api.", result.Message);
        Assert.Equal(["start:api"], provider.PreparedActions);
        Assert.Equal(
            [
                "start:api-replica-1:1/3",
                "start:api-replica-2:2/3",
                "start:api-replica-3:3/3"
            ],
            provider.InstanceActions);
    }

    [Fact]
    public async Task CreateResourceAsync_RejectsResourceClassMismatch()
    {
        var provider = new TestResourceCreationProvider();
        var controlPlane = CreateControlPlane(
            [],
            provider,
            resourceTypeClasses: new Dictionary<string, ResourceClass>
            {
                ["test.resource"] = ResourceClass.Project
            });

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.CreateResourceAsync(
                new CreateResourceCommand(
                    "test",
                    "test.resource",
                    "target",
                    "Target",
                    JsonSerializer.SerializeToElement(new { enabled = true }),
                    ResourceClass: ResourceClass.Container)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceClassMismatch, exception.Error.Code);
        Assert.Contains("requires class 'Project'", exception.Message, StringComparison.Ordinal);
        Assert.Null(provider.CreatedRequest);
    }

    [Fact]
    public async Task ListTraceSpansAsync_RequiresTraceReadPermission()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ListTraceSpansAsync());

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Contains(CloudShellPermissions.Observability.Traces.Read, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ObservabilityQueries_FilterSignalsToReadableResources()
    {
        var logStore = new TestLogStore(
            new LogDescriptor("visible-log", "Visible", "test", "visible", LogSourceKind.Resource, "visible"),
            new LogDescriptor("hidden-log", "Hidden", "test", "hidden", LogSourceKind.Resource, "hidden"));
        var traceStore = new TestTraceStore(
            CreateTraceSpan("visible-trace", "visible"),
            CreateTraceSpan("hidden-trace", "hidden"));
        var metricStore = new TestMetricStore(
            CreateMetricPoint("visible", "visible"),
            CreateMetricPoint("hidden", "hidden"));
        var controlPlane = CreateControlPlane(
            [
                CreateResource("visible", ResourceState.Running),
                CreateResource("hidden", ResourceState.Running)
            ],
            authorization: new PermissionAndResourceAuthorizationService(
                [
                    CloudShellPermissions.Observability.Logs.Read,
                    CloudShellPermissions.Observability.Traces.Read,
                    CloudShellPermissions.Observability.Metrics.Read
                ],
                ("visible", CloudShellPermissions.Resources.Read)),
            logStore: logStore,
            traceStore: traceStore,
            metricStore: metricStore);

        var logs = await controlPlane.ListLogsAsync();
        var spans = await controlPlane.ListTraceSpansAsync();
        var points = await controlPlane.ListMetricPointsAsync();

        Assert.Equal("visible", Assert.Single(logs).ResourceId);
        Assert.Equal("visible", Assert.Single(spans).ResourceId);
        Assert.Equal("visible", Assert.Single(points).ResourceId);
    }

    private static IControlPlane CreateControlPlane(
        IReadOnlyList<Resource> resources,
        IResourceProvider? provider = null,
        IReadOnlyList<ResourceGroup>? groups = null,
        ICloudShellAuthorizationService? authorization = null,
        IReadOnlyDictionary<string, ResourceClass>? resourceTypeClasses = null,
        IReadOnlyList<ResourcePermissionGrant>? permissionGrants = null,
        IReadOnlyList<ResourceIdentityProviderDefinition>? identityProviders = null,
        IReadOnlyList<IResourceIdentityProvisioner>? identityProvisioners = null,
        IReadOnlyList<IResourceIdentityProviderSetupHandler>? identityProviderSetupHandlers = null,
        IReadOnlyList<IResourceIdentityDirectoryProvider>? identityDirectoryProviders = null,
        IReadOnlyList<IResourcePermissionGrantStatusProvider>? permissionGrantStatusProviders = null,
        IReadOnlyList<IResourceOrchestrationDescriptorProvider>? descriptorProviders = null,
        IReadOnlyList<IContainerHostProvider>? containerHostProviders = null,
        IReadOnlyList<IResourceActionAvailabilityProvider>? actionAvailabilityProviders = null,
        Action<ResourceDeclarationStore>? configureDeclarations = null,
        IResourceEventStore? resourceEvents = null,
        IHttpContextAccessor? httpContextAccessor = null,
        ILogStore? logStore = null,
        ITraceStore? traceStore = null,
        IMetricStore? metricStore = null)
    {
        provider ??= new TestResourceProvider();
        var registrations = new TestResourceRegistrationStore(resources.Select(resource =>
            new ResourceRegistration(
                resource.Id,
                provider.Id,
                null,
                DateTimeOffset.UtcNow,
                resource.DependsOn)));
        var resourceManager = new TestResourceManagerStore(
            resources,
            [provider],
            groups ?? [],
            resourceTypeClasses ?? new Dictionary<string, ResourceClass>());
        var resourceGroups = new TestResourceGroupStore(groups ?? []);
        var declarations = new ResourceDeclarationStore();
        foreach (var grant in permissionGrants ?? [])
        {
            declarations.AddPermissionGrant(grant);
        }

        configureDeclarations?.Invoke(declarations);

        var templates = new ResourceTemplateService(resourceManager, resourceGroups, registrations);
        var identityProvisioning = new ResourceIdentityProvisioningService(
            declarations,
            registrations,
            new ResourceIdentityProviderCatalog(identityProviders ?? []),
            identityProvisioners ?? []);
        var identityProviderSetup = new ResourceIdentityProviderSetupService(
            declarations,
            new ResourceIdentityProviderCatalog(identityProviders ?? []),
            identityProviderSetupHandlers ?? []);
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            descriptorProviders ?? [],
            resourceManager,
            registrations,
            declarations,
            CreateSelectionStore(),
            containerHostProviders ?? [],
            actionAvailabilityProviders: actionAvailabilityProviders ?? [],
            resourceEvents: resourceEvents);

        return new InProcessControlPlane(
            resourceManager,
            resourceGroups,
            registrations,
            declarations,
            orchestration,
            identityProvisioning,
            identityProviderSetup,
            templates,
            logStore ?? new EmptyLogStore(),
            traceStore ?? new EmptyTraceStore(),
            metricStore ?? new EmptyMetricStore(),
            new InMemoryResourceHealthStore(Options.Create(new ResourceHealthOptions())),
            new ResourceHealthProbeService(new TestHttpClientFactory()),
            new ResourceHealthRefreshCoordinator(),
            CreateSelectionStore(),
            [],
            authorization ?? new AllowAllAuthorizationService(),
            resourceEvents,
            httpContextAccessor,
            new ResourceIdentityProviderCatalog(identityProviders ?? []),
            identityDirectoryProviders ?? [],
            permissionGrantStatusProviders ?? []);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role);
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }

    private static IHttpContextAccessor CreateUnauthenticatedHttpContextAccessor() =>
        new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

    private static Resource CreateResource(
        string id,
        ResourceState state,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<ResourceAction>? actions = null,
        string? name = null,
        string? displayName = null) =>
        new(
            id,
            name ?? id,
            "Test",
            "Test",
            "local",
            state,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            Actions: actions ??
            [
                ResourceAction.Start,
                ResourceAction.Stop,
                ResourceAction.Pause,
                ResourceAction.Restart
            ],
            DisplayName: displayName);

    private static TraceSpan CreateTraceSpan(string traceId, string resourceId) =>
        new(
            traceId,
            $"{traceId}-span",
            null,
            "GET /test",
            resourceId,
            $"{resourceId}-service",
            "server",
            "ok",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(10));

    private static MetricPoint CreateMetricPoint(string name, string resourceId) =>
        new(
            name,
            resourceId,
            $"{resourceId}-service",
            1,
            DateTimeOffset.UtcNow);

    private static Resource CreateIdentityResource(string id, string identityName) =>
        CreateResource(id, ResourceState.Running) with
        {
            Identity = new ResourceIdentityBinding("identity:dev", Name: identityName)
        };

    private sealed class TestResourceIdentityDirectoryProvider : IResourceIdentityDirectoryProvider
    {
        public List<ResourceIdentityDirectoryRequest> Requests { get; } = [];

        public string ProviderId => "identity:dev";

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
                        "operators",
                        "Operators",
                        ProviderId),
                    "Operators",
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

    private static Resource CreateStorageResource(string id) =>
        new(
            id,
            id,
            StorageProviderNames.LocalStorage,
            StorageProviderNames.LocalStorage,
            "local",
            null,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageProvider] = StorageProviderNames.LocalStorage,
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem
            },
            Capabilities:
            [
                new(ResourceCapabilityIds.StorageProvider),
                new(ResourceCapabilityIds.StorageMountProvider)
            ]);

    private static CreateResourceCommand CreateStorageOwnedVolumeCommand() =>
        new(
            PlatformResourceProvider.ProviderId,
            PlatformResourceProvider.VolumeResourceType,
            "volume:data",
            "Data",
            JsonSerializer.SerializeToElement(new VolumeResourceDefinition(
                "volume:data",
                "Data",
                StorageResourceId: "storage:local",
                SubPath: "data")),
            ResourceClass: ResourceClass.Storage);

    private static ClaimsCloudShellAuthorizationService CreateClaimsAuthorization(params Claim[] claims)
    {
        var options = new CloudShellAuthenticationOptions { Enabled = true };
        var identity = new ClaimsIdentity(
            claims,
            "Test",
            ClaimTypes.Name,
            options.RoleClaimType);
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };

        return new ClaimsCloudShellAuthorizationService(
            new HttpContextAccessor { HttpContext = context },
            Options.Create(options));
    }

    private static Claim CreateResourcePermissionClaim(string resourceId, string permission) =>
        new(
            CloudShellAuthenticationOptions.ResourcePermissionClaimType,
            ResourcePermissionClaimAuthorization.CreateResourcePermissionClaimValue(
                resourceId,
                permission));

    private static ResourceOrchestratorSelectionStore CreateSelectionStore()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        return new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(contentRoot),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
    }

    private sealed class TestResourceProvider : IResourceProvider, IResourceProcedureProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public List<string> ExecutedActions { get; } = [];

        public string? FailedActionResourceId { get; init; }

        public string FailureMessage { get; init; } = "Action failed.";

        public IReadOnlyList<Resource> GetResources() => [];

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Id}."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add($"{context.Resource.Id}:{action.Id}");
            if (string.Equals(context.Resource.Id, FailedActionResourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(FailureMessage);
            }

            context.AppendProviderEvent(
                Id,
                "action.executed",
                $"Test provider executed action '{action.Id}'.");
            return Task.FromResult(ResourceProcedureResult.Completed($"Executed {action.Id}."));
        }
    }

    private sealed class TestActionAvailabilityProvider(
        string resourceId,
        string actionId,
        string reason) : IResourceActionAvailabilityProvider
    {
        public string Reason => reason;

        public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase);

        public Task<string?> GetActionUnavailableReasonAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(reason);
    }

    private sealed class StaticWorkloadDescriptorProvider(
        string resourceId,
        ResourceWorkloadConfiguration workload) : IResourceOrchestrationDescriptorProvider
    {
        public bool CanDescribe(Resource resource) =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceOrchestrationDescriptor> DescribeAsync(
            Resource resource,
            ResourceOrchestrationDescriptorContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                resource.EffectiveTypeId,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(workload)));
    }

    private sealed class StaticContainerHostDescriptorProvider(
        string resourceId,
        ContainerHostDescriptor host) : IResourceOrchestrationDescriptorProvider
    {
        public bool CanDescribe(Resource resource) =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceOrchestrationDescriptor> DescribeAsync(
            Resource resource,
            ResourceOrchestrationDescriptorContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceOrchestrationDescriptor(
                resource.Id,
                ContainerHostResourceTypes.ContainerHost,
                resource.DependsOn,
                [],
                resource.Endpoints,
                "1.0",
                JsonSerializer.SerializeToElement(host)));
    }

    private sealed class TestResourceIdentityProvisioner(string providerId) : IResourceIdentityProvisioner
    {
        public List<ResourceIdentityProvisioningRequest> Requests { get; } = [];

        public string ProviderId => providerId;

        public bool CanProvision(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProvisioningResult> ProvisionAsync(
            ResourceIdentityProvisioningRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProvisioningResult(request.Provider.Id));
        }
    }

    private sealed class TestResourceIdentityProviderSetupHandler(string providerId) :
        IResourceIdentityProviderSetupHandler
    {
        public List<ResourceIdentityProviderSetupRequest> Requests { get; } = [];

        public string ProviderId => providerId;

        public bool CanSetup(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, ProviderId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceIdentityProviderSetupResult> SetupAsync(
            ResourceIdentityProviderSetupRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ResourceIdentityProviderSetupResult(request.Provider.Id));
        }
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }

    private sealed class TestReadOnlyResourceProvider : IResourceProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public IReadOnlyList<Resource> GetResources() => [];
    }

    private sealed class TestImageUpdateResourceProvider : IResourceProvider, IResourceImageUpdateProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public List<string> UpdatedImages { get; } = [];

        public string? FailureMessage { get; init; }

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanUpdateImage(Resource resource) => true;

        public Task<ResourceProcedureResult> UpdateImageAsync(
            ResourceProcedureContext context,
            string image,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(FailureMessage))
            {
                throw new InvalidOperationException(FailureMessage);
            }

            UpdatedImages.Add($"{context.Resource.Id}:{image}:{restartIfRunning}:{triggeredBy}");
            return Task.FromResult(ResourceProcedureResult.Completed($"Updated {context.Resource.Id}."));
        }
    }

    private sealed class TestReplicaUpdateResourceProvider : IResourceProvider, IResourceReplicaUpdateProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public List<string> UpdatedReplicas { get; } = [];

        public string? FailureMessage { get; init; }

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanUpdateReplicas(Resource resource) => true;

        public Task<ResourceProcedureResult> UpdateReplicasAsync(
            ResourceProcedureContext context,
            int replicas,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(FailureMessage))
            {
                throw new InvalidOperationException(FailureMessage);
            }

            UpdatedReplicas.Add($"{context.Resource.Id}:{replicas}:{restartIfRunning}:{triggeredBy}");
            return Task.FromResult(ResourceProcedureResult.Completed($"Updated {context.Resource.Id}."));
        }
    }

    private sealed class TestResourceCreationProvider : IResourceCreationProvider, IResourceProcedureProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public ResourceCreationRequest? CreatedRequest { get; private set; }

        public List<string> ExecutedActions { get; } = [];

        public IReadOnlyList<Resource> GetResources() =>
            CreatedRequest is null
                ? []
                :
                [
                    CreateResource(CreatedRequest.ResourceId, ResourceState.Stopped)
                ];

        public bool CanCreate(ResourceCreationRequest request) =>
            string.Equals(request.ResourceType, "test.resource", StringComparison.OrdinalIgnoreCase);

        public Task CreateAsync(
            ResourceCreationRequest request,
            ResourceCreationContext context,
            CancellationToken cancellationToken = default)
        {
            CreatedRequest = request;
            return context.Registrations.RegisterAsync(
                Id,
                request.ResourceId,
                request.ResourceGroupId,
                cancellationToken: cancellationToken);
        }

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Id}."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add($"{context.Resource.Id}:{action.Id}");
            return Task.FromResult(ResourceProcedureResult.Completed($"Executed {action.Id}."));
        }
    }

    private sealed class TestOrchestratorServiceProcedureProvider :
        IResourceProvider,
        IResourceOrchestratorServiceProcedureProvider
    {
        public string Id => "test.orchestrator-service";

        public string DisplayName => "Test orchestrator service";

        public List<string> PreparedActions { get; } = [];

        public List<string> InstanceActions { get; } = [];

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanExecuteOrchestratorService(
            Resource resource,
            ResourceAction action) =>
            action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop or ResourceActionKind.Restart;

        public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceOrchestratorService(
                context.Resource.Id,
                "api",
                new ResourceWorkloadConfiguration(
                    ResourceWorkloadKind.ContainerImage,
                    "API",
                    Image: "example/api:latest",
                    Replicas: 3)));

        public Task PrepareOrchestratorServiceAsync(
            ResourceOrchestratorServiceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            PreparedActions.Add($"{action.Kind.ToString().ToLowerInvariant()}:{context.Service.Name}");
            return Task.CompletedTask;
        }

        public Task ExecuteOrchestratorServiceInstanceAsync(
            ResourceOrchestratorServiceInstanceContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            InstanceActions.Add(
                $"{action.Kind.ToString().ToLowerInvariant()}:{context.Instance.Name}:{context.Instance.ReplicaOrdinal}/{context.Instance.ReplicaCount}");
            return Task.CompletedTask;
        }
    }

    private sealed class TestResourceManagerStore(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<IResourceProvider> providers,
        IReadOnlyList<ResourceGroup> groups,
        IReadOnlyDictionary<string, ResourceClass> resourceTypeClasses) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => providers;

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => groups;

        public IReadOnlyList<Resource> GetAvailableResources() =>
            resources
                .Concat(providers.SelectMany(provider => provider.GetResources()))
                .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        public IReadOnlyList<Resource> GetResources() => GetAvailableResources();

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) =>
            resourceTypeClasses.GetValueOrDefault(resourceType);

        public Resource? GetResource(string id) =>
            GetResources().FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) =>
            GetResources()
                .Where(resource => string.Equals(resource.ParentResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            GetResources().Any(resource => string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestResourceRegistrationStore(IEnumerable<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> _registrations = registrations.ToDictionary(
            registration => registration.ResourceId,
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => _registrations.Values.ToArray();

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
                new ResourceRegistration(resourceId, "test", null, DateTimeOffset.UtcNow, []);
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
                new ResourceRegistration(resourceId, "test", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                DependsOn = dependsOn
            };
            return Task.CompletedTask;
        }

        public Task SetIdentityAsync(
            string resourceId,
            ResourceIdentityBinding? identity,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "test", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                Identity = identity
            };
            return Task.CompletedTask;
        }
    }

    private sealed class TestResourceGroupStore(IReadOnlyList<ResourceGroup> groups) : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => groups;

        public ResourceGroup? GetGroupForResource(string resourceId) =>
            groups.FirstOrDefault(group =>
                group.ResourceIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase));

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceGroup(Guid.NewGuid().ToString("N"), name, description, []));
    }

    private sealed class EmptyLogStore : ILogStore
    {
        public IReadOnlyList<ILogProvider> Providers => [];

        public IReadOnlyList<LogDescriptor> GetLogs() => [];

        public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) => [];

        public LogDescriptor? GetLog(string logId) => null;

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);

        public async IAsyncEnumerable<LogEntry> StreamLogAsync(
            string logId,
            int initialEntries = 50,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyTraceStore : ITraceStore
    {
        public IReadOnlyList<TraceSpan> GetSpans(
            string? resourceId = null,
            string? traceId = null,
            int maxSpans = 200,
            TelemetryScope? scope = null) => [];

        public void AddSpans(IEnumerable<TraceSpan> spans)
        {
        }
    }

    private sealed class EmptyMetricStore : IMetricStore
    {
        public IReadOnlyList<MetricPoint> GetPoints(
            string? resourceId = null,
            string? metricName = null,
            int maxPoints = 200,
            TelemetryScope? scope = null) => [];

        public void AddPoints(IEnumerable<MetricPoint> points)
        {
        }
    }

    private sealed class TestLogStore(params LogDescriptor[] logs) : ILogStore
    {
        public IReadOnlyList<ILogProvider> Providers => [];

        public IReadOnlyList<LogDescriptor> GetLogs() => logs;

        public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) =>
            logs
                .Where(log => string.Equals(log.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        public LogDescriptor? GetLog(string logId) =>
            logs.FirstOrDefault(log => string.Equals(log.Id, logId, StringComparison.OrdinalIgnoreCase));

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);

        public async IAsyncEnumerable<LogEntry> StreamLogAsync(
            string logId,
            int initialEntries = 50,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class TestTraceStore(params TraceSpan[] spans) : ITraceStore
    {
        public IReadOnlyList<TraceSpan> GetSpans(
            string? resourceId = null,
            string? traceId = null,
            int maxSpans = 200,
            TelemetryScope? scope = null) =>
            spans
                .Where(span => string.IsNullOrWhiteSpace(resourceId) ||
                    string.Equals(span.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Where(span => string.IsNullOrWhiteSpace(traceId) ||
                    string.Equals(span.TraceId, traceId, StringComparison.OrdinalIgnoreCase))
                .Take(maxSpans)
                .ToArray();

        public void AddSpans(IEnumerable<TraceSpan> spans)
        {
        }
    }

    private sealed class TestMetricStore(params MetricPoint[] points) : IMetricStore
    {
        public IReadOnlyList<MetricPoint> GetPoints(
            string? resourceId = null,
            string? metricName = null,
            int maxPoints = 200,
            TelemetryScope? scope = null) =>
            points
                .Where(point => string.IsNullOrWhiteSpace(resourceId) ||
                    string.Equals(point.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .Where(point => string.IsNullOrWhiteSpace(metricName) ||
                    string.Equals(point.Name, metricName, StringComparison.OrdinalIgnoreCase))
                .Take(maxPoints)
                .ToArray();

        public void AddPoints(IEnumerable<MetricPoint> points)
        {
        }
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class TestPermissionGrantStatusProvider(
        string providerId,
        ResourcePermissionGrantEffectivenessState state,
        string detail) : IResourcePermissionGrantStatusProvider
    {
        public string ProviderId => providerId;

        public bool CanGetStatus(ResourcePermissionGrantStatusRequest request) => true;

        public Task<ResourcePermissionGrantStatus> GetStatusAsync(
            ResourcePermissionGrantStatusRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourcePermissionGrantStatus(
                request.Grant,
                state,
                detail,
                providerId,
                DateTimeOffset.UtcNow));
    }

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class DenyAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => false;
    }

    private sealed class PermissionAuthorizationService(params string[] permissions) : ICloudShellAuthorizationService
    {
        private readonly HashSet<string> permissions = new(permissions, StringComparer.OrdinalIgnoreCase);

        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => permissions.Contains(permission);

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => HasPermission(permission);

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            HasPermission(permission);
    }

    private sealed class ResourceScopedAuthorizationService(
        params (string ResourceId, string Permission)[] grants) : ICloudShellAuthorizationService
    {
        private readonly HashSet<string> grants = new(
            grants.Select(grant => CreateKey(grant.ResourceId, grant.Permission)),
            StringComparer.OrdinalIgnoreCase);

        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            grants.Contains(CreateKey(resourceId, permission));

        private static string CreateKey(string resourceId, string permission) =>
            $"{resourceId}\n{permission}";
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

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
