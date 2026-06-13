using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.ControlPlane.Tests;

public sealed class InProcessControlPlaneResourceStateTests
{
    public static TheoryData<ResourceState, string[]> StateResourceActionCapabilities =>
        new()
        {
            { ResourceState.Running, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Starting, [ResourceActionIds.Stop, ResourceActionIds.Restart] },
            { ResourceState.Paused, [ResourceActionIds.Run, ResourceActionIds.Stop] },
            { ResourceState.Degraded, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Stopped, [ResourceActionIds.Run] },
            { ResourceState.Unknown, [ResourceActionIds.Run] }
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
                ResourceAction.Run,
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
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Run));
        Assert.Equal(
            "Container host 'docker:missing' is not registered.",
            capability.GetActionUnavailableReason(ResourceActionIds.Run));
        Assert.DoesNotContain(ResourceActionIds.Run, capability.ExecutableActionIds);
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
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Run));
        Assert.Equal(
            "Container host 'docker:local' does not advertise required capability 'container.build'.",
            capability.GetActionUnavailableReason(ResourceActionIds.Run));
    }

    [Fact]
    public async Task GetResourceOperationCapabilities_ReturnsProviderUnavailableReason()
    {
        var resource = CreateResource("target", ResourceState.Stopped);
        var provider = new TestResourceProvider();
        var availability = new TestActionAvailabilityProvider(
            resource.Id,
            ResourceActionIds.Run,
            "Reference target missing.");
        var controlPlane = CreateControlPlane(
            [resource],
            provider,
            actionAvailabilityProviders: [availability]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.False(capability.CanExecuteAction(ResourceActionIds.Run));
        Assert.Equal(availability.Reason, capability.GetActionUnavailableReason(ResourceActionIds.Run));
        Assert.DoesNotContain(ResourceActionIds.Run, capability.ExecutableActionIds);
        Assert.Empty(provider.ExecutedActions);
    }

    [Theory]
    [InlineData(ResourceState.Running, ResourceActionIds.Run)]
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
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Run)));

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
            ResourceActionIds.Run,
            "Reference target missing.");
        var controlPlane = CreateControlPlane(
            [resource],
            provider,
            actionAvailabilityProviders: [availability]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Run)));

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
                    identity,
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

    [Theory]
    [InlineData(ResourceState.Starting, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Paused, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Unknown, ResourceActionIds.Run)]
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

        var notification = Assert.Single(notifications);
        Assert.Equal(ResourceChangeKind.ResourceActionExecuted, notification.Kind);
        Assert.Equal("target", notification.ResourceId);
        Assert.Equal(ResourceActionIds.Stop, notification.ActionId);
        Assert.Contains("target", notification.Resources);
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
    public async Task UpdateResourceImageAsync_DispatchesToImageUpdateProvider()
    {
        var provider = new TestImageUpdateResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)], provider);

        var result = await controlPlane.UpdateResourceImageAsync(
            new UpdateResourceImageCommand(
                "target",
                "example/api:20260608",
                RestartIfRunning: false,
                TriggeredBy: "build-server"));

        Assert.Equal("Updated target.", result.Message);
        Assert.Equal(["target:example/api:20260608:False:build-server"], provider.UpdatedImages);
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
    public async Task UpdateResourceReplicasAsync_DispatchesToReplicaUpdateProvider()
    {
        var provider = new TestReplicaUpdateResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)], provider);

        var result = await controlPlane.UpdateResourceReplicasAsync(
            new UpdateResourceReplicasCommand(
                "target",
                3,
                RestartIfRunning: false,
                TriggeredBy: "load-balancer"));

        Assert.Equal("Updated target.", result.Message);
        Assert.Equal(["target:3:False:load-balancer"], provider.UpdatedReplicas);
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
        Assert.Equal(["target:run"], provider.ExecutedActions);
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
            ResourceAction.Run);

        Assert.Equal("Started application:api.", result.Message);
        Assert.Equal(["run:api"], provider.PreparedActions);
        Assert.Equal(
            [
                "run:api-replica-1:1/3",
                "run:api-replica-2:2/3",
                "run:api-replica-3:3/3"
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

    private static IResourceManager CreateControlPlane(
        IReadOnlyList<Resource> resources,
        IResourceProvider? provider = null,
        IReadOnlyList<ResourceGroup>? groups = null,
        ICloudShellAuthorizationService? authorization = null,
        IReadOnlyDictionary<string, ResourceClass>? resourceTypeClasses = null,
        IReadOnlyList<ResourcePermissionGrant>? permissionGrants = null,
        IReadOnlyList<ResourceIdentityProviderDefinition>? identityProviders = null,
        IReadOnlyList<IResourceIdentityProvisioner>? identityProvisioners = null,
        IReadOnlyList<IResourceOrchestrationDescriptorProvider>? descriptorProviders = null,
        IReadOnlyList<IContainerHostProvider>? containerHostProviders = null,
        IReadOnlyList<IResourceActionAvailabilityProvider>? actionAvailabilityProviders = null,
        Action<ResourceDeclarationStore>? configureDeclarations = null)
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
            new ResourceIdentityProviderCatalog(identityProviders ?? []),
            identityProvisioners ?? []);
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            descriptorProviders ?? [],
            resourceManager,
            registrations,
            declarations,
            CreateSelectionStore(),
            containerHostProviders ?? [],
            actionAvailabilityProviders: actionAvailabilityProviders ?? []);

        return new InProcessControlPlane(
            resourceManager,
            resourceGroups,
            registrations,
            declarations,
            orchestration,
            identityProvisioning,
            templates,
            new EmptyLogStore(),
            new EmptyTraceStore(),
            authorization ?? new AllowAllAuthorizationService());
    }

    private static Resource CreateResource(
        string id,
        ResourceState state,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<ResourceAction>? actions = null) =>
        new(
            id,
            id,
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
                ResourceAction.Run,
                ResourceAction.Stop,
                ResourceAction.Pause,
                ResourceAction.Restart
            ]);

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

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanUpdateImage(Resource resource) => true;

        public Task<ResourceProcedureResult> UpdateImageAsync(
            ResourceProcedureContext context,
            string image,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
            UpdatedImages.Add($"{context.Resource.Id}:{image}:{restartIfRunning}:{triggeredBy}");
            return Task.FromResult(ResourceProcedureResult.Completed($"Updated {context.Resource.Id}."));
        }
    }

    private sealed class TestReplicaUpdateResourceProvider : IResourceProvider, IResourceReplicaUpdateProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public List<string> UpdatedReplicas { get; } = [];

        public IReadOnlyList<Resource> GetResources() => [];

        public bool CanUpdateReplicas(Resource resource) => true;

        public Task<ResourceProcedureResult> UpdateReplicasAsync(
            ResourceProcedureContext context,
            int replicas,
            bool restartIfRunning,
            string? triggeredBy = null,
            CancellationToken cancellationToken = default)
        {
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
            action.Kind is ResourceActionKind.Run or ResourceActionKind.Stop or ResourceActionKind.Restart;

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
            int maxSpans = 200) => [];

        public void AddSpans(IEnumerable<TraceSpan> spans)
        {
        }
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
