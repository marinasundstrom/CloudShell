using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using DefinitionAttributeDefinition = CloudShell.ResourceDefinitions.ResourceAttributeDefinition;
using DefinitionAttributeId = CloudShell.ResourceDefinitions.ResourceAttributeId;
using DefinitionCapabilityId = CloudShell.ResourceDefinitions.ResourceCapabilityId;
using DefinitionGraphSnapshot = CloudShell.ResourceDefinitions.ResourceGraphSnapshot;
using DefinitionGraphVersion = CloudShell.ResourceDefinitions.ResourceGraphVersion;
using DefinitionResourceResolver = CloudShell.ResourceDefinitions.ResourceResolver;
using DefinitionResourceState = CloudShell.ResourceDefinitions.ResourceState;
using DefinitionJson = CloudShell.ResourceDefinitions.ResourceDefinitionJson;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceManagerStoreProjectionTests
{
    [Fact]
    public void GetResources_IncludesProviderChildrenUnderRegisteredRoot()
    {
        var root = CreateResource("root", "Root");
        var child = CreateResource("child", "Child", parentResourceId: "root");
        var store = CreateStore(
            [root, child],
            registrations: [CreateRegistration("root")]);

        var resources = store.GetResources();

        Assert.Equal(["child", "root"], resources.Select(resource => resource.Id).Order());
        Assert.Equal(["child"], store.GetChildren("root").Select(resource => resource.Id));
        Assert.True(resources.Single(resource => resource.Id == "root").IsDeclaredResource);
        Assert.True(resources.Single(resource => resource.Id == "child").IsProjectedResource);
        Assert.Equal(
            ResourceGraphMembershipKinds.Declared,
            resources.Single(resource => resource.Id == "root").ResourceAttributes[ResourceAttributeNames.ResourceGraphMembership]);
        Assert.Equal(
            ResourceGraphMembershipKinds.Projected,
            resources.Single(resource => resource.Id == "child").ResourceAttributes[ResourceAttributeNames.ResourceGraphMembership]);
    }

    [Fact]
    public void GetResources_AppliesDeclarationParentBeforeVisibilityFiltering()
    {
        var root = CreateResource("root", "Root");
        var declaredChild = CreateResource("declared-child", "Declared Child");
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared-child",
            parentResourceId: "root");
        var store = CreateStore(
            [root, declaredChild],
            registrations: [CreateRegistration("root")],
            declarations: declarations);

        var resources = store.GetResources();

        var child = Assert.Single(resources, resource => resource.Id == "declared-child");
        Assert.Equal("root", child.ParentResourceId);
        Assert.Equal(["declared-child"], store.GetChildren("root").Select(resource => resource.Id));
        Assert.True(child.IsDeclaredResource);
    }

    [Fact]
    public void GetResources_AppliesDeclarationClassAndAttributes()
    {
        var resource = CreateResource(
            "declared",
            "Declared",
            resourceClass: ResourceClass.Generic,
            attributes: new Dictionary<string, string>
            {
                ["provider.attribute"] = "provider-value",
                ["shared.attribute"] = "provider-value"
            });
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared",
            resourceClass: ResourceClass.Project,
            attributes: new Dictionary<string, string>
            {
                ["declaration.attribute"] = "declaration-value",
                ["shared.attribute"] = "declaration-value"
            });
        var store = CreateStore(
            [resource],
            registrations: [CreateRegistration("declared")],
            declarations: declarations);

        var projected = Assert.Single(store.GetResources());

        Assert.Equal(ResourceClass.Project, projected.ResourceClass);
        Assert.Equal("provider-value", projected.ResourceAttributes["provider.attribute"]);
        Assert.Equal("declaration-value", projected.ResourceAttributes["declaration.attribute"]);
        Assert.Equal("declaration-value", projected.ResourceAttributes["shared.attribute"]);
    }

    [Fact]
    public void GetResources_AppliesDeclarationPersistenceAttributes()
    {
        var startup = CreateResource("startup", "Startup");
        var persisted = CreateResource("persisted", "Persisted");
        var declarations = new ResourceDeclarationStore();
        var builder = new TestCloudShellBuilder();
        declarations.Declare(builder, "test", "startup");
        declarations.Declare(builder, "test", "persisted");
        declarations.Persist("persisted", overwrite: true);
        var store = CreateStore(
            [startup, persisted],
            registrations: [CreateRegistration("startup"), CreateRegistration("persisted")],
            declarations: declarations);

        var resources = store.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(
            ResourceDeclarationPersistence.Transient.ToString(),
            resources["startup"].ResourceAttributes[ResourceAttributeNames.DeclarationPersistence]);
        Assert.Equal(
            "false",
            resources["startup"].ResourceAttributes[ResourceAttributeNames.DeclarationOverwritePersistedState]);
        Assert.Equal(
            ResourceDeclarationPersistence.Persisted.ToString(),
            resources["persisted"].ResourceAttributes[ResourceAttributeNames.DeclarationPersistence]);
        Assert.Equal(
            "true",
            resources["persisted"].ResourceAttributes[ResourceAttributeNames.DeclarationOverwritePersistedState]);
    }

    [Fact]
    public void GetResources_AppliesDeclarationIdentity()
    {
        var resource = CreateResource("declared", "Declared");
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(new TestCloudShellBuilder(), "test", "declared");
        declarations.SetIdentity(
            "declared",
            new ResourceIdentityBinding(
                "development",
                "application:api",
                ["database.read"],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["resource"] = "api"
                },
                Name: "api-service"));
        var store = CreateStore(
            [resource],
            registrations: [CreateRegistration("declared")],
            declarations: declarations,
            identityProviders: new ResourceIdentityProviderCatalog(
                [new("development", "Development identity", ResourceIdentityProviderKind.BuiltIn)]));

        var projected = Assert.Single(store.GetResources());
        var diagnostics = store.GetResourceModelDiagnostics();

        Assert.NotNull(projected.IdentityBinding);
        Assert.Equal("api-service", projected.IdentityBinding.Name);
        Assert.Equal("development", projected.IdentityBinding.ProviderId);
        Assert.Equal("application:api", projected.IdentityBinding.Subject);
        Assert.Equal(["database.read"], projected.IdentityBinding.IdentityScopes);
        Assert.Equal("api", projected.IdentityBinding.IdentityClaims["resource"]);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetResources_AppliesRegistrationIdentityWhenDeclarationDoesNotDeclareIdentity()
    {
        var resource = CreateResource("registered", "Registered");
        var registration = CreateRegistration("registered") with
        {
            Identity = ResourceIdentityBinding.RequireIdentity() with { Name = "registered-service" }
        };
        var store = CreateStore(
            [resource],
            registrations: [registration],
            identityProviders: new ResourceIdentityProviderCatalog(
                [new("identity:dev", "Development identity", ResourceIdentityProviderKind.BuiltIn)]));

        var projected = Assert.Single(store.GetResources());
        var diagnostics = store.GetResourceModelDiagnostics();

        Assert.NotNull(projected.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, projected.IdentityBinding.Kind);
        Assert.Equal("registered-service", projected.IdentityBinding.Name);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetResources_KeepsDeclarationIdentityBeforeRegistrationIdentity()
    {
        var resource = CreateResource("declared", "Declared");
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared",
            identity: new ResourceIdentityBinding("identity:dev", Name: "declared-service"));
        var registration = CreateRegistration("declared") with
        {
            Identity = ResourceIdentityBinding.RequireIdentity() with { Name = "registration-service" }
        };
        var store = CreateStore(
            [resource],
            registrations: [registration],
            declarations: declarations,
            identityProviders: new ResourceIdentityProviderCatalog(
                [new("identity:dev", "Development identity", ResourceIdentityProviderKind.BuiltIn)]));

        var projected = Assert.Single(store.GetResources());

        Assert.NotNull(projected.IdentityBinding);
        Assert.Equal("declared-service", projected.IdentityBinding.Name);
        Assert.Equal("identity:dev", projected.IdentityBinding.ProviderId);
    }

    [Fact]
    public void GetResourceModelDiagnostics_ReportsDeclarationIdentityWithoutProvider()
    {
        var resource = CreateResource("declared", "Declared");
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(new TestCloudShellBuilder(), "test", "declared");
        declarations.SetIdentity(
            "declared",
            ResourceIdentityBinding.RequireIdentity(["database.read"]) with
            {
                Name = "api-service"
            });
        var store = CreateStore(
            [resource],
            registrations: [CreateRegistration("declared")],
            declarations: declarations);

        var projected = Assert.Single(store.GetResources());
        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.NotNull(projected.IdentityBinding);
        Assert.Equal(ResourceIdentityBindingKind.Required, projected.IdentityBinding.Kind);
        Assert.Equal("api-service", projected.IdentityBinding.Name);
        Assert.Equal(ResourceModelValidation.ResourceIdentityProviderUnresolvedCode, diagnostic.Code);
        Assert.Equal("declared", diagnostic.ResourceId);
        Assert.Contains("No default resource identity provider", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetAvailableResources_NormalizesKnownResourceTypeClassMismatch()
    {
        var resource = CreateResource(
            "mismatch",
            "Mismatch",
            resourceClass: ResourceClass.Container);
        var store = CreateStore(
            [resource],
            extensionRegistry: CreateExtensionRegistry(ResourceClass.Project));

        var projected = Assert.Single(store.GetAvailableResources());
        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Equal(ResourceClass.Project, projected.ResourceClass);
        Assert.Equal(ResourceModelValidation.ResourceClassMismatchCode, diagnostic.Code);
        Assert.Equal("mismatch", diagnostic.ResourceId);
        Assert.Equal("test.resource", diagnostic.ResourceType);
        Assert.Equal(ResourceClass.Project, diagnostic.ExpectedResourceClass);
        Assert.Equal(ResourceClass.Container, diagnostic.ActualResourceClass);
    }

    [Fact]
    public void GetAvailableResources_ProjectsLivenessCapabilityFromLivenessHealthCheck()
    {
        var resource = CreateResource(
            "api",
            "API",
            healthChecks:
            [
                new ResourceHealthCheck(
                    ResourceProbeSource.ForHttp("/alive", "http"),
                    ResourceProbeType.Liveness,
                    "liveness")
            ]);
        var store = CreateStore([resource]);

        var projected = Assert.Single(store.GetAvailableResources());

        Assert.True(projected.SupportsLiveness);
        Assert.False(projected.SupportsRecovery);
    }

    [Fact]
    public void GetAvailableResources_ProjectsRecoveryCapabilityWhenLivenessAndRestartAreSupported()
    {
        var resource = CreateResource(
            "api",
            "API",
            actions: [ResourceAction.Restart],
            healthChecks:
            [
                new ResourceHealthCheck(
                    ResourceProbeSource.ForHttp("/alive", "http"),
                    ResourceProbeType.Liveness,
                    "liveness")
            ]);
        var store = CreateStore([resource]);

        var projected = Assert.Single(store.GetAvailableResources());
        var recovery = Assert.Single(
            projected.ResourceCapabilities,
            capability => string.Equals(capability.Id, ResourceCapabilityIds.Recovery, StringComparison.OrdinalIgnoreCase));

        Assert.True(projected.SupportsLiveness);
        Assert.True(projected.SupportsRecovery);
        Assert.Equal(
            ResourceCapabilityIds.Liveness,
            recovery.CapabilityMetadata[ResourceCapabilityMetadataNames.RequiresCapability]);
    }

    [Fact]
    public void GetAvailableResources_ProjectsRecoveryCapabilityWhenLivenessAndStartAreSupported()
    {
        var resource = CreateResource(
            "api",
            "API",
            actions: [ResourceAction.Start],
            healthChecks:
            [
                new ResourceHealthCheck(
                    ResourceProbeSource.ForHttp("/alive", "http"),
                    ResourceProbeType.Liveness,
                    "liveness")
            ]);
        var store = CreateStore([resource]);

        var projected = Assert.Single(store.GetAvailableResources());

        Assert.True(projected.SupportsLiveness);
        Assert.True(projected.SupportsRecovery);
    }

    [Fact]
    public void GetAvailableResources_DoesNotProjectRecoveryCapabilityFromGenericHealthCheck()
    {
        var resource = CreateResource(
            "api",
            "API",
            actions: [ResourceAction.Restart],
            healthChecks:
            [
                new ResourceHealthCheck(
                    ResourceProbeSource.ForHttp("/healthz", "http"),
                    ResourceProbeType.Health,
                    "health")
            ]);
        var store = CreateStore([resource]);

        var projected = Assert.Single(store.GetAvailableResources());

        Assert.False(projected.SupportsLiveness);
        Assert.False(projected.SupportsRecovery);
    }

    [Fact]
    public void GetResources_NormalizesDeclarationClassMismatchForKnownResourceType()
    {
        var resource = CreateResource(
            "declared",
            "Declared",
            resourceClass: ResourceClass.Project);
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "test",
            "declared",
            resourceClass: ResourceClass.Container);
        var store = CreateStore(
            [resource],
            registrations: [CreateRegistration("declared")],
            declarations: declarations,
            extensionRegistry: CreateExtensionRegistry(ResourceClass.Project));

        var projected = Assert.Single(store.GetResources());
        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Equal(ResourceClass.Project, projected.ResourceClass);
        Assert.Equal(ResourceModelValidation.ResourceClassMismatchCode, diagnostic.Code);
        Assert.Equal("declaration metadata", diagnostic.Source);
        Assert.Equal(ResourceClass.Project, diagnostic.ExpectedResourceClass);
        Assert.Equal(ResourceClass.Container, diagnostic.ActualResourceClass);
    }

    [Fact]
    public void GetResourceModelDiagnostics_ReportsUnregisteredIdentityProvider()
    {
        var resource = CreateResource(
            "identity",
            "Identity",
            identity: new ResourceIdentityBinding("identity:missing"));
        var store = CreateStore(
            [resource],
            identityProviders: new ResourceIdentityProviderCatalog(
                [new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc)]));

        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Equal(ResourceModelValidation.ResourceIdentityProviderUnresolvedCode, diagnostic.Code);
        Assert.Equal("identity", diagnostic.ResourceId);
        Assert.Equal("test.resource", diagnostic.ResourceType);
        Assert.Equal(ResourceClass.Generic, diagnostic.ExpectedResourceClass);
        Assert.Equal(ResourceClass.Generic, diagnostic.ActualResourceClass);
        Assert.Equal("identity binding", diagnostic.Source);
        Assert.Contains("identity:missing", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetResourceModelDiagnostics_ReportsMissingDefaultIdentityProvider()
    {
        var resource = CreateResource(
            "identity",
            "Identity",
            identity: ResourceIdentityBinding.RequireIdentity());
        var store = CreateStore(
            [resource],
            identityProviders: new ResourceIdentityProviderCatalog(
                [
                    new("identity:dev", "Development identity", ResourceIdentityProviderKind.Oidc),
                    new("identity:entra", "Microsoft Entra ID", ResourceIdentityProviderKind.Oidc)
                ]));

        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Equal(ResourceModelValidation.ResourceIdentityProviderUnresolvedCode, diagnostic.Code);
        Assert.Contains("No default resource identity provider", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetResourceModelDiagnostics_UsesProgrammaticIdentityProviderDefault()
    {
        var resource = CreateResource(
            "identity",
            "Identity",
            identity: ResourceIdentityBinding.RequireIdentity());
        var declarations = new ResourceDeclarationStore();
        declarations.AddIdentityProvider(
            new ResourceIdentityProviderDefinition(
                "identity:dev",
                "Development identity",
                ResourceIdentityProviderKind.BuiltIn),
            useAsDefault: true);
        var store = CreateStore(
            [resource],
            declarations: declarations);

        Assert.Empty(store.GetResourceModelDiagnostics());
    }

    [Fact]
    public void GetResources_ProjectsIdentityProvisioningDeclarationResource()
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            ResourceIdentityProvisioningResources.ProviderId,
            "identity-provisioning:keycloak",
            resourceClass: ResourceClass.Infrastructure,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = "identity-provisioning",
                ["identity.provider"] = "Keycloak"
            });
        var registration = new ResourceRegistration(
            "identity-provisioning:keycloak",
            ResourceIdentityProvisioningResources.ProviderId,
            null,
            DateTimeOffset.UtcNow,
            []);
        var store = new ResourceManagerStore(
            [
                new ResourceIdentityProvisioningResourceProvider(
                    declarations,
                    new ResourceIdentityProviderSetupService(
                        declarations,
                        new ResourceIdentityProviderCatalog(),
                        []))
            ],
            new TestResourceGroupStore([]),
            new TestResourceRegistrationStore([registration]),
            declarations,
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var resource = Assert.Single(store.GetResources());

        Assert.Equal("identity-provisioning:keycloak", resource.Id);
        Assert.Equal("keycloak", resource.Name);
        Assert.Equal("Keycloak Identity Provisioning", resource.DisplayName);
        Assert.Equal(ResourceIdentityProvisioningResources.ResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Infrastructure, resource.ResourceClass);
        Assert.Null(resource.State);
        Assert.Equal(
            "identity-provisioning",
            resource.ResourceAttributes[ResourceAttributeNames.InfrastructureKind]);
        Assert.Equal("Keycloak", resource.ResourceAttributes["identity.provider"]);
        Assert.NotNull(resource.Actions);
        var action = Assert.Single(resource.Actions);
        Assert.Equal(ResourceIdentityProvisioningResourceProvider.SetupIdentityProviderActionId, action.Id);
        Assert.Equal("Set up identity provider", action.DisplayName);
        Assert.Equal(
            ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities,
            action.RequiredPermission);
    }

    [Fact]
    public void GetResources_ComposesResourceModelBridgeProviderProjection()
    {
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new DefinitionGraphSnapshot(DefinitionGraphVersion.Initial, [CreateResourceModelExecutableState()]),
            CreateResourceModelResolver(),
            projectionOptions: new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));
        var store = new ResourceManagerStore(
            [provider],
            new TestResourceGroupStore([]),
            new TestResourceRegistrationStore(
            [
                new(
                    "application.executable:api",
                    ExecutableApplicationResourceTypeProvider.ProviderId,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]),
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var resource = Assert.Single(store.GetResources());

        Assert.Equal("application.executable:api", resource.Id);
        Assert.Equal("API", resource.DisplayName);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, resource.Provider);
        Assert.Equal(ResourceClass.Executable, resource.ResourceClass);
        Assert.Equal(["storage:data"], resource.DependsOn);
        Assert.Equal("dotnet", resource.ResourceAttributes[ResourceAttributeNames.ExecutablePath]);
        Assert.True(resource.IsDeclaredResource);
        Assert.Contains(resource.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(resource.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void GetGroupForResource_InheritsGroupFromRegisteredParent()
    {
        var group = new ResourceGroup("group-one", "Group One", "Test group", []);
        var root = CreateResource("root", "Root");
        var child = CreateResource("child", "Child", parentResourceId: "root");
        var store = CreateStore(
            [root, child],
            groups: [group],
            registrations: [CreateRegistration("root", group.Id)]);

        var inheritedGroup = store.GetGroupForResource("child");

        Assert.NotNull(inheritedGroup);
        Assert.Equal(group.Id, inheritedGroup.Id);
    }

    [Fact]
    public void GetResources_DoesNotLoopWhenProviderParentGraphCycles()
    {
        var first = CreateResource("first", "First", parentResourceId: "second");
        var second = CreateResource("second", "Second", parentResourceId: "first");
        var store = CreateStore(
            [first, second],
            registrations: [CreateRegistration("first")]);

        var resources = store.GetResources();

        Assert.Equal(["first", "second"], resources.Select(resource => resource.Id).Order());
    }

    private static ResourceManagerStore CreateStore(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<ResourceGroup>? groups = null,
        IReadOnlyList<ResourceRegistration>? registrations = null,
        ResourceDeclarationStore? declarations = null,
        CloudShellExtensionRegistry? extensionRegistry = null,
        ResourceIdentityProviderCatalog? identityProviders = null)
    {
        var groupStore = new TestResourceGroupStore(groups ?? []);
        var registrationStore = new TestResourceRegistrationStore(registrations ?? []);
        return new ResourceManagerStore(
            [new TestResourceProvider(resources)],
            groupStore,
            registrationStore,
            declarations ?? new ResourceDeclarationStore(),
            identityProviders ?? new ResourceIdentityProviderCatalog(),
            extensionRegistry ?? new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
    }

    private static DefinitionResourceResolver CreateResourceModelResolver() =>
        new(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<DefinitionAttributeId, DefinitionAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
                    })
            ],
            [
                new ExecutableApplicationResourceTypeProvider().TypeDefinition
            ]);

    private static DefinitionResourceState CreateResourceModelExecutableState() =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: "API",
            DependsOn: ["storage:data"],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    DefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<DefinitionCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    DefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("storage:data", "App_Data")
                    ]))
            });

    private static CloudShellExtensionRegistry CreateExtensionRegistry(ResourceClass resourceClass)
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .AddExtension(new TestResourceTypeExtension(resourceClass));

        return Assert.IsType<CloudShellExtensionRegistry>(
            Assert.Single(services, descriptor =>
                descriptor.ServiceType == typeof(CloudShellExtensionRegistry)).ImplementationInstance);
    }

    private static Resource CreateResource(
        string id,
        string name,
        string? parentResourceId = null,
        ResourceClass resourceClass = ResourceClass.Generic,
        IReadOnlyDictionary<string, string>? attributes = null,
        ResourceIdentityBinding? identity = null,
        IReadOnlyList<ResourceAction>? actions = null,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        IReadOnlyList<ResourceCapability>? capabilities = null) =>
        new(
            id,
            name,
            "Test",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: parentResourceId,
            TypeId: "test.resource",
            Actions: actions,
            HealthChecks: healthChecks,
            ResourceClass: resourceClass,
            Attributes: attributes,
            Capabilities: capabilities,
            Identity: identity);

    private static ResourceRegistration CreateRegistration(
        string resourceId,
        string? resourceGroupId = null) =>
        new(
            resourceId,
            "test",
            resourceGroupId,
            DateTimeOffset.UtcNow,
            []);

    private sealed class TestResourceProvider(IReadOnlyList<Resource> resources) : IResourceProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public IReadOnlyList<Resource> GetResources() => resources;
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
            throw new NotSupportedException();
    }

    private sealed class TestResourceRegistrationStore(IReadOnlyList<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }

    private sealed class TestRegistrationComponent;

    private sealed class TestResourceTypeExtension(ResourceClass resourceClass) : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "test.resource-types",
            "Test resource types",
            "Test resource type contribution.",
            "1.0.0",
            ["resource-type.test.resource"],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddResourceType<TestRegistrationComponent>(
                "test.resource",
                "Test resource",
                "Test resource.",
                "test",
                1,
                resourceClass: resourceClass);
        }
    }
}
