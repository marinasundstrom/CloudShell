using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceActionTests
{
    [Fact]
    public void Resource_ExposesEmptyActionsWhenNoneAreDefined()
    {
        var resource = CreateResource();

        Assert.Empty(resource.ResourceActions);
        Assert.Equal(ResourceClass.Generic, resource.ResourceClass);
        Assert.Empty(resource.ResourceAttributes);
    }

    [Fact]
    public void Resource_ExposesProviderDefinedActions()
    {
        var resource = CreateResource([ResourceAction.Start, new ResourceAction("custom", "Custom")]);

        Assert.Collection(
            resource.ResourceActions,
            action => Assert.Equal(ResourceActionKind.Start, action.Kind),
            action =>
            {
                Assert.Equal("custom", action.Id);
                Assert.Equal(ResourceActionKind.Custom, action.Kind);
            });
    }

    [Fact]
    public void Resource_ProvidesCaseInsensitiveActionLookup()
    {
        var resource = CreateResource([ResourceAction.Stop, new ResourceAction("custom", "Custom")]);

        Assert.True(resource.HasAction(ResourceActionIds.Stop));
        Assert.True(resource.HasAction("STOP"));
        Assert.NotNull(resource.StopAction);
        Assert.Null(resource.StartAction);
        Assert.Equal("custom", resource.GetAction("CUSTOM")?.Id);
    }

    [Fact]
    public void Resource_ExposesProjectedIdentityBinding()
    {
        var identity = new ResourceIdentityBinding(
            "identity:entra",
            "application:api",
            ["api://cloudshell-control-plane/.default"],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["appRole"] = "Api"
            },
            Name: "api-service");
        var resource = CreateResource() with { Identity = identity };
        var binding = resource.IdentityBinding;

        Assert.NotNull(binding);
        Assert.Same(identity, binding);
        Assert.Equal(ResourceIdentityBindingKind.Provider, binding.Kind);
        Assert.True(binding.HasResolvedProvider);
        Assert.Equal("api-service", binding.Name);
        Assert.Equal("identity:entra", binding.ProviderId);
        Assert.Equal(["api://cloudshell-control-plane/.default"], binding.IdentityScopes);
        Assert.Equal("Api", binding.IdentityClaims["appRole"]);
    }

    [Fact]
    public void ResourceIdentityBinding_CanRequireIdentityWithoutProviderBinding()
    {
        var identity = ResourceIdentityBinding.RequireIdentity(["db.read"]);

        Assert.Equal(ResourceIdentityBindingKind.Required, identity.Kind);
        Assert.False(identity.HasResolvedProvider);
        Assert.Null(identity.ProviderId);
        Assert.Equal(["db.read"], identity.IdentityScopes);
        Assert.Empty(identity.IdentityClaims);
    }

    [Fact]
    public void ResourceIdentityBinding_RequiresProviderForProviderBinding()
    {
        Assert.ThrowsAny<ArgumentException>(() => new ResourceIdentityBinding(null));
    }

    [Fact]
    public void ResourceIdentityProviderDefinition_NormalizesEmptySettings()
    {
        var provider = new ResourceIdentityProviderDefinition(
            "identity:dev",
            "Development identity",
            ResourceIdentityProviderKind.Oidc);

        Assert.Empty(provider.ProviderSettings);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_ResolvesConcreteProviderBinding()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [
                new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc),
                new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)
            ],
            "identity:entra");
        var binding = new ResourceIdentityBinding("IDENTITY:DEV");

        var resolution = catalog.Resolve(binding);

        Assert.True(resolution.IsResolved);
        Assert.Same(binding, resolution.Binding);
        Assert.Equal("identity:dev", resolution.Provider?.Id);
        Assert.Null(resolution.Reason);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_ResolvesRequiredIdentityToDefaultProvider()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [
                new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc),
                new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)
            ],
            "identity:entra");

        var resolution = catalog.Resolve(ResourceIdentityBinding.RequireIdentity(["api.read"]));

        Assert.True(resolution.IsResolved);
        Assert.Equal("identity:entra", resolution.Provider?.Id);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_UsesSingleProviderAsDefault()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc)]);

        Assert.Equal("identity:dev", catalog.DefaultProviderId);
        Assert.Equal("identity:dev", catalog.DefaultProvider?.Id);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_ReportsUnresolvedProvider()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc)]);

        var resolution = catalog.Resolve(new ResourceIdentityBinding("identity:missing"));

        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.Provider);
        Assert.Equal(
            "Resource identity provider 'identity:missing' is not registered.",
            resolution.Reason);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_ReportsMissingDefaultProvider()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [
                new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc),
                new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)
            ]);

        var resolution = catalog.Resolve(ResourceIdentityBinding.RequireIdentity());

        Assert.False(resolution.IsResolved);
        Assert.Equal("No default resource identity provider is registered.", resolution.Reason);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_RejectsUnknownDefaultProvider()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ResourceIdentityProviderCatalog(
                [new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc)],
                "identity:missing"));

        Assert.Equal(
            "Default resource identity provider 'identity:missing' is not registered.",
            exception.Message);
    }

    [Fact]
    public void ResourceIdentityProviderCatalog_MergesProgrammaticProvidersAndDefault()
    {
        var catalog = new ResourceIdentityProviderCatalog(
            [new("identity:configured", "Configured", ResourceIdentityProviderKind.Oidc)]);
        var merged = catalog.Merge(
            [new("identity:dev", "Development identity", ResourceIdentityProviderKind.BuiltIn)],
            "identity:dev");

        Assert.Equal("identity:dev", merged.DefaultProviderId);
        Assert.NotNull(merged.GetProvider("identity:configured"));
        Assert.NotNull(merged.GetProvider("identity:dev"));
        Assert.Equal(
            "identity:dev",
            merged.Resolve(ResourceIdentityBinding.RequireIdentity()).Provider?.Id);
    }

    [Fact]
    public void ResourceOperationCapabilities_ProvidesCaseInsensitiveActionLookup()
    {
        var capabilities = new ResourceOperationCapabilities(
            "sample:resource",
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ResourceActionIds.Start,
                ResourceActionIds.Restart
            },
            [
                new ResourceActionCapability(ResourceActionIds.Start, true),
                new ResourceActionCapability(ResourceActionIds.Stop, false, "Cannot stop while stopped."),
                new ResourceActionCapability(ResourceActionIds.Restart, true)
            ]);

        Assert.True(capabilities.CanStart);
        Assert.False(capabilities.CanStop);
        Assert.True(capabilities.CanExecuteAction("RESTART"));
        Assert.Equal("Cannot stop while stopped.", capabilities.GetActionUnavailableReason("STOP"));

        var legacyCapabilities = new ResourceOperationCapabilities(
            "sample:resource",
            true,
            true,
            new HashSet<string> { ResourceActionIds.Restart });

        Assert.True(legacyCapabilities.CanExecuteAction("RESTART"));
    }

    [Fact]
    public void StandardActions_MarkDisruptiveCommandsForConfirmation()
    {
        Assert.Equal(ResourceActionIds.Start, ResourceAction.Start.Id);
        Assert.Equal(ResourceActionIds.Stop, ResourceAction.Stop.Id);
        Assert.Equal(ResourceActionIds.Pause, ResourceAction.Pause.Id);
        Assert.Equal(ResourceActionIds.Restart, ResourceAction.Restart.Id);
        Assert.False(ResourceAction.Start.RequiresConfirmation);
        Assert.True(ResourceAction.Stop.RequiresConfirmation);
        Assert.True(ResourceAction.Pause.RequiresConfirmation);
        Assert.True(ResourceAction.Restart.RequiresConfirmation);
    }

    [Fact]
    public void StandardActions_DefinePresentationPolicySeparatelyFromActionKind()
    {
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Start.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Stop.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Pause.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Overflow, ResourceAction.Restart.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionIcon.Restart, ResourceAction.Restart.EffectivePresentation.Icon);
    }

    [Fact]
    public void ResourceActionPermissions_MapStandardActionsToLifecyclePermission()
    {
        Assert.Equal(
            CommonResourceOperationPermissions.LifecycleAction,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Start));
        Assert.Equal(
            CommonResourceOperationPermissions.LifecycleAction,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Stop));
        Assert.Equal(
            CommonResourceOperationPermissions.LifecycleAction,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Pause));
        Assert.Equal(
            CommonResourceOperationPermissions.LifecycleAction,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Restart));
    }

    [Fact]
    public void ResourceActionPermissions_MapCustomActionsToGenericExecutePermission()
    {
        var action = new ResourceAction("applyLoadBalancerConfiguration", "Apply");

        Assert.Equal(
            CommonResourceOperationPermissions.ExecuteCustomAction,
            ResourceActionPermissions.GetRequiredPermission(action));
    }

    [Fact]
    public void ResourceActionPermissions_UseCustomActionPermissionWhenDeclared()
    {
        var action = new ResourceAction(
            "applyLoadBalancerConfiguration",
            "Apply",
            RequiredPermission: LoadBalancerResourceOperationPermissions.ApplyConfiguration);

        Assert.Equal(
            LoadBalancerResourceOperationPermissions.ApplyConfiguration,
            ResourceActionPermissions.GetRequiredPermission(action));
    }

    [Fact]
    public void CloudShellPermissions_KeepCompatibilityAliasesForResourceOperationPermissions()
    {
        Assert.Equal(
            CommonResourceOperationPermissions.LifecycleAction,
            CloudShellPermissions.Resources.Actions.Lifecycle);
        Assert.Equal(
            CommonResourceOperationPermissions.ExecuteCustomAction,
            CloudShellPermissions.Resources.Actions.Execute);
        Assert.Equal(
            NetworkResourceOperationPermissions.ReconcileEndpointMappings,
            CloudShellPermissions.Network.Actions.ReconcileEndpointMappings);
        Assert.Equal(
            LoadBalancerResourceOperationPermissions.ApplyConfiguration,
            CloudShellPermissions.Network.Actions.ApplyLoadBalancerConfiguration);
        Assert.Equal(
            RabbitMQResourceOperationPermissions.Publish,
            CloudShellPermissions.Messaging.RabbitMQ.Actions.Publish);
        Assert.Equal(
            RabbitMQResourceOperationPermissions.Consume,
            CloudShellPermissions.Messaging.RabbitMQ.Actions.Consume);
        Assert.Equal(
            RabbitMQResourceOperationPermissions.Configure,
            CloudShellPermissions.Messaging.RabbitMQ.Actions.Configure);
        Assert.Equal(
            RabbitMQResourceOperationPermissions.ReconcileAccess,
            CloudShellPermissions.Messaging.RabbitMQ.Actions.ReconcileAccess);
    }

    private static Resource CreateResource(IReadOnlyList<ResourceAction>? actions = null) =>
        new(
            "sample:resource",
            "Sample",
            "Sample Resource",
            "Sample",
            "local",
            ResourceState.Running,
            [],
            "1.0.0",
            DateTimeOffset.UnixEpoch,
            [],
            Actions: actions);
}
