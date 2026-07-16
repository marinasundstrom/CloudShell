using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using DefinitionAttributeDefinition = CloudShell.ResourceModel.ResourceAttributeDefinition;
using DefinitionAttributeId = CloudShell.ResourceModel.ResourceAttributeId;
using DefinitionAttributeValueType = CloudShell.ResourceModel.ResourceAttributeValueType;
using DefinitionDiagnosticCodes = CloudShell.ResourceModel.ResourceDefinitionDiagnosticCodes;
using DefinitionCapabilityId = CloudShell.ResourceModel.ResourceCapabilityId;
using DefinitionGraphSnapshot = CloudShell.ResourceModel.ResourceGraphSnapshot;
using DefinitionGraphVersion = CloudShell.ResourceModel.ResourceGraphVersion;
using DefinitionResourceRecord = CloudShell.ResourceModel.ResourceRecord;
using DefinitionResourceReference = CloudShell.ResourceModel.ResourceReference;
using DefinitionResourceTypeProvider = CloudShell.ResourceModel.IResourceTypeProvider;
using DefinitionResourceResolver = CloudShell.ResourceModel.ResourceResolver;
using DefinitionResourceState = CloudShell.ResourceModel.ResourceState;
using DefinitionJson = CloudShell.ResourceModel.ResourceDefinitionJson;

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
        Assert.Equal(
            "dotnet",
            resource.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.True(resource.IsDeclaredResource);
        Assert.Contains(resource.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(resource.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void GetResourceModelDiagnostics_IncludesResourceModelBridgeDiagnostics()
    {
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new DefinitionGraphSnapshot(DefinitionGraphVersion.Initial, [CreateResourceModelExecutableState()]),
            new DefinitionResourceResolver(
                [
                    new(ExecutableApplicationResourceTypeProvider.ClassId)
                ],
                [
                    new(
                        ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                        ExecutableApplicationResourceTypeProvider.ClassId,
                        Attributes: new Dictionary<DefinitionAttributeId, DefinitionAttributeDefinition>
                        {
                            ["container:replicas"] = new(
                                DefaultValue: "one",
                                ValueType: DefinitionAttributeValueType.Integer)
                        })
                ]));
        var store = new ResourceManagerStore(
            [provider],
            new TestResourceGroupStore([]),
            new TestResourceRegistrationStore([]),
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Equal(DefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("application.executable:api", diagnostic.ResourceId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(), diagnostic.ResourceType);
        Assert.Equal("resource model", diagnostic.Source);
        Assert.Contains("container:replicas", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetResources_ComposesDiRegisteredResourceModelBridgeProvider()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraph([CreateResourceModelExecutableState()]);
        services.AddResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            projectionOptions: new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var store = new ResourceManagerStore(
            providers,
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
        Assert.Equal(
            "dotnet",
            resource.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.True(resource.IsDeclaredResource);
    }

    [Fact]
    public void GetResources_ComposesRegistrationMetadataOverResourceModelBridgeProjection()
    {
        var group = new ResourceGroup("apps", "Applications", "Application resources", []);
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraph([CreateResourceModelExecutableState()]);
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = new TestResourceRegistrationStore(
        [
            new(
                "application.executable:api",
                "resource-model",
                group.Id,
                DateTimeOffset.UtcNow,
                ["configuration:api"],
                new ResourceIdentityBinding("identity:dev", Name: "api-service"))
        ]);
        var store = new ResourceManagerStore(
            serviceProvider.GetServices<IResourceProvider>().ToArray(),
            new TestResourceGroupStore([group]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(
                [new("identity:dev", "Development identity", ResourceIdentityProviderKind.BuiltIn)]),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var resource = Assert.Single(store.GetResources());

        Assert.Equal(["storage:data", "configuration:api"], resource.DependsOn);
        Assert.NotNull(resource.IdentityBinding);
        Assert.Equal("identity:dev", resource.IdentityBinding.ProviderId);
        Assert.Equal("api-service", resource.IdentityBinding.Name);
        Assert.Equal(group.Id, store.GetGroupForResource(resource.Id)?.Id);
        Assert.Empty(store.GetResourceModelDiagnostics());
    }

    [Fact]
    public void GetResources_ProjectsPersistedResourceRecordsThroughResourceModelBridge()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            [DefinitionResourceRecord.FromState(CreateResourceModelExecutableState())]);
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var store = new ResourceManagerStore(
            serviceProvider.GetServices<IResourceProvider>().ToArray(),
            new TestResourceGroupStore([]),
            new TestResourceRegistrationStore(
            [
                new(
                    "application.executable:api",
                    "resource-model",
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
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, resource.Provider);
        Assert.Equal(
            "dotnet",
            resource.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Contains(resource.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(resource.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void GetResources_ProjectsPersistedApplicationTopologyRecordsThroughResourceModelBridge()
    {
        var states = CreateApplicationTopologyProjectGraphStates();
        var services = new ServiceCollection();
        services.AddLocalVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            states.Select(DefinitionResourceRecord.FromState).ToArray());
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = new TestResourceRegistrationStore(
            states
                .Select(state => new ResourceRegistration(
                    state.EffectiveResourceId,
                    "resource-model",
                    null,
                    DateTimeOffset.UtcNow,
                    []))
                .ToArray());
        var store = new ResourceManagerStore(
            serviceProvider.GetServices<IResourceProvider>().ToArray(),
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var resources = store.GetResources().ToDictionary(
            resource => resource.Id,
            StringComparer.OrdinalIgnoreCase);
        var api = resources["application.dotnet-app:application-topology-api"];
        var database = resources["application.sql-database:application-topology-db"];
        var sqlServer = resources["application.sql-server:application-topology-sql-server"];

        Assert.Equal(6, resources.Count);
        Assert.Empty(store.GetResourceModelDiagnostics());
        Assert.Equal(ResourceClass.Project, api.ResourceClass);
        Assert.Equal(AspNetCoreProjectResourceTypeProvider.ProviderId, api.Provider);
        Assert.True(api.IsDeclaredResource);
        Assert.Equal("../Api/CloudShell.ApplicationTopologyApi.csproj", api.ResourceAttributes[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath]);
        Assert.Equal(
            [
                "application.sql-database:application-topology-db",
                "configuration.store:application-topology-settings",
                "secrets.vault:application-topology-secrets"
            ],
            api.DependsOn);
        Assert.Contains(api.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
        Assert.Equal(["application.sql-server:application-topology-sql-server"], database.DependsOn);
        Assert.Equal(["storage.volume:application-topology-sql-data"], sqlServer.DependsOn);
    }

    [Fact]
    public void GetResources_ProjectsPersistedSettingsAndSecretsRecordsThroughResourceModelBridge()
    {
        var states = CreateSettingsAndSecretsGraphStates();
        var services = new ServiceCollection();
        services.AddIdentityProvisioningResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            states.Select(DefinitionResourceRecord.FromState).ToArray());
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = new TestResourceRegistrationStore(
            states
                .Select(state => new ResourceRegistration(
                    state.EffectiveResourceId,
                    "resource-model",
                    null,
                    DateTimeOffset.UtcNow,
                    []))
                .ToArray());
        var store = new ResourceManagerStore(
            serviceProvider.GetServices<IResourceProvider>().ToArray(),
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var resources = store.GetResources().ToDictionary(
            resource => resource.Id,
            StringComparer.OrdinalIgnoreCase);
        var identity = resources["cloudshell.identity-provisioning:settings-secrets-identity"];
        var settings = resources["configuration.store:settings-secrets-settings"];
        var secrets = resources["secrets.vault:settings-secrets-secrets"];
        var api = resources["application.dotnet-app:settings-secrets-api"];

        Assert.Equal(4, resources.Count);
        Assert.Empty(store.GetResourceModelDiagnostics());
        Assert.Equal(ResourceClass.Infrastructure, identity.ResourceClass);
        Assert.Equal(ResourceClass.Configuration, settings.ResourceClass);
        Assert.Equal(ResourceClass.SecretsVault, secrets.ResourceClass);
        Assert.Equal(ResourceClass.Project, api.ResourceClass);
        Assert.Equal(
            [
                "cloudshell.identity-provisioning:settings-secrets-identity",
                "configuration.store:settings-secrets-settings",
                "secrets.vault:settings-secrets-secrets"
            ],
            api.DependsOn);
        Assert.Equal(
            ["cloudshell.identity-provisioning:settings-secrets-identity"],
            settings.DependsOn);
        Assert.Equal(
            ["cloudshell.identity-provisioning:settings-secrets-identity"],
            secrets.DependsOn);
        Assert.Equal("http://localhost:5138", settings.ResourceAttributes[
            ConfigurationStoreResourceTypeProvider.Attributes.Endpoint]);
        Assert.Equal("http://localhost:6138", secrets.ResourceAttributes[
            SecretsVaultResourceTypeProvider.Attributes.Endpoint]);
        Assert.Equal("../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj", api.ResourceAttributes[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath]);
        Assert.Contains(identity.ResourceCapabilities, capability =>
            capability.Id == IdentityProvisioningResourceTypeProvider.Capabilities.IdentityProvisioning.ToString());
        Assert.Contains(api.ResourceActions, action =>
            action.Id == AspNetCoreProjectResourceTypeProvider.Operations.Start.ToString());
    }

    [Fact]
    public void GetResources_ReportsPersistedSettingsAndSecretsWrongIdentityReferenceDiagnostics()
    {
        var states = CreateSettingsAndSecretsGraphStatesWithInvalidIdentityReference();
        var services = new ServiceCollection();
        services.AddIdentityProvisioningResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            states.Select(DefinitionResourceRecord.FromState).ToArray());
        services.AddResourceModelGraphResourceProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = new TestResourceRegistrationStore(
            states
                .Select(state => new ResourceRegistration(
                    state.EffectiveResourceId,
                    "resource-model",
                    null,
                    DateTimeOffset.UtcNow,
                    []))
                .ToArray());
        var store = new ResourceManagerStore(
            serviceProvider.GetServices<IResourceProvider>().ToArray(),
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());

        var settings = Assert.Single(store.GetResources(), resource =>
            resource.Id == "configuration.store:settings-secrets-settings");
        var diagnostic = Assert.Single(store.GetResourceModelDiagnostics());

        Assert.Empty(settings.DependsOn);
        Assert.Equal(DefinitionDiagnosticCodes.ResourceReferenceTypeMismatch, diagnostic.Code);
        Assert.Equal("configuration.store:settings-secrets-settings", diagnostic.ResourceId);
        Assert.Contains(ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(), diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(IdentityProvisioningResourceTypeProvider.ResourceTypeId.ToString(), diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteActionAsync_RoutesGraphResourceThroughProcedureCapableBridgeProvider()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraph([CreateResourceModelExecutableState()]);
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var actionAvailabilityProviders = serviceProvider
            .GetServices<IResourceActionAvailabilityProvider>()
            .ToArray();
        var registrations = new TestResourceRegistrationStore(
        [
            new(
                "application.executable:api",
                "resource-model",
                null,
                DateTimeOffset.UtcNow,
                [])
        ]);
        var store = new ResourceManagerStore(
            providers,
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            store,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            actionAvailabilityProviders: actionAvailabilityProviders);
        var resource = Assert.Single(store.GetResources());

        Assert.Contains(
            actionAvailabilityProviders,
            provider => provider is ResourceModelGraphProcedureProvider);

        var result = await orchestration.ExecuteActionAsync(
            resource,
            ResourceAction.Start,
            startDependencies: false,
            new AllowAllAuthorizationService());

        Assert.Equal("Executed Start for api.", result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_RoutesPersistedRecordThroughProcedureCapableBridgeProvider()
    {
        var services = new ServiceCollection();
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            [DefinitionResourceRecord.FromState(CreateResourceModelExecutableState())]);
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var actionAvailabilityProviders = serviceProvider
            .GetServices<IResourceActionAvailabilityProvider>()
            .ToArray();
        var registrations = new TestResourceRegistrationStore(
        [
            new(
                "application.executable:api",
                "resource-model",
                null,
                DateTimeOffset.UtcNow,
                [])
        ]);
        var store = new ResourceManagerStore(
            providers,
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            store,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            actionAvailabilityProviders: actionAvailabilityProviders);
        var resource = Assert.Single(store.GetResources());

        var unavailableReason = await orchestration.GetActionUnavailableReasonAsync(
            resource,
            ResourceAction.Start);
        var result = await orchestration.ExecuteActionAsync(
            resource,
            ResourceAction.Start,
            startDependencies: false,
            new AllowAllAuthorizationService());

        Assert.Null(unavailableReason);
        Assert.Equal("Executed Start for api.", result.Message);
    }

    [Fact]
    public async Task ExecuteActionAsync_RoutesPersistedSettingsAndSecretsOperationsThroughProcedureCapableBridgeProvider()
    {
        var states = CreateSettingsAndSecretsGraphStates();
        var services = new ServiceCollection();
        services.AddIdentityProvisioningResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            states.Select(DefinitionResourceRecord.FromState).ToArray());
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var actionAvailabilityProviders = serviceProvider
            .GetServices<IResourceActionAvailabilityProvider>()
            .ToArray();
        var registrations = new TestResourceRegistrationStore(
            states
                .Select(state => new ResourceRegistration(
                    state.EffectiveResourceId,
                    "resource-model",
                    null,
                    DateTimeOffset.UtcNow,
                    []))
                .ToArray());
        var store = new ResourceManagerStore(
            providers,
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            store,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            actionAvailabilityProviders: actionAvailabilityProviders);
        var resources = store.GetResources().ToDictionary(
            resource => resource.Id,
            StringComparer.OrdinalIgnoreCase);
        var identity = resources["cloudshell.identity-provisioning:settings-secrets-identity"];
        var settings = resources["configuration.store:settings-secrets-settings"];
        var secrets = resources["secrets.vault:settings-secrets-secrets"];
        var identitySetup = Assert.Single(identity.ResourceActions, action =>
            action.Id == IdentityProvisioningResourceTypeProvider.Operations.Setup.ToString());
        var settingsInspect = Assert.Single(settings.ResourceActions, action =>
            action.Id == ConfigurationStoreResourceTypeProvider.Operations.Inspect.ToString());
        var secretsInspect = Assert.Single(secrets.ResourceActions, action =>
            action.Id == SecretsVaultResourceTypeProvider.Operations.Inspect.ToString());

        var identitySetupUnavailableReason = await orchestration.GetActionUnavailableReasonAsync(identity, identitySetup);

        Assert.NotNull(identitySetupUnavailableReason);
        Assert.Contains("no identity provisioning setup handler", identitySetupUnavailableReason, StringComparison.Ordinal);
        Assert.Null(await orchestration.GetActionUnavailableReasonAsync(settings, settingsInspect));
        Assert.Null(await orchestration.GetActionUnavailableReasonAsync(secrets, secretsInspect));
        var settingsResult = await orchestration.ExecuteActionAsync(
            settings,
            settingsInspect,
            startDependencies: false,
            new AllowAllAuthorizationService());
        var secretsResult = await orchestration.ExecuteActionAsync(
            secrets,
            secretsInspect,
            startDependencies: false,
            new AllowAllAuthorizationService());

        Assert.StartsWith(
            "Executed Configuration Store Inspect for settings-secrets-settings.",
            settingsResult.Message,
            StringComparison.Ordinal);
        Assert.Contains("Configuration Store", settingsResult.Message, StringComparison.Ordinal);
        Assert.StartsWith(
            "Executed Secrets Vault Inspect for settings-secrets-secrets.",
            secretsResult.Message,
            StringComparison.Ordinal);
        Assert.Contains("Secrets Vault", secretsResult.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetActionUnavailableReasonAsync_BlocksPersistedSettingsAndSecretsOperationForWrongIdentityReference()
    {
        var states = CreateSettingsAndSecretsGraphStatesWithInvalidIdentityReference();
        var services = new ServiceCollection();
        services.AddIdentityProvisioningResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraphRecords(
            states.Select(DefinitionResourceRecord.FromState).ToArray());
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var actionAvailabilityProviders = serviceProvider
            .GetServices<IResourceActionAvailabilityProvider>()
            .ToArray();
        var registrations = new TestResourceRegistrationStore(
            states
                .Select(state => new ResourceRegistration(
                    state.EffectiveResourceId,
                    "resource-model",
                    null,
                    DateTimeOffset.UtcNow,
                    []))
                .ToArray());
        var store = new ResourceManagerStore(
            providers,
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            store,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            actionAvailabilityProviders: actionAvailabilityProviders);
        var settings = Assert.Single(store.GetResources(), resource =>
            resource.Id == "configuration.store:settings-secrets-settings");
        var inspect = Assert.Single(settings.ResourceActions, action =>
            action.Id == ConfigurationStoreResourceTypeProvider.Operations.Inspect.ToString());

        var reason = await orchestration.GetActionUnavailableReasonAsync(settings, inspect);

        Assert.NotNull(reason);
        Assert.Contains("resolved to resource type", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(), reason, StringComparison.Ordinal);
        Assert.Contains(IdentityProvisioningResourceTypeProvider.ResourceTypeId.ToString(), reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetActionUnavailableReasonAsync_ReportsGraphOperationProjectionDiagnostics()
    {
        var services = new ServiceCollection();
        services.AddSingleton(ExecutableApplicationResourceTypeProvider.ClassDefinition);
        services.AddSingleton<DefinitionResourceTypeProvider, ExecutableApplicationResourceTypeProvider>();
        services.AddResourceModelGraphServices();
        services.AddInMemoryResourceModelGraph([CreateResourceModelExecutableState()]);
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider
            .GetServices<IResourceProvider>()
            .ToArray();
        var actionAvailabilityProviders = serviceProvider
            .GetServices<IResourceActionAvailabilityProvider>()
            .ToArray();
        var registrations = new TestResourceRegistrationStore(
        [
            new(
                "application.executable:api",
                "resource-model",
                null,
                DateTimeOffset.UtcNow,
                [])
        ]);
        var store = new ResourceManagerStore(
            providers,
            new TestResourceGroupStore([]),
            registrations,
            new ResourceDeclarationStore(),
            new ResourceIdentityProviderCatalog(),
            new CloudShellExtensionRegistry(),
            new InMemoryCloudShellExtensionActivationStore());
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            store,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore(),
            actionAvailabilityProviders: actionAvailabilityProviders);
        var resource = Assert.Single(store.GetResources());

        var reason = await orchestration.GetActionUnavailableReasonAsync(
            resource,
            ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains("no operation projection is available", reason, StringComparison.Ordinal);
        Assert.Contains("start", reason, StringComparison.OrdinalIgnoreCase);
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
            DependsOn: [DefinitionResourceReference.DependsOnResourceId("storage:data")],
            Attributes: new Dictionary<DefinitionAttributeId, CloudShell.ResourceModel.ResourceAttributeValue>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet",
                [ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                    CloudShell.ResourceModel.ResourceAttributeValue.FromObject(
                        new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<DefinitionCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    DefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("storage:data", "App_Data")
                    ]))
            });

    private static IReadOnlyList<DefinitionResourceState> CreateApplicationTopologyProjectGraphStates()
    {
        var volume = new DefinitionResourceState(
            "application-topology-sql-data",
            LocalVolumeResourceTypeProvider.ResourceTypeId);
        var sqlServer = new DefinitionResourceState(
            "application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Capabilities: new Dictionary<DefinitionCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    DefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(volume.EffectiveResourceId, "/var/opt/mssql")
                    ]))
            });
        var database = new DefinitionResourceState(
            "application-topology-db",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlDatabaseResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    sqlServer.EffectiveResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "application_topology",
                [SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated] = bool.TrueString.ToLowerInvariant()
            });
        var settings = new DefinitionResourceState(
            "application-topology-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId);
        var secrets = new DefinitionResourceState(
            "application-topology-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId);
        var api = new DefinitionResourceState(
            "application-topology-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    database.EffectiveResourceId,
                    typeId: SqlDatabaseResourceTypeProvider.ResourceTypeId),
                DefinitionResourceReference.DependsOnResourceId(
                    settings.EffectiveResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                DefinitionResourceReference.DependsOnResourceId(
                    secrets.EffectiveResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../Api/CloudShell.ApplicationTopologyApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:21422",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    bool.TrueString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    bool.FalseString.ToLowerInvariant()
            });

        return [volume, sqlServer, database, settings, secrets, api];
    }

    private static IReadOnlyList<DefinitionResourceState> CreateSettingsAndSecretsGraphStates()
    {
        var identity = new DefinitionResourceState(
            "settings-secrets-identity",
            IdentityProvisioningResourceTypeProvider.ResourceTypeId,
            ProviderId: IdentityProvisioningResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider] = "Built-in Identity",
                [IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind] = "built-in"
            });
        var settings = new DefinitionResourceState(
            "settings-secrets-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });
        var secrets = new DefinitionResourceState(
            "settings-secrets-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ProviderId: SecretsVaultResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] = "http://localhost:6138"
            });
        var api = new DefinitionResourceState(
            "settings-secrets-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    identity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId),
                DefinitionResourceReference.DependsOnResourceId(
                    settings.EffectiveResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                DefinitionResourceReference.DependsOnResourceId(
                    secrets.EffectiveResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:5227",
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    bool.FalseString.ToLowerInvariant(),
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    bool.FalseString.ToLowerInvariant()
            });

        return [identity, settings, secrets, api];
    }

    private static IReadOnlyList<DefinitionResourceState> CreateSettingsAndSecretsGraphStatesWithInvalidIdentityReference()
    {
        var wrongIdentity = new DefinitionResourceState(
            "settings-secrets-identity",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId);
        var settings = new DefinitionResourceState(
            "settings-secrets-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ProviderId: ConfigurationStoreResourceTypeProvider.ProviderId,
            DependsOn:
            [
                DefinitionResourceReference.DependsOnResourceId(
                    wrongIdentity.EffectiveResourceId,
                    typeId: IdentityProvisioningResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<DefinitionAttributeId, string>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] = "http://localhost:5138"
            });

        return [wrongIdentity, settings];
    }

    private static ResourceOrchestratorSelectionStore CreateSelectionStore() =>
        new(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));

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

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
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
