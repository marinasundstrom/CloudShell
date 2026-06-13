using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using CloudShell.Providers.DockerCompose;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text.Json;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeclarationTests
{
    [Fact]
    public void Resources_DeclaresTransientResourceWithDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .DependsOn("application:api");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal(["application:api"], declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Transient, declaration.Persistence);
        Assert.False(declaration.OverwritePersistedState);
    }

    [Fact]
    public void WithReference_RemainsAliasForGenericDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .Declare("configuration", "configuration:example")
                    .WithReference(api);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:example");

        Assert.Equal(["application:api"], declaration.DependsOn);
    }

    [Fact]
    public void WithParent_RecordsDeclarationParent()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var parent = resources.Declare("docker", "docker:dev");

                resources
                    .Declare("docker", "docker:container:redis")
                    .WithParent(parent);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "docker:container:redis");

        Assert.Equal("docker:dev", declaration.ParentResourceId);
        Assert.Empty(declaration.DependsOn);
    }

    [Fact]
    public void ResourceMetadataExtensions_RecordClassAndAttributes()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare(
                        "metadata",
                        "sample:resource",
                        resourceClass: ResourceClass.Executable,
                        attributes: new Dictionary<string, string>
                        {
                            [" executable.path "] = " dotnet "
                        })
                    .WithResourceClass(ResourceClass.Project)
                    .WithResourceAttribute(" project.path ", " src/API/API.csproj ")
                    .WithResourceAttributes(new Dictionary<string, string>
                    {
                        [" project.language "] = " csharp ",
                        [" project.path "] = " src/Worker/Worker.csproj "
                    });
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal(ResourceClass.Project, declaration.ResourceClassOverride);
        Assert.Equal("dotnet", declaration.ResourceAttributes["executable.path"]);
        Assert.Equal("src/Worker/Worker.csproj", declaration.ResourceAttributes["project.path"]);
        Assert.Equal("csharp", declaration.ResourceAttributes["project.language"]);
    }

    [Fact]
    public void ResourceIdentityExtensions_RecordProgrammaticIdentity()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("applications", "application:api")
                    .WithIdentity(identity =>
                    {
                        identity.Name = "api-service";
                        identity.Provider = "development";
                        identity.Subject = "application:api";
                        identity.Scopes.Add("database.read");
                        identity.Claims.Add("resource", "api");
                    });
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());
        var identity = declaration.IdentityBinding;

        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityBindingKind.Provider, identity.Kind);
        Assert.Equal("api-service", identity.Name);
        Assert.Equal("development", identity.ProviderId);
        Assert.Equal("application:api", identity.Subject);
        Assert.Equal(["database.read"], identity.IdentityScopes);
        Assert.Equal("api", identity.IdentityClaims["resource"]);
    }

    [Fact]
    public void ResourceIdentityExtensions_CanDeclareIdentityRequirementWithoutProvider()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("applications", "application:api")
                    .RequireIdentity(["database.read"], name: "api-service");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());
        var identity = declaration.IdentityBinding;

        Assert.NotNull(identity);
        Assert.Equal(ResourceIdentityBindingKind.Required, identity.Kind);
        Assert.Equal("api-service", identity.Name);
        Assert.Null(identity.ProviderId);
        Assert.Equal(["database.read"], identity.IdentityScopes);
    }

    [Fact]
    public void ResourcePermissionGrantExtensions_RecordIdentityPermissionGrant()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources
                    .Declare("applications", "application:api")
                    .WithIdentity("development", name: "api-service");

                resources
                    .Declare("configuration", "configuration:database")
                    .Allow(api.Identity, "Database/databases/readWrite/action")
                    .Allow(api.Identity, "Database/databases/readWrite/action");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var grant = Assert.Single(store.GetPermissionGrants());

        Assert.Equal("application:api", grant.Identity.ResourceId);
        Assert.Equal("api-service", grant.Identity.Name);
        Assert.Equal("configuration:database", grant.TargetResourceId);
        Assert.Equal("Database/databases/readWrite/action", grant.Permission);
    }

    [Fact]
    public void ResourcePermissionGrantExtensions_CanGrantToResourceBuilderIdentity()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .Declare("configuration", "configuration:database")
                    .Allow(api, "Database/databases/read/action");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var grant = Assert.Single(store.GetPermissionGrants());

        Assert.Equal("application:api", grant.Identity.ResourceId);
        Assert.Equal("configuration:database", grant.TargetResourceId);
        Assert.Equal("Database/databases/read/action", grant.Permission);
    }

    [Fact]
    public void ResourcePermissionGrantEvaluator_EvaluatesDeclaredGrants()
    {
        var evaluator = new ResourcePermissionGrantEvaluator(
            [
                new(
                    ResourceIdentityReference.ForResource("application:api", "api-service"),
                    "configuration:database",
                    "Database/databases/read/action")
            ]);

        var allowed = evaluator.Evaluate(
            ResourceIdentityReference.ForResource("application:api", "api-service"),
            "configuration:database",
            "Database/databases/read/action");
        var deniedPermission = evaluator.Evaluate(
            ResourceIdentityReference.ForResource("application:api", "api-service"),
            "configuration:database",
            "Database/databases/write/action");
        var deniedIdentity = evaluator.Evaluate(
            ResourceIdentityReference.ForResource("application:api", "worker-service"),
            "configuration:database",
            "Database/databases/read/action");

        Assert.True(allowed.IsAllowed);
        Assert.NotNull(allowed.Grant);
        Assert.False(deniedPermission.IsAllowed);
        Assert.Null(deniedPermission.Grant);
        Assert.False(deniedIdentity.IsAllowed);
        Assert.Null(deniedIdentity.Grant);
    }

    [Fact]
    public void ResourcePermissionGrantEvaluator_SupportsWildcardPermission()
    {
        var evaluator = new ResourcePermissionGrantEvaluator(
            [
                new(
                    ResourceIdentityReference.ForResource("application:api"),
                    "configuration:database",
                    CloudShellPermissions.All)
            ]);

        var allowed = evaluator.Evaluate(
            ResourceIdentityReference.ForResource("application:api"),
            "configuration:database",
            "Database/databases/read/action");

        Assert.True(allowed.IsAllowed);
    }

    [Fact]
    public void ResourceDeclarationStore_CreatesPermissionGrantEvaluator()
    {
        var store = new ResourceDeclarationStore();
        store.AddPermissionGrant(new ResourcePermissionGrant(
            ResourceIdentityReference.ForResource("application:api"),
            "configuration:database",
            "Database/databases/read/action"));

        var evaluation = store
            .CreatePermissionGrantEvaluator()
            .Evaluate(
                ResourceIdentityReference.ForResource("application:api"),
                "configuration:database",
                "Database/databases/read/action");

        Assert.True(evaluation.IsAllowed);
    }

    [Fact]
    public void ResourceManagerStore_AppliesDeclarationParentMetadata()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .AddExtension<ParentMetadataExtension>()
            .Resources(resources =>
            {
                var parent = resources.Declare("parent-metadata", "sample:parent");

                resources
                    .Declare("parent-metadata", "sample:child")
                    .WithParent(parent);
            });
        services.AddSingleton<IResourceGroupStore, EmptyResourceGroupStore>();
        services.AddSingleton<IResourceRegistrationStore, DeclarationRegistrationStore>();
        services.AddScoped<IResourceManagerStore, ResourceManagerStore>();

        using var serviceProvider = services.BuildServiceProvider();
        var resources = serviceProvider
            .GetRequiredService<IResourceManagerStore>()
            .GetResources();
        var child = Assert.Single(resources, resource => resource.Id == "sample:child");

        Assert.Equal("sample:parent", child.ParentResourceId);
    }


    [Fact]
    public void Persist_CanRequestOverwrite()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("configuration", "configuration:example")
                    .Persist(overwrite: true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
        Assert.True(declaration.OverwritePersistedState);
    }

    [Fact]
    public void WithAutoStart_ConfiguresStartupDefaultAndResourceOverrides()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.WithAutoStart(false);

                resources.Declare("configuration", "configuration:inherited");
                resources
                    .Declare("configuration", "configuration:enabled")
                    .WithAutoStart(true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var inherited = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:inherited");
        var enabled = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:enabled");

        Assert.False(store.DefaultAutoStart);
        Assert.Null(inherited.AutoStartOverride);
        Assert.True(enabled.AutoStartOverride);
        Assert.False(store.ShouldAutoStart("configuration:inherited"));
        Assert.True(store.ShouldAutoStart("configuration:enabled"));
        Assert.True(store.DefaultDependencyAutoStart);
        Assert.True(store.ShouldAutoStartAsDependency("configuration:inherited"));
    }

    [Fact]
    public void WithDependencyAutoStart_ConfiguresDefaultAndResourceOverrides()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.WithDependencyAutoStart(false);

                resources.Declare("configuration", "configuration:inherited");
                resources
                    .Declare("configuration", "configuration:enabled")
                    .WithDependencyAutoStart(true);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var inherited = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:inherited");
        var enabled = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:enabled");

        Assert.False(store.DefaultDependencyAutoStart);
        Assert.Null(inherited.DependencyAutoStartOverride);
        Assert.True(enabled.DependencyAutoStartOverride);
        Assert.False(store.ShouldAutoStartAsDependency("configuration:inherited"));
        Assert.True(store.ShouldAutoStartAsDependency("configuration:enabled"));
        Assert.True(store.DefaultAutoStart);
        Assert.True(store.ShouldAutoStart("configuration:inherited"));
    }

    [Fact]
    public void WithAutoStart_PreservesTypedBuilderFluentChains()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example", "Example Configuration")
                    .WithAutoStart(false)
                    .WithEntry("SampleMessage", "Hello");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.False(declaration.AutoStartOverride);
    }

    [Fact]
    public void WithDependencyAutoStart_PreservesTypedBuilderFluentChains()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example", "Example Configuration")
                    .WithDependencyAutoStart(false)
                    .WithEntry("SampleMessage", "Hello");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.False(declaration.DependencyAutoStartOverride);
    }

    [Fact]
    public void ProvisionIdentityOnStartup_RecordsResourceDeclarationIntent()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .Declare("applications", "application:api")
                    .WithIdentity("identity:development", name: "api-service")
                    .ProvisionIdentityOnStartup();
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.True(declaration.ProvisionIdentityOnStartup);
        Assert.Equal("identity:development", declaration.IdentityBinding?.ProviderId);
        Assert.Equal("api-service", declaration.IdentityBinding?.Name);
    }

    [Fact]
    public async Task ExecuteAction_DoesNotStartDependencyWhenDependencyAutoStartIsDisabled()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var dependency = resources
                    .Declare("auto-start", "dependency")
                    .WithDependencyAutoStart(false);

                resources
                    .Declare("auto-start", "target")
                    .DependsOn(dependency);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = new AutoStartResourceProvider();
        var registrations = new DeclarationRegistrationStore(declarations);
        var resourceManager = new TestResourceManagerStore(provider, registrations);
        var selectionStore = new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            resourceManager,
            registrations,
            declarations,
            selectionStore);
        var target = resourceManager.GetResource("target")!;

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.ExecuteActionAsync(
                target,
                ResourceAction.Run,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains(
            "Could not auto-start dependency 'Dependency' for resource 'Target'.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependency path: Target (target) -> Dependency (dependency).",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains("Reason: auto-start is disabled.", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.ExecutedResources);
    }

    [Fact]
    public async Task ExecuteAction_StartsDependencyWhenStartupAutoStartIsDisabled()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var dependency = resources
                    .Declare("auto-start", "dependency")
                    .WithAutoStart(false);

                resources
                    .Declare("auto-start", "target")
                    .DependsOn(dependency);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = new AutoStartResourceProvider();
        var registrations = new DeclarationRegistrationStore(declarations);
        var resourceManager = new TestResourceManagerStore(provider, registrations);
        var selectionStore = new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            resourceManager,
            registrations,
            declarations,
            selectionStore);
        var target = resourceManager.GetResource("target")!;

        await orchestration.ExecuteActionAsync(
            target,
            ResourceAction.Run,
            startDependencies: true,
            new AllowAllAuthorizationService());

        Assert.Equal(["dependency", "target"], provider.ExecutedResources);
    }

    [Fact]
    public async Task ExecuteAction_UsesProviderDependencyAutoStartDefault()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var dependency = resources
                    .Declare("auto-start", "dependency");

                resources
                    .Declare("auto-start", "target")
                    .DependsOn(dependency);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = new AutoStartResourceProvider
        {
            DefaultDependencyAutoStart = false
        };
        var registrations = new DeclarationRegistrationStore(declarations);
        var resourceManager = new TestResourceManagerStore(provider, registrations);
        var selectionStore = new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            resourceManager,
            registrations,
            declarations,
            selectionStore);
        var target = resourceManager.GetResource("target")!;

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.ExecuteActionAsync(
                target,
                ResourceAction.Run,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains("Reason: auto-start is disabled.", exception.Message, StringComparison.Ordinal);
        Assert.Empty(provider.ExecutedResources);
    }

    [Fact]
    public async Task ExecuteAction_ReturnsDependencyAutoStartFailureDetailsWhenDependencyRunFails()
    {
        var services = new ServiceCollection();
        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var dependency = resources
                    .Declare("auto-start", "dependency");

                resources
                    .Declare("auto-start", "target")
                    .DependsOn(dependency);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provider = new AutoStartResourceProvider
        {
            FailedResourceId = "dependency",
            FailureMessage = "Dependency process failed to bind port 5104."
        };
        var registrations = new DeclarationRegistrationStore(declarations);
        var resourceManager = new TestResourceManagerStore(provider, registrations);
        var selectionStore = new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            resourceManager,
            registrations,
            declarations,
            selectionStore);
        var target = resourceManager.GetResource("target")!;

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            orchestration.ExecuteActionAsync(
                target,
                ResourceAction.Run,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains(
            "Could not auto-start dependency 'Dependency' for resource 'Target'.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependency path: Target (target) -> Dependency (dependency).",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Reason: Dependency process failed to bind port 5104.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Equal(["dependency"], provider.ExecutedResources);
    }

    [Fact]
    public void TypedConfigurationStoreBuilder_DeclaresResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example", "Example Configuration")
                    .WithEntry("SampleMessage", "Hello")
                    .WithResourceGroup("group-1")
                    .Persist();
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("configuration", declaration.ProviderId);
        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal("group-1", declaration.ResourceGroupId);
        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
    }

    [Fact]
    public void TypedConfigurationStoreBuilder_CreatesEntryReferences()
    {
        var services = new ServiceCollection();
        ConfigurationEntryReference? reference = null;

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources
                    .AddConfigurationStore("configuration:app", "App Settings");

                reference = settings.Entry("Database:Name");
            });

        Assert.NotNull(reference);
        Assert.Equal("configuration:app", reference.StoreResourceId);
        Assert.Equal("Database:Name", reference.EntryName);
        Assert.Null(reference.Version);
    }

    [Fact]
    public void TypedHostConfigurationSourceBuilder_DeclaresResourceAndCreatesEntryReferences()
    {
        var services = new ServiceCollection();
        ConfigurationEntryReference? reference = null;

        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var hostSettings = resources
                    .AddHostConfigurationSource("configuration:host-dev", "Host Development Settings")
                    .WithEntry("ExternalApi:BaseUrl");

                reference = hostSettings.Entry("ExternalApi:BaseUrl");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:host-dev");
        var provider = serviceProvider.GetRequiredService<HostConfigurationSourceProvider>();
        var resource = Assert.Single(provider.GetResources());

        Assert.Equal(HostConfigurationSourceProvider.ProviderId, declaration.ProviderId);
        Assert.Equal(ResourceClass.Configuration, declaration.ResourceClassOverride);
        Assert.Equal("configuration:host-dev", reference?.StoreResourceId);
        Assert.Equal("ExternalApi:BaseUrl", reference?.EntryName);
        Assert.Equal("configuration:host-dev", resource.Id);
        Assert.Equal(HostConfigurationSourceProvider.ResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Configuration, resource.ResourceClass);
        Assert.Equal("1", resource.ResourceAttributes[ResourceAttributeNames.ConfigurationEntryCount]);
    }

    [Fact]
    public void TypedSecretsVaultBuilder_DeclaresResourceAndCreatesSecretReferences()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        SecretReference? reference = null;

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var vault = resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("db-password", "local-dev-password");

                reference = vault.Secret("db-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "secrets-vault:app");
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resource = Assert.Single(provider.GetResources());

        Assert.Equal(SecretsVaultProvider.ProviderId, declaration.ProviderId);
        Assert.Equal(ResourceClass.SecretsVault, declaration.ResourceClassOverride);
        Assert.Equal("secrets-vault:app", reference?.VaultResourceId);
        Assert.Equal("db-password", reference?.SecretName);
        Assert.Equal("secrets-vault:app", resource.Id);
        Assert.Equal(SecretsVaultProvider.ResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.SecretsVault, resource.ResourceClass);
        Assert.Equal("1", resource.ResourceAttributes["secretsVault.secrets"]);
    }

    [Fact]
    public void TypedSecretsVaultBuilder_WorksWithSecretsProviderOnly()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        SecretReference? reference = null;

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddSecretsProvider()
            .Resources(resources =>
            {
                var vault = resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("db-password", "local-dev-password");

                reference = vault.Secret("db-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resolver = Assert.Single(serviceProvider.GetServices<ISecretReferenceResolver>());
        var resource = Assert.Single(provider.GetResources());

        Assert.Same(provider, resolver);
        Assert.Null(serviceProvider.GetService<ConfigurationResourceProvider>());
        Assert.Equal("secrets-vault:app", reference?.VaultResourceId);
        Assert.Equal("db-password", reference?.SecretName);
        Assert.Equal("secrets-vault:app", resource.Id);
        Assert.Equal(SecretsVaultProvider.ResourceType, resource.EffectiveTypeId);
    }

    [Fact]
    public void ConfigurationAndSecretsProviders_ComposeWithoutDuplicateExtensions()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .AddSecretsProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var extensionIds = registry.Extensions
            .Select(extension => extension.Id)
            .ToArray();
        var resourceTypeIds = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .Select(resourceType => resourceType.Id)
            .ToArray();

        Assert.Contains("cloudshell.configuration", extensionIds);
        Assert.Contains("cloudshell.secrets", extensionIds);
        Assert.Equal(
            resourceTypeIds.Length,
            resourceTypeIds.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Single(serviceProvider.GetServices<ISecretReferenceResolver>());
    }

    [Fact]
    public async Task SecretsVaultProvider_ResolvesSecretsFromMultipleVaults()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                resources
                    .AddSecretsVault("secrets-vault:one", "One")
                    .WithSecret("token", "one-token");
                resources
                    .AddSecretsVault("secrets-vault:two", "Two")
                    .WithSecret("token", "two-token");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var context = new ResourceSettingResolutionContext("application:api", "group-1", "run");

        var first = await provider.ResolveSecretAsync(
            new SecretReference("secrets-vault:one", "token"),
            context);
        var second = await provider.ResolveSecretAsync(
            new SecretReference("secrets-vault:two", "token"),
            context);
        var missing = await provider.ResolveSecretAsync(
            new SecretReference("secrets-vault:one", "missing"),
            context);

        Assert.True(first.IsResolved);
        Assert.Equal("one-token", first.Value);
        Assert.True(second.IsResolved);
        Assert.Equal("two-token", second.Value);
        Assert.False(missing.IsResolved);
        Assert.Contains("was not found", missing.ErrorMessage);
    }

    [Fact]
    public async Task SecretsVaultProvider_RequiresGrantForIdentityBoundSecretResolution()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var api = resources
                    .Declare("applications", "application:api")
                    .WithIdentity("identity:development", name: "api-service");
                resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("token", "secret-token")
                    .Allow(api.Identity, SecretsVaultResourceOperationPermissions.ReadSecrets);
                resources
                    .AddSecretsVault("secrets-vault:other", "Other Secrets")
                    .WithSecret("token", "other-token");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var context = new ResourceSettingResolutionContext(
            "application:api",
            "group-1",
            "run",
            ResourceIdentityReference.ForResource("application:api", "api-service"));

        var allowed = await provider.ResolveSecretAsync(
            new SecretReference("secrets-vault:app", "token"),
            context);
        var denied = await provider.ResolveSecretAsync(
            new SecretReference("secrets-vault:other", "token"),
            context);

        Assert.True(allowed.IsResolved);
        Assert.Equal("secret-token", allowed.Value);
        Assert.False(denied.IsResolved);
        Assert.Contains("is not allowed to read secrets", denied.ErrorMessage);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsSecretResolutionFailureBeforeStart()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var api = resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithIdentity("identity:development", name: "api-service")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:app", "sample-api-key"));

                resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("sample-api-key", "secret");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    provider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [provider]);

        var exception = await Assert.ThrowsAsync<ResourceSettingResolutionException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Run));

        Assert.Equal("SAMPLE_API_KEY", exception.SettingName);
        Assert.Equal("secret", exception.ReferenceKind);
        Assert.Contains("is not allowed to read secrets", exception.Message);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingSecretReferenceTargetAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .Resources(resources =>
            {
                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:missing", "sample-api-key"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    provider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Run);

        Assert.Equal(
            "Setting 'SAMPLE_API_KEY' references Secrets Vault 'secrets-vault:missing', but that resource is not available.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingSecretGrantAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .AddSecretsProvider()
            .Resources(resources =>
            {
                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithIdentity("identity:development", name: "api-service")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:app", "sample-api-key"));

                resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("sample-api-key", "secret");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var resource = Assert.Single(resources, resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    provider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore(resources, providers);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Run);

        Assert.NotNull(reason);
        Assert.Contains("Setting 'SAMPLE_API_KEY' references 'secrets-vault:app'", reason);
        Assert.Contains("identity 'application:api/api-service' is not allowed to read secrets", reason);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, reason);
    }

    [Fact]
    public async Task LocalProcessRunner_DisposeStopsControlPlaneScopedProcesses()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var options = new LocalProcessOptions
        {
            RuntimeStatePath = "application-runtime-state.json",
            LogDirectory = "application-logs"
        };
        var environment = new TestHostEnvironment(contentRoot);
        var runtimeStates = new ApplicationRuntimeStateStore(options, environment);
        var definition = CreateLongRunningProcessDefinition();
        Process? process = null;

        try
        {
            var runner = new LocalProcessRunner(runtimeStates, options, environment);
            await runner.StartAsync(definition);
            var runtimeState = runtimeStates.Get(definition.Id);
            Assert.NotNull(runtimeState?.LastKnownProcessId);
            process = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            Assert.False(process.HasExited);

            runner.Dispose();

            Assert.True(await WaitForExitAsync(process, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LocalProcessRunner_CleanupHostScopedProcessStopsRecoveredProcess()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var options = new LocalProcessOptions
        {
            RuntimeStatePath = "application-runtime-state.json",
            LogDirectory = "application-logs"
        };
        var environment = new TestHostEnvironment(contentRoot);
        var runtimeStates = new ApplicationRuntimeStateStore(options, environment);
        var definition = CreateLongRunningProcessDefinition();
        LocalProcessRunner? firstRunner = null;
        LocalProcessRunner? recoveryRunner = null;
        Process? process = null;

        try
        {
            firstRunner = new LocalProcessRunner(runtimeStates, options, environment);
            await firstRunner.StartAsync(definition);
            var runtimeState = runtimeStates.Get(definition.Id);
            Assert.NotNull(runtimeState?.LastKnownProcessId);
            process = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            Assert.False(process.HasExited);

            recoveryRunner = new LocalProcessRunner(runtimeStates, options, environment);
            await recoveryRunner.CleanupHostScopedProcessAsync(definition);

            Assert.True(await WaitForExitAsync(process, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            firstRunner?.Dispose();
            recoveryRunner?.Dispose();

            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void ProgrammaticApplicationResources_DefaultToControlPlaneScopedLifetime()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .Resources(resources =>
            {
                resources.AddExecutableApplication(
                    "application:worker",
                    "Worker",
                    executablePath: "dotnet");
                resources.AddAspNetCoreProject(
                    "application:api",
                    "API",
                    "src/API/API.csproj");
                resources.AddContainer(
                    "redis",
                    "redis:7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();

        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:worker")?.Lifetime);
        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:api")?.Lifetime);
        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:redis")?.Lifetime);
    }

    [Fact]
    public void ApplicationProvider_ProjectsFreshStartingRuntimeState()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .Resources(resources =>
            {
                resources.AddExecutableApplication(
                    "application:api",
                    "API",
                    executablePath: "dotnet");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var runtimeStates = serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>();
        runtimeStates.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            DateTimeOffset.UtcNow,
            State: ResourceState.Starting));
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();

        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");

        Assert.Equal(ResourceState.Starting, resource.State);
        Assert.True(resource.HasAction(ResourceActionIds.Stop));
        Assert.True(resource.HasAction(ResourceActionIds.Restart));
        Assert.False(resource.HasAction(ResourceActionIds.Run));
    }

    [Fact]
    public void ApplicationProvider_IgnoresStaleStartingRuntimeState()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            })
            .Resources(resources =>
            {
                resources.AddExecutableApplication(
                    "application:api",
                    "API",
                    executablePath: "dotnet");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var runtimeStates = serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>();
        runtimeStates.Save(new ApplicationRuntimeState(
            "application:api",
            null,
            null,
            DateTimeOffset.UtcNow.Subtract(TimeSpan.FromMinutes(10)),
            State: ResourceState.Starting));
        var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();

        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");

        Assert.Equal(ResourceState.Stopped, resource.State);
        Assert.True(resource.HasAction(ResourceActionIds.Run));
        Assert.False(resource.HasAction(ResourceActionIds.Stop));
        Assert.False(resource.HasAction(ResourceActionIds.Restart));
    }

    [Fact]
    public async Task SecretsVaultProvider_ExportsSecretNamesWithoutValues()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets")
                    .WithSecret("db-password", "local-dev-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resource = Assert.Single(provider.GetResources());

        var template = await provider.ExportAsync(
            resource,
            new ResourceTemplateExportContext(
                new ResourceRegistration(
                    resource.Id,
                    provider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    []),
                null));

        var secret = Assert.Single(template.Configuration.GetProperty("secrets").EnumerateArray());

        Assert.Equal(SecretsVaultProvider.ProviderId, template.ProviderId);
        Assert.Equal(SecretsVaultProvider.ResourceType, template.ResourceType);
        Assert.Equal("db-password", secret.GetProperty("name").GetString());
        Assert.Equal(string.Empty, secret.GetProperty("value").GetString());
    }

    [Fact]
    public async Task SecretsVaultProvider_ManagesUiCreatedVaults()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var registrations = new MutableResourceRegistrationStore();

        await provider.SetupVaultAsync(
            new SecretsVaultDefinition(
                "secrets-vault:ui",
                "UI Vault",
                [new("token", "initial")]),
            "group-a",
            registrations);

        await provider.UpdateVaultAsync(
            new SecretsVaultDefinition(
                "secrets-vault:ui",
                "Updated UI Vault",
                [new("token", "rotated", "v2")]),
            "group-b",
            registrations);

        var vault = provider.GetVault("secrets-vault:ui");
        var registration = registrations.GetRegistration("secrets-vault:ui");
        var resource = Assert.Single(provider.GetResources());
        await provider.DeleteAsync(
            new ResourceProcedureContext(
                resource,
                registration,
                registration?.ResourceGroupId,
                registrations));

        Assert.NotNull(vault);
        Assert.Equal("Updated UI Vault", vault.Name);
        var secret = Assert.Single(vault.Secrets);
        Assert.Equal("token", secret.Name);
        Assert.Equal("rotated", secret.Value);
        Assert.Equal("v2", secret.Version);
        Assert.NotNull(registration);
        Assert.Equal("group-b", registration.ResourceGroupId);
        Assert.Null(provider.GetVault("secrets-vault:ui"));
        Assert.Null(registrations.GetRegistration("secrets-vault:ui"));
    }

    [Fact]
    public async Task ConfigurationProvider_ExposesStoreLogs()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.DefinitionsPath = "configuration-stores.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        serviceProvider.GetRequiredService<ConfigurationStore>().Save(
            new ConfigurationStoreDefinition(
                "configuration:example",
                "Example Configuration"));

        var log = Assert.Single(provider.GetLogs());
        var entries = await provider.ReadLogAsync(log.Id);

        Assert.Equal("configuration:example", log.ResourceId);
        Assert.Equal(LogSourceKind.Resource, log.SourceKind);
        Assert.True(log.SupportsStreaming);
        Assert.Empty(entries);
    }

    [Fact]
    public void ConfigurationProvider_ResolvesConfigurationEntries()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.DefinitionsPath = "configuration-stores.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        serviceProvider.GetRequiredService<ConfigurationStore>().Save(
            new ConfigurationStoreDefinition(
                "configuration:app",
                "App Settings",
                [
                    new("Database:Name", "appdb"),
                    new("Database:Password", "secret", IsSecret: true)
                ]));
        var resolver = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        var context = new ResourceSettingResolutionContext("application:api", "group-1", "run");

        var resolved = resolver.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:app", "Database:Name"),
            context);
        var legacySecret = resolver.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:app", "Database:Password"),
            context);

        Assert.True(resolved.IsResolved);
        Assert.Equal("appdb", resolved.Value);
        Assert.True(legacySecret.IsResolved);
        Assert.Equal("secret", legacySecret.Value);
        Assert.DoesNotContain(
            resolver.GetStore("configuration:app")!.Entries,
            entry => entry.IsSecret);
    }

    [Fact]
    public void ConfigurationProvider_RequiresGrantForIdentityBoundEntryResolution()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var api = resources
                    .Declare("applications", "application:api")
                    .WithIdentity("identity:development", name: "api-service");
                resources
                    .AddConfigurationStore("configuration:app", "App Settings")
                    .WithEntry("Message", "hello")
                    .Allow(api.Identity, ConfigurationStoreResourceOperationPermissions.ReadEntries);
                resources
                    .AddConfigurationStore("configuration:other", "Other Settings")
                    .WithEntry("Message", "other");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        var context = new ResourceSettingResolutionContext(
            "application:api",
            "group-1",
            "run",
            ResourceIdentityReference.ForResource("application:api", "api-service"));

        var allowed = resolver.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:app", "Message"),
            context);
        var denied = resolver.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:other", "Message"),
            context);

        Assert.True(allowed.IsResolved);
        Assert.Equal("hello", allowed.Value);
        Assert.False(denied.IsResolved);
        Assert.Contains("is not allowed to read configuration entries", denied.ErrorMessage);
    }

    [Fact]
    public void HostConfigurationSourceProvider_ResolvesOnlyExplicitlyExposedEntries()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ExternalApi:BaseUrl"] = "https://api.example.test",
                ["ConnectionStrings:Default"] = "Server=localhost"
            })
            .Build();

        services.AddSingleton<IConfiguration>(configuration);
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                resources
                    .AddHostConfigurationSource("configuration:host-dev", "Host Development Settings")
                    .WithEntry("ExternalApi:BaseUrl");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<HostConfigurationSourceProvider>();
        var context = new ResourceSettingResolutionContext("application:api", "group-1", "run");

        var resolved = provider.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:host-dev", "ExternalApi:BaseUrl"),
            context);
        var rejected = provider.ResolveConfigurationEntry(
            new ConfigurationEntryReference("configuration:host-dev", "ConnectionStrings:Default"),
            context);

        Assert.True(resolved.IsResolved);
        Assert.Equal("https://api.example.test", resolved.Value);
        Assert.False(rejected.IsResolved);
        Assert.Contains("is not exposed", rejected.ErrorMessage);
    }

    [Fact]
    public void TypedExecutableBuilder_CanUseHostConfigurationEntryReferences()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var hostSettings = resources
                    .AddHostConfigurationSource("configuration:host-dev", "Host Development Settings")
                    .WithEntry("ExternalApi:BaseUrl");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithAppSetting("ExternalApi:BaseUrl", hostSettings.Entry("ExternalApi:BaseUrl"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal(["configuration:host-dev"], declaration.DependsOn);
        var setting = Assert.Single(application.AppSettings);
        Assert.Equal("ExternalApi:BaseUrl", setting.Name);
        Assert.Equal("configuration:host-dev", setting.ConfigurationEntry?.StoreResourceId);
        Assert.Equal("ExternalApi:BaseUrl", setting.ConfigurationEntry?.EntryName);
    }

    [Fact]
    public void TypedExecutableBuilder_SeparatesReferencesFromWaitDependencies()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources.AddConfigurationStore(
                    "configuration:settings",
                    "Settings");
                var postgres = resources.Declare("managed", "postgres-main");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithReference(settings)
                    .DependsOn(postgres)
                    .WithServiceDiscovery();
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal(["postgres-main"], declaration.DependsOn);
        Assert.Equal(["configuration:settings"], application.References);
        Assert.True(application.UseServiceDiscovery);
    }

    [Fact]
    public void TypedExecutableBuilder_StoresAppSettingsAndReferenceBackedEnvironmentVariables()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources
                    .AddConfigurationStore("configuration:app", "App Settings");
                var secrets = resources
                    .AddSecretsVault("secrets-vault:app", "App Secrets");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        "API",
                        executablePath: "dotnet")
                    .WithAppSetting("Database:Host", settings.Entry("Database:Host"))
                    .WithEnvironment(
                        "DB_PASSWORD",
                        secrets.Secret("db-password"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal(["configuration:app", "secrets-vault:app"], declaration.DependsOn);
        var appSetting = Assert.Single(application.AppSettings);
        Assert.Equal("Database:Host", appSetting.Name);
        Assert.Equal("configuration:app", appSetting.ConfigurationEntry?.StoreResourceId);
        Assert.Equal("Database:Host", appSetting.ConfigurationEntry?.EntryName);
        var environment = Assert.Single(application.EnvironmentVariables);
        Assert.Equal("DB_PASSWORD", environment.Name);
        Assert.Equal("secrets-vault:app", environment.Secret?.VaultResourceId);
        Assert.Equal("db-password", environment.Secret?.SecretName);
    }

    [Fact]
    public async Task ApplicationProvider_ConfiguresEnvironmentVariablesAsResourceCapability()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
            var configurationProvider = Assert.Single(
                serviceProvider.GetServices<IResourceEnvironmentVariableConfigurationProvider>());
            var registrations = new MutableResourceRegistrationStore();

            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    "dotnet",
                    dependsOn: ["postgres:db"],
                    environmentVariables:
                    [
                        new EnvironmentVariableAssignment("ASPNETCORE_ENVIRONMENT", "Development")
                    ]),
                resourceGroupId: null,
                registrations);

            var resource = Assert.Single(provider.GetResources());
            Assert.True(resource.HasCapability(ResourceCapabilityIds.EnvironmentVariables));
            Assert.True(configurationProvider.CanConfigureEnvironmentVariables(resource));

            var result = await configurationProvider.UpdateEnvironmentVariablesAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations),
                [
                    new EnvironmentVariableAssignment("ASPNETCORE_ENVIRONMENT", "Staging"),
                    EnvironmentVariableAssignment.FromConfiguration(
                        "ConnectionStrings__Default",
                        new ConfigurationEntryReference("configuration:app", "ConnectionStrings:Default")),
                    EnvironmentVariableAssignment.FromSecret(
                        "EXTERNAL_API_KEY",
                        new SecretReference("secrets-vault:app", "ExternalApiKey"))
                ]);

            var application = provider.GetApplication("application:api");
            var registration = registrations.GetRegistration("application:api");

            Assert.Equal("Environment variables updated.", result.Message);
            Assert.Equal(
                ["postgres:db", "configuration:app", "secrets-vault:app"],
                application?.DependsOn);
            Assert.Equal(
                ["postgres:db", "configuration:app", "secrets-vault:app"],
                registration?.DependsOn);
            Assert.Contains(
                application?.EnvironmentVariables ?? [],
                variable => variable.Name == "ConnectionStrings__Default" &&
                    variable.ConfigurationEntry?.StoreResourceId == "configuration:app" &&
                    variable.ConfigurationEntry.EntryName == "ConnectionStrings:Default");
            Assert.Contains(
                application?.EnvironmentVariables ?? [],
                variable => variable.Name == "EXTERNAL_API_KEY" &&
                    variable.Secret?.VaultResourceId == "secrets-vault:app" &&
                    variable.Secret.SecretName == "ExternalApiKey");
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ApplicationProvider_ConfiguresAppSettingsWithReferences()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
            var configurationProvider = Assert.Single(
                serviceProvider.GetServices<IResourceAppSettingConfigurationProvider>());
            var registrations = new MutableResourceRegistrationStore();

            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    "dotnet",
                    dependsOn: ["postgres:db"],
                    environmentVariables:
                    [
                        EnvironmentVariableAssignment.FromSecret(
                            "EXTERNAL_API_KEY",
                            new SecretReference("secrets-vault:env", "ExternalApiKey"))
                    ]),
                resourceGroupId: null,
                registrations);

            var resource = Assert.Single(provider.GetResources());
            Assert.True(configurationProvider.CanConfigureAppSettings(resource));

            var result = await configurationProvider.UpdateAppSettingsAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations),
                [
                    AppSetting.Literal("ASPNETCORE_ENVIRONMENT", "Staging"),
                    AppSetting.FromConfiguration(
                        "ConnectionStrings:Default",
                        new ConfigurationEntryReference("configuration:app", "ConnectionStrings:Default")),
                    AppSetting.FromSecret(
                        "ExternalApi:Key",
                        new SecretReference("secrets-vault:app", "ExternalApiKey"))
                ]);

            var application = provider.GetApplication("application:api");
            var registration = registrations.GetRegistration("application:api");

            Assert.Equal("App settings updated.", result.Message);
            Assert.Equal(
                ["postgres:db", "configuration:app", "secrets-vault:app", "secrets-vault:env"],
                application?.DependsOn);
            Assert.Equal(
                ["postgres:db", "configuration:app", "secrets-vault:app", "secrets-vault:env"],
                registration?.DependsOn);
            Assert.Contains(
                application?.AppSettings ?? [],
                setting => setting.Name == "ConnectionStrings:Default" &&
                    setting.ConfigurationEntry?.StoreResourceId == "configuration:app" &&
                    setting.ConfigurationEntry.EntryName == "ConnectionStrings:Default");
            Assert.Contains(
                application?.AppSettings ?? [],
                setting => setting.Name == "ExternalApi:Key" &&
                    setting.Secret?.VaultResourceId == "secrets-vault:app" &&
                    setting.Secret.SecretName == "ExternalApiKey");
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TypedExecutableBuilder_CanDependOnContainerResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var redis = resources
                    .AddDocker()
                    .AddContainer("redis", "redis", "7.2");

                resources
                    .AddExecutableApplication(
                        "application:web",
                        "Web",
                        executablePath: "dotnet")
                    .DependsOn(redis);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:web");

        Assert.Equal(["docker:container:redis"], declaration.DependsOn);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_UsesDotNetWatchByDefault()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources.AddConfigurationStore(
                    "configuration:settings",
                    "Settings");

                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj",
                        endpoint: "http://localhost:5127")
                    .WithReference(settings);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(
            store.GetDeclarations(),
            declaration => declaration.ResourceId == "application:api");
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, application.ResourceType);
        Assert.Equal("src/API/API.csproj", application.ProjectPath);
        Assert.Null(application.ProjectArguments);
        Assert.True(application.AspNetCoreHotReload);
        Assert.Empty(application.ExecutablePath);
        Assert.Null(application.Arguments);
        Assert.Null(application.Endpoint);
        Assert.DoesNotContain(
            application.EnvironmentVariables,
            variable => variable.Name == "ASPNETCORE_URLS");
        var port = Assert.Single(application.EndpointPorts);
        Assert.Equal("http", port.Name);
        Assert.Equal(5127, port.TargetPort);
        Assert.Equal(5127, port.Port);
        Assert.Equal("http", port.Protocol);
        Assert.Equal(["configuration:settings"], application.References);
        Assert.True(application.UseServiceDiscovery);

    }

    [Fact]
    public async Task ApplicationProvider_AddsObservabilityMetadataAndOtelEnvironment()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));

        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
                options.OtlpEndpoint = "http://localhost:4317";
            })
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj")
                    .WithOtlpExporter(headers: "x-otlp-api-key=test-key");
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceProvider>();
            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "application:api");
            var descriptor = await provider.DescribeAsync(
                resource,
                new ResourceOrchestrationDescriptorContext(null, null, null!));
            var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var environment = workload?.WorkloadEnvironmentVariables
                .ToDictionary(variable => variable.Name, variable => variable.Value);

            Assert.True(resource.EffectiveObservability.Logs);
            Assert.True(resource.EffectiveObservability.Traces);
            Assert.True(resource.EffectiveObservability.Metrics);
            Assert.Equal(ResourceClass.Project, resource.ResourceClass);
            Assert.Equal(ResourceWorkloadKind.AspNetCoreProject.ToString(), resource.ResourceAttributes[ResourceAttributeNames.WorkloadKind]);
            Assert.Equal("src/API/API.csproj", resource.ResourceAttributes[ResourceAttributeNames.ProjectPath]);
            Assert.Equal("true", resource.ResourceAttributes[ResourceAttributeNames.ProjectHotReload]);
            Assert.False(resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.ExecutablePath));
            Assert.False(resource.ResourceAttributes.ContainsKey(ResourceAttributeNames.ExecutableArguments));
            Assert.Equal(ResourceWorkloadKind.AspNetCoreProject, workload?.Kind);
            Assert.Equal("src/API/API.csproj", workload?.ProjectPath);
            Assert.Equal("http://localhost:4317", environment?["OTEL_EXPORTER_OTLP_ENDPOINT"]);
            Assert.Equal("grpc", environment?["OTEL_EXPORTER_OTLP_PROTOCOL"]);
            Assert.Equal("x-otlp-api-key=test-key", environment?["OTEL_EXPORTER_OTLP_HEADERS"]);
            Assert.Equal("api", environment?["OTEL_SERVICE_NAME"]);
            Assert.Contains("cloudshell.resource.id=application:api", environment?["OTEL_RESOURCE_ATTRIBUTES"]);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDeclareNamedEndpoints()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj")
                    .WithHttpEndpoint(port: 5127)
                    .WithEndpointPort(
                        "dashboard",
                        targetPort: 18888,
                        port: 18888,
                        protocol: "http");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Collection(
            application.EndpointPorts.OrderBy(port => port.Name, StringComparer.OrdinalIgnoreCase),
            port =>
            {
                Assert.Equal("dashboard", port.Name);
                Assert.Equal(18888, port.TargetPort);
                Assert.Equal(18888, port.Port);
                Assert.Equal("http", port.Protocol);
            },
            port =>
            {
                Assert.Equal("http", port.Name);
                Assert.Equal(80, port.TargetPort);
                Assert.Equal(5127, port.Port);
                Assert.Equal("http", port.Protocol);
            });
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDeclareHealthChecks()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj")
                    .WithHttpHealthCheck("/health")
                    .WithHttpProbe(ResourceProbeType.Liveness, "/alive");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Collection(
            application.HealthChecks,
            check =>
            {
                Assert.Equal("/health", check.Path);
                Assert.Equal(ResourceProbeType.Health, check.Type);
                Assert.Equal("health", check.Name);
            },
            check =>
            {
                Assert.Equal("/alive", check.Path);
                Assert.Equal(ResourceProbeType.Liveness, check.Type);
                Assert.Equal("liveness", check.Name);
            });
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_AssignsEndpointPortWhenOmitted()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
                    "API",
                    "src/API/API.csproj");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));
        var port = Assert.Single(application.EndpointPorts);

        Assert.Equal("http", port.Name);
        Assert.Equal(80, port.TargetPort);
        Assert.Null(port.Port);
        Assert.Equal("http", port.Protocol);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_UsesStableHttpEndpointNameForHttpsEndpoint()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
                    "API",
                    "src/API/API.csproj",
                    endpoint: "https://localhost:5127");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));
        var port = Assert.Single(application.EndpointPorts);

        Assert.Equal("http", port.Name);
        Assert.Equal(5127, port.TargetPort);
        Assert.Equal(5127, port.Port);
        Assert.Equal("https", port.Protocol);
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDisableHotReload()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj",
                        hotReload: false)
                    .WithApplicationArguments("--seed");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal("src/API/API.csproj", application.ProjectPath);
        Assert.Equal("--seed", application.ProjectArguments);
        Assert.False(application.AspNetCoreHotReload);
        Assert.Null(application.Arguments);
    }

    [Fact]
    public async Task TypedDockerContainerBuilder_DeclaresEngineAndContainer()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var postgres = resources.Declare("managed", "postgres-main");

                var container = resources
                    .AddDocker()
                    .WithRegistry("http://registry.local:5000")
                    .WithRegistryCredentialsFromEnvironment("registry-user", "REGISTRY_PASSWORD")
                    .AddContainer("redis", "redis", "7.2")
                    .WithLifetime(ResourceLifetime.Detached)
                    .DependsOn(postgres)
                    .WithResourceGroup("group-1");

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IDockerContainerResourceBuilder>>(container);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declarations = store.GetDeclarations();
        var host = Assert.Single(declarations, declaration =>
            declaration.ResourceId == DockerContainerResourceProvider.DefaultHostResourceId);
        var container = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis");
        var options = serviceProvider.GetRequiredService<DockerProviderOptions>();
        var declaredContainers = options
            .GetType()
            .GetProperty("DeclaredContainers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredContainer = Assert.Single(declaredContainers!.Cast<object>());
        var definition = Assert.IsType<DockerContainerResourceDefinition>(
            declaredContainer
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredContainer));

        Assert.Equal("docker", host.ProviderId);
        Assert.Empty(host.DependsOn);
        Assert.Equal("docker", container.ProviderId);
        Assert.Equal(DockerContainerResourceProvider.DefaultHostResourceId, container.ParentResourceId);
        Assert.Equal("group-1", container.ResourceGroupId);
        Assert.Equal(
            [DockerContainerResourceProvider.DefaultHostResourceId, "postgres-main"],
            container.DependsOn);
        Assert.Equal("redis", definition.Name);
        Assert.Equal("redis:7.2", definition.Image);
        Assert.Equal("http://registry.local:5000", definition.Registry);
        Assert.Equal("registry-user", definition.RegistryCredentials?.Username);
        Assert.Equal("REGISTRY_PASSWORD", definition.RegistryCredentials?.PasswordEnvironmentVariable);
        Assert.Equal(DockerContainerResourceProvider.DefaultHostResourceId, definition.DockerResourceId);
        Assert.Equal(container.DependsOn, definition.DependsOn);
        Assert.Equal(ResourceLifetime.Detached, definition.Lifetime);

        using var provider = new DockerContainerResourceProvider(options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "docker:container:redis");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
        Assert.Equal("http://registry.local:5000", workload?.Registry);
        Assert.Equal("http://registry.local:5000", resource.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
    }

    [Fact]
    public void TypedDockerBuilder_ParentsContainersUnderSpecificDockerResource()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDocker("docker:dev", "Development Docker")
                    .AddContainer("redis-dev", "redis", "7.2");

                resources
                    .AddDocker("docker:test", "Test Docker")
                    .AddContainer("redis-test", "redis", "7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declarations = store.GetDeclarations();
        var devDocker = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:dev");
        var testDocker = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:test");
        var devContainer = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis-dev");
        var testContainer = Assert.Single(declarations, declaration =>
            declaration.ResourceId == "docker:container:redis-test");
        using var provider = new DockerContainerResourceProvider(
            serviceProvider.GetRequiredService<DockerProviderOptions>());
        var resources = provider.GetResources();
        var devContainerResource = Assert.Single(resources, resource =>
            resource.Id == "docker:container:redis-dev");
        var testContainerResource = Assert.Single(resources, resource =>
            resource.Id == "docker:container:redis-test");

        Assert.Equal("docker", devDocker.ProviderId);
        Assert.Equal("docker", testDocker.ProviderId);
        Assert.Equal("docker:dev", devContainer.ParentResourceId);
        Assert.Equal("docker:test", testContainer.ParentResourceId);
        Assert.Equal(["docker:dev"], devContainer.DependsOn);
        Assert.Equal(["docker:test"], testContainer.DependsOn);
        Assert.Equal("docker:dev", devContainerResource.ParentResourceId);
        Assert.Equal("docker:test", testContainerResource.ParentResourceId);
    }

    [Fact]
    public async Task DockerProvider_DescribesDefaultHostAsGenericContainerHost()
    {
        using var provider = new DockerContainerResourceProvider(new DockerProviderOptions());
        var host = Assert.Single(provider.GetResources(), resource =>
            resource.Id == DockerContainerResourceProvider.DefaultHostResourceId);

        Assert.True(provider.CanDescribe(host));

        var descriptor = await provider.DescribeAsync(
            host,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var definition = descriptor.Configuration.Deserialize<ContainerHostDescriptor>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ContainerHostResourceTypes.ContainerHost, descriptor.ResourceType);
        Assert.NotNull(definition);
        Assert.Equal(ContainerHostKind.Docker, definition.Kind);
        Assert.True(definition.IsDefault);
        Assert.Equal(provider.Endpoint.ToString(), definition.Endpoint);
        Assert.Equal(ContainerRegistryDefaults.Default, definition.Registry);
        Assert.Equal(
            [ContainerHostCapabilityIds.ContainerBuild, ContainerHostCapabilityIds.ContainerImage],
            definition.HostCapabilities.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(DockerContainerResourceProvider.HostResourceType, host.EffectiveTypeId);
        Assert.Equal("local", host.ResourceAttributes["docker.host.kind"]);
        Assert.Equal(ContainerRegistryDefaults.Default, host.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
    }

    [Fact]
    public void DockerBuilder_DeclaresRemoteHostWithRedactedProjectedAttributes()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDocker("docker:build-01", "Build Host 01")
                    .UseRemoteHost(new Uri("tcp://user:secret@Build-01.Example.com:2375/?debug=true"))
                    .WithHostCredentialsFromEnvironment("docker-user", "DOCKER_HOST_PASSWORD")
                    .AddContainer("redis", "redis", "7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        using var provider = new DockerContainerResourceProvider(
            serviceProvider.GetRequiredService<DockerProviderOptions>());
        var host = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "docker:build-01");
        var container = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "docker:container:redis");

        Assert.Equal(DockerContainerResourceProvider.HostResourceType, host.EffectiveTypeId);
        Assert.Equal(ResourceClass.Infrastructure, host.ResourceClass);
        Assert.Equal("remote", host.ResourceAttributes["docker.host.kind"]);
        Assert.Equal("tcp://build-01.example.com", host.ResourceAttributes["docker.host.endpoint"]);
        Assert.DoesNotContain("secret", host.PrimaryEndpoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DOCKER_HOST_PASSWORD", host.ResourceAttributes.Values);
        Assert.Equal("docker:build-01", container.ParentResourceId);
    }

    [Fact]
    public async Task DockerProvider_RejectsDuplicateHostIdentityWithinResourceGroup()
    {
        using var provider = new DockerContainerResourceProvider(new DockerProviderOptions());
        var registrations = new MutableResourceRegistrationStore();
        var endpoint = new Uri("tcp://build-01.example.com:2375");

        await provider.SetupHostAsync(
            "docker:build-a",
            "Build A",
            DockerHostDefinition.Remote(endpoint),
            "team-a",
            registrations);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupHostAsync(
                "docker:build-b",
                "Build B",
                DockerHostDefinition.Remote(new Uri("tcp://BUILD-01.example.com:2375/")),
                "team-a",
                registrations));

        await provider.SetupHostAsync(
            "docker:build-c",
            "Build C",
            DockerHostDefinition.Remote(endpoint),
            "team-b",
            registrations);

        Assert.Contains("already registered", exception.Message);
        Assert.Equal(2, registrations.GetRegistrations().Count);
    }

    [Fact]
    public void UseDocker_RegistersImplicitContainerHostWithoutResourceDeclaration()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .UseDocker();

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider
            .GetRequiredService<ResourceDeclarationStore>()
            .GetDeclarations();
        var containerHost = Assert.Single(serviceProvider.GetServices<IContainerHostProvider>())
            .GetDefaultHost();

        Assert.Empty(declarations);
        Assert.Equal("docker", containerHost.Id);
        Assert.Equal(ContainerHostKind.Docker, containerHost.Kind);
        Assert.True(containerHost.IsDefault);
        Assert.Equal(ContainerRegistryDefaults.Default, containerHost.Registry);
        Assert.Equal(
            [ContainerHostCapabilityIds.ContainerBuild, ContainerHostCapabilityIds.ContainerImage],
            containerHost.HostCapabilities.Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void TypedDockerContainerBuilder_NormalizesExplicitContainerId()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDockerContainer("rabbitmq", "RabbitMQ", "rabbitmq:4")
                    .DependsOn("configuration:settings")
                    .DependsOn(DockerContainerResourceProvider.DefaultHostResourceId);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var container = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "docker:container:rabbitmq");

        Assert.Equal(
            [DockerContainerResourceProvider.DefaultHostResourceId, "configuration:settings"],
            container.DependsOn);
    }

    [Fact]
    public void PlatformResources_DeclareNetworkAndServiceWithExposure()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var network = resources.AddNetwork("network:app", "App Network");
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddService("service:api", "API")
                    .Targets(api)
                    .WithNetwork(network)
                    .WithPort(
                        "http",
                        targetPort: 8080,
                        port: 5080,
                        protocol: "http",
                        exposure: ResourceExposureScope.Public);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var service = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "service:api");

        Assert.Equal(PlatformResourceProvider.ProviderId, service.ProviderId);
        Assert.Equal(["application:api", "network:app"], service.DependsOn);

        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var serviceDefinition = Assert.Single(options.DeclaredServices).Definition;

        Assert.Equal("service:api", serviceDefinition.Id);
        Assert.Equal([new ServiceTarget("application:api")], serviceDefinition.Targets);
        Assert.Equal(["network:app"], serviceDefinition.NetworkIds);

        var port = Assert.Single(serviceDefinition.Ports);
        Assert.Equal("http", port.Name);
        Assert.Equal(8080, port.TargetPort);
        Assert.Equal(5080, port.Port);
        Assert.Equal(ResourceExposureScope.Public, port.Exposure);
    }

    [Fact]
    public void Resources_RegisterProgrammaticIdentityProviderAndDefault()
    {
        var services = new ServiceCollection();
        ResourceIdentityProviderDefinition? provider = null;

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                provider = resources.AddIdentityProvider(
                    "identity:dev",
                    "Development Identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    useAsDefault: true);

                resources
                    .Declare("applications", "application:api")
                    .WithIdentity(provider);

                resources
                    .Declare("applications", "application:worker")
                    .RequireIdentity(name: "worker-service");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var catalog = declarations.CreateIdentityProviderCatalog(new ResourceIdentityProviderCatalog());

        Assert.NotNull(provider);
        Assert.Equal("identity:dev", declarations.DefaultIdentityProviderId);
        Assert.Equal("identity:dev", catalog.DefaultProviderId);
        Assert.Equal("identity:dev", catalog.GetProvider("identity:dev")?.Id);

        var api = declarations.GetDeclaration("application:api");
        var worker = declarations.GetDeclaration("application:worker");
        Assert.NotNull(api);
        Assert.NotNull(worker);
        Assert.Equal("identity:dev", api.IdentityBinding?.ProviderId);
        Assert.Equal("worker-service", worker.IdentityBinding?.Name);
        Assert.Equal("identity:dev", catalog.Resolve(worker.IdentityBinding!).Provider?.Id);
    }

    [Fact]
    public void PlatformResources_DeclareNetworkEndpointRequestsAndMappings()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var network = resources
                    .AddNetwork("network:app", "App Network", isDefault: true);
                var api = resources.Declare("applications", "application:api");
                var proxy = resources.Declare("networking", "networking:proxy");
                var publicEndpoint = network.AddTcpEndpoint("localhost", 4040, "public");
                var autoEndpoint = network.RequestHttpEndpoint("api");

                network.MapEndpoint(
                    autoEndpoint,
                    new ResourceEndpointReference(api.ResourceId, "http"),
                    proxy,
                    "mapping:api");

                Assert.Equal(new ResourceEndpointReference("network:app", "public"), publicEndpoint);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var networkDeclaration = declarations.GetDeclaration("network:app");
        Assert.NotNull(networkDeclaration);
        Assert.Equal(["application:api", "networking:proxy"], networkDeclaration.DependsOn);

        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var definition = Assert.Single(options.DeclaredNetworks).Definition;

        Assert.Collection(
            definition.NetworkEndpoints.OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase),
            endpoint =>
            {
                Assert.Equal("api", endpoint.Name);
                Assert.Equal(ResourceEndpointProtocol.Http, endpoint.Protocol);
                Assert.Equal(ResourceEndpointAssignment.Auto, endpoint.Assignment);
                Assert.Equal("localhost", endpoint.Host);
            },
            endpoint =>
            {
                Assert.Equal("public", endpoint.Name);
                Assert.Equal(ResourceEndpointProtocol.Tcp, endpoint.Protocol);
                Assert.Equal(ResourceEndpointAssignment.Manual, endpoint.Assignment);
                Assert.Equal("localhost", endpoint.Host);
                Assert.Equal(4040, endpoint.Port);
            });

        var mapping = Assert.Single(definition.NetworkEndpointMappings);
        Assert.Equal("mapping:api", mapping.Id);
        Assert.Equal(new ResourceEndpointReference("network:app", "api"), mapping.Source);
        Assert.Equal(new ResourceEndpointReference("application:api", "http"), mapping.Target);
        Assert.Equal("network:app", mapping.NetworkResourceId);
        Assert.Equal("networking:proxy", mapping.ProviderResourceId);

        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "network:app");

        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingProvider));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointProvider));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper));
        Assert.Collection(
            resource.Endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase),
            endpoint =>
            {
                Assert.Equal("api", endpoint.Name);
                Assert.StartsWith("http://localhost:", endpoint.Address);
            },
            endpoint =>
            {
                Assert.Equal("public", endpoint.Name);
                Assert.Equal("tcp://localhost:4040", endpoint.Address);
            });
    }

    [Fact]
    public void PlatformResources_DeclareLoadBalancerRoutes()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var webApp = resources.Declare("applications", "application:web");
                var apiService = resources.Declare("applications", "application:api");
                var postgres = resources.Declare("applications", "application:postgres");
                var dockerHost = resources.Declare("docker", "docker:engine");

                var lb = resources
                    .AddLoadBalancer("public")
                    .UseProvider("traefik")
                    .UseHost(dockerHost)
                    .ExposeHttp(80)
                    .ExposeHttps(443);

                lb.MapHost("app.local", webApp, endpoint: "http");
                lb.MapPath("api.local", "/v1", apiService, port: 5000);
                lb.MapTcp(5432, postgres, endpoint: "postgres");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = declarations.GetDeclaration("load-balancer:public");
        Assert.NotNull(declaration);
        Assert.Equal(
            ["docker:engine", "application:web", "application:api", "application:postgres"],
            declaration.DependsOn);

        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var definition = Assert.Single(options.DeclaredLoadBalancers).Definition;

        Assert.Equal("load-balancer:public", definition.Id);
        Assert.Equal("Public", definition.Name);
        Assert.Equal("traefik", definition.Provider);
        Assert.Equal("docker:engine", definition.HostResourceId);
        Assert.Collection(
            definition.LoadBalancerEntrypoints.OrderBy(entrypoint => entrypoint.Name, StringComparer.OrdinalIgnoreCase),
            entrypoint =>
            {
                Assert.Equal("http", entrypoint.Name);
                Assert.Equal(ResourceEndpointProtocol.Http, entrypoint.Protocol);
                Assert.Equal(80, entrypoint.Port);
            },
            entrypoint =>
            {
                Assert.Equal("https", entrypoint.Name);
                Assert.Equal(ResourceEndpointProtocol.Https, entrypoint.Protocol);
                Assert.Equal(443, entrypoint.Port);
            });
        Assert.Collection(
            definition.LoadBalancerRoutes.OrderBy(route => route.Name, StringComparer.OrdinalIgnoreCase),
            route =>
            {
                Assert.Equal(LoadBalancerRouteKind.Http, route.Kind);
                Assert.Equal("http", route.EntrypointName);
                Assert.Equal("api.local", route.Match.Host);
                Assert.Equal("/v1", route.Match.PathPrefix);
                Assert.Equal("application:api", route.Target.ResourceId);
                Assert.Equal(5000, route.Target.Port);
            },
            route =>
            {
                Assert.Equal(LoadBalancerRouteKind.Http, route.Kind);
                Assert.Equal("http", route.EntrypointName);
                Assert.Equal("app.local", route.Match.Host);
                Assert.Equal("application:web", route.Target.ResourceId);
                Assert.Equal("http", route.Target.EndpointName);
            },
            route =>
            {
                Assert.Equal(LoadBalancerRouteKind.Tcp, route.Kind);
                Assert.Equal("tcp-5432", route.EntrypointName);
                Assert.Equal(5432, route.Match.Port);
                Assert.Equal("application:postgres", route.Target.ResourceId);
                Assert.Equal("postgres", route.Target.EndpointName);
            });

        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "load-balancer:public");

        Assert.Equal(PlatformResourceProvider.LoadBalancerResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, resource.ResourceClass);
        Assert.Equal("traefik", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerProvider]);
        Assert.Equal("docker:engine", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerHostResourceId]);
        Assert.Equal("2", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerEntrypointCount]);
        Assert.Equal("3", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerRouteCount]);
        Assert.Equal("2", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerHttpRouteCount]);
        Assert.Equal("1", resource.ResourceAttributes[ResourceAttributeNames.LoadBalancerTcpRouteCount]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingLoadBalancer));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingGateway));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingTls));
        Assert.Collection(
            resource.Endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase),
            endpoint =>
            {
                Assert.Equal("http", endpoint.Name);
                Assert.Equal("http://localhost:80", endpoint.Address);
            },
            endpoint =>
            {
                Assert.Equal("https", endpoint.Name);
                Assert.Equal("https://localhost:443", endpoint.Address);
            });
    }

    [Fact]
    public void PlatformResources_DeclareVirtualNetworkAsNetworkPrimitive()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var network = resources
                    .AddVirtualNetwork("network:app", "App Network", isDefault: true);
                var apiEndpoint = network.RequestHttpEndpoint(
                    "api",
                    exposure: ResourceExposureScope.Public);

                Assert.Equal(new ResourceEndpointReference("network:app", "api"), apiEndpoint);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declaration = serviceProvider
            .GetRequiredService<ResourceDeclarationStore>()
            .GetDeclaration("network:app");
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var definition = Assert.Single(options.DeclaredNetworks).Definition;
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "network:app");

        Assert.NotNull(declaration);
        Assert.Equal(NetworkResourceKind.Virtual, definition.Kind);
        Assert.True(definition.IsDefault);
        Assert.Equal(PlatformResourceProvider.VirtualNetworkResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, resource.ResourceClass);
        Assert.Equal("Default virtual", resource.ResourceAttributes[ResourceAttributeNames.NetworkKind]);
        Assert.Equal("logicalOnly", resource.ResourceAttributes[ResourceAttributeNames.NetworkHostReadiness]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingProvider));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointProvider));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingVirtualNetwork));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingIngress));
        Assert.StartsWith("http://localhost:", Assert.Single(resource.Endpoints).Address);
    }

    [Fact]
    public void PlatformProvider_ProjectsHostNetworkWhenNoNetworkExists()
    {
        var options = new PlatformResourceOptions();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);

        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == PlatformResourceProvider.HostNetworkResourceId);

        Assert.Equal(PlatformResourceProvider.NetworkResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, resource.ResourceClass);
        Assert.Equal("Host", resource.ResourceAttributes[ResourceAttributeNames.NetworkKind]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingHostNetwork));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointProvider));
        Assert.Equal("network://network:host", Assert.Single(resource.Endpoints).Address);
    }

    [Fact]
    public void PlatformProvider_UsesHostLocalNetworkEnvironmentForDefaultEndpoints()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "api",
                    ResourceEndpointProtocol.Http,
                    Assignment: ResourceEndpointAssignment.Auto)
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(
            platformStore,
            options,
            new TestHostLocalNetworkEnvironment());

        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "network:app");

        Assert.Equal("http://loopback.test:4123", Assert.Single(resource.Endpoints).Address);
    }

    [Fact]
    public async Task PlatformProvider_ReconcilesEndpointMappingsWithSelectedProvider()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            IsDefault: true,
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "api",
                    ResourceEndpointProtocol.Http,
                    Host: "localhost",
                    Assignment: ResourceEndpointAssignment.Auto)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "api"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:proxy")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                CreateNetworkingProviderResource("networking:proxy")
            ],
            [provider]);

        var result = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(
                network,
                new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            network.ResourceActions.Single());

        Assert.Equal("Reconciled 1 endpoint mapping(s).", result.Message);
    }

    [Fact]
    public async Task PlatformProvider_RejectsDuplicatePlatformEndpointAssignment()
    {
        var options = new PlatformResourceOptions();
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var registrations = new MutableResourceRegistrationStore();

        await provider.SetupServiceAsync(
            new ServiceResourceDefinition(
                "service:api",
                "API",
                [new ServiceTarget("application:api")],
                [new ServicePort("http", 8080, 5080, "http", ResourceExposureScope.Public)],
                []),
            null,
            registrations);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupNetworkAsync(
                new NetworkResourceDefinition(
                    "network:app",
                    "App Network",
                    Endpoints:
                    [
                        new ResourceEndpointRequest(
                            "public",
                            ResourceEndpointProtocol.Http,
                            Host: "localhost",
                            Port: 5080,
                            Assignment: ResourceEndpointAssignment.Manual)
                    ]),
                null,
                registrations));

        Assert.Contains("endpoint assignment", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("service:api", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_RejectsEndpointMappingSourceOutsideNetwork()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("service:api", "public"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:proxy")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("service:api", "public", "http://localhost:5080"),
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                CreateNetworkingProviderResource("networking:proxy")
            ],
            [provider]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    network,
                    new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                    null,
                    new TestResourceRegistrationStore([]),
                    resourceManager),
                network.ResourceActions.Single()));

        Assert.Contains("source endpoint", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("network:app", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_RejectsDuplicateEndpointMappingSources()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "public",
                    ResourceEndpointProtocol.Http,
                    Host: "localhost",
                    Port: 5080,
                    Assignment: ResourceEndpointAssignment.Manual)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "public"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:proxy"),
                new ResourceEndpointMappingDefinition(
                    "mapping:web",
                    "Web",
                    new ResourceEndpointReference("network:app", "public"),
                    new ResourceEndpointReference("application:web", "http"),
                    "network:app",
                    "networking:proxy")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                CreateEndpointResource("application:web", "http", "http://localhost:8081"),
                CreateNetworkingProviderResource("networking:proxy")
            ],
            [provider]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    network,
                    new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                    null,
                    new TestResourceRegistrationStore([]),
                    resourceManager),
                network.ResourceActions.Single()));

        Assert.Contains("already used", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mapping:api", exception.Message, StringComparison.Ordinal);
        Assert.Contains("mapping:web", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_ProvisionsVirtualEndpointMappingsWithActivatedProvider()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            IsDefault: true,
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "api",
                    ResourceEndpointProtocol.Http,
                    Host: "localhost",
                    Port: 5089,
                    Assignment: ResourceEndpointAssignment.Manual)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "api"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:host-macos")
            ],
            Kind: NetworkResourceKind.Virtual);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provisioner = new TestEndpointMappingProvisioner();
        var provider = new PlatformResourceProvider(
            store,
            options,
            endpointMappingProvisioners: [provisioner]);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                CreateNetworkingProviderResource("networking:host-macos")
            ],
            [provider]);

        var result = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(
                network,
                new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            network.ResourceActions.Single());

        Assert.Equal("Reconciled 1 endpoint mapping(s), provisioned 1.", result.Message);
        Assert.NotNull(provisioner.Context);
        Assert.Equal("mapping:api", provisioner.Context.Mapping.Id);
        Assert.Equal("network:app", provisioner.Context.NetworkResource.Id);
        Assert.Equal("application:api", provisioner.Context.TargetResource.Id);
        Assert.Equal("networking:host-macos", provisioner.Context.ProviderResource.Id);
    }

    [Fact]
    public async Task PlatformProvider_RejectsVirtualEndpointMappingWhenHostProviderIsNotActivated()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            IsDefault: true,
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "api",
                    ResourceEndpointProtocol.Http,
                    Host: "localhost",
                    Port: 5090,
                    Assignment: ResourceEndpointAssignment.Manual)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "api"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:host-macos")
            ],
            Kind: NetworkResourceKind.Virtual);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                CreateNetworkingProviderResource("networking:host-macos")
            ],
            [provider]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    network,
                    new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                    null,
                    new TestResourceRegistrationStore([]),
                    resourceManager),
                network.ResourceActions.Single()));

        Assert.Contains("no activated host networking service", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_RejectsEndpointMappingProviderWithoutMapperCapability()
    {
        var definition = new NetworkResourceDefinition(
            "network:app",
            "App Network",
            Endpoints:
            [
                new ResourceEndpointRequest(
                    "api",
                    ResourceEndpointProtocol.Http,
                    Host: "localhost",
                    Assignment: ResourceEndpointAssignment.Auto)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "api"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    "networking:proxy")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredNetworks.Add(new DeclaredNetworkResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var network = Assert.Single(provider.GetResources(), resource => resource.Id == "network:app");
        var resourceManager = new StaticResourceManagerStore(
            [
                network,
                CreateEndpointResource("application:api", "http", "http://localhost:8080"),
                new Resource(
                    "networking:proxy",
                    "Proxy",
                    "Proxy",
                    "Test",
                    "local",
                    ResourceState.Running,
                    [],
                    "1.0",
                    DateTimeOffset.UtcNow,
                    [])
            ],
            [provider]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    network,
                    new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                    null,
                    new TestResourceRegistrationStore([]),
                    resourceManager),
                network.ResourceActions.Single()));

        Assert.Contains(ResourceCapabilityIds.NetworkingEndpointMapper, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectResourceBuilder_CanAttachContainerImageForOrchestration()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                IProjectResourceBuilder project = resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "API",
                        "src/API/API.csproj");

                project
                    .AsContainerImage("example/api:dev")
                    .WithReplicas(2);

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IProjectResourceBuilder>>(project);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<ApplicationProviderOptions>();
        var declaredApplications = options
            .GetType()
            .GetProperty("DeclaredApplications", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(options) as System.Collections.IEnumerable;
        var declaredApplication = Assert.Single(declaredApplications!.Cast<object>());
        var application = Assert.IsType<ApplicationResourceDefinition>(
            declaredApplication
                .GetType()
                .GetProperty("Definition")!
                .GetValue(declaredApplication));

        Assert.Equal("example/api:dev", application.ContainerImage);
        Assert.Null(application.ContainerBuildContext);
        Assert.Equal(2, application.Replicas);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_DeclaresTopLevelContainerWorkload()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                var container = resources
                    .AddContainer(
                        "sql",
                        "mcr.microsoft.com/mssql/server:2022-latest",
                        replicas: 3)
                    .WithImage("example/sql-server:dev")
                    .WithRegistry("https://registry.example.com")
                    .WithRegistryCredentialsFromEnvironment("registry-user", "REGISTRY_PASSWORD")
                    .WithEndpoint("tds", targetPort: 1433, port: 14333)
                    .WithContainerHost("docker:dev")
                    .WithLifetime(ResourceLifetime.Detached);

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IContainerResourceBuilder>>(container);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:sql");
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:sql");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("applications", declaration.ProviderId);
        Assert.Null(declaration.ParentResourceId);
        Assert.Empty(declaration.DependsOn);
        Assert.Equal(ApplicationResourceTypes.ContainerApp, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Container, resource.ResourceClass);
        Assert.Equal(
            ResourceWorkloadKind.ContainerImage.ToString(),
            resource.ResourceAttributes[ResourceAttributeNames.WorkloadKind]);
        Assert.Equal("example/sql-server:dev", resource.ResourceAttributes[ResourceAttributeNames.ContainerImage]);
        Assert.Equal("https://registry.example.com", resource.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
        Assert.Equal("docker:dev", resource.ResourceAttributes[ResourceAttributeNames.ContainerHostId]);
        Assert.StartsWith("rev-", resource.ResourceAttributes[ResourceAttributeNames.ContainerRevision]);
        Assert.Equal(resource.ResourceAttributes[ResourceAttributeNames.ContainerRevision], resource.Version);
        Assert.Equal(ApplicationLifetime.Detached, provider.GetApplication("application:sql")?.Lifetime);
        Assert.Equal(ResourceWorkloadKind.ContainerImage, workload?.Kind);
        Assert.Equal("example/sql-server:dev", workload?.Image);
        Assert.Equal("https://registry.example.com", workload?.Registry);
        Assert.Equal(3, workload?.Replicas);
        Assert.Equal("3", resource.ResourceAttributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("registry-user", provider.GetApplication("application:sql")?.ContainerRegistryCredentials?.Username);
        Assert.Equal(
            "REGISTRY_PASSWORD",
            provider.GetApplication("application:sql")?.ContainerRegistryCredentials?.PasswordEnvironmentVariable);
        Assert.Equal("docker:dev", workload?.ContainerHostId);
        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("tds", port.Name);
        Assert.Equal(1433, port.TargetPort);
        Assert.Equal(14333, port.Port);
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("tcp://localhost:14333", endpoint.Address);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_DefaultsRegistryToDockerHub()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources.AddContainer("redis", "redis:7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:redis");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ContainerRegistryDefaults.Default, resource.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
        Assert.Equal(ContainerRegistryDefaults.Default, workload?.Registry);
    }

    [Fact]
    public void ResourceOrchestratorService_DefaultsToWorkloadPortsAndReplicaCount()
    {
        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.ContainerImage,
            "API",
            Image: "example/api:dev",
            Replicas: 3,
            Ports:
            [
                new ServicePort("http", 8080, 5080, "http")
            ]);
        var service = new ResourceOrchestratorService(
            "application:api",
            "api",
            workload);

        Assert.Equal("application:api", service.ResourceId);
        Assert.Equal("api", service.Name);
        Assert.Same(workload, service.Workload);
        Assert.Equal(3, service.Replicas);
        Assert.Equal(workload.WorkloadPorts, service.ServicePorts);
        Assert.Empty(service.ServiceDependencies);
        Assert.Empty(service.ServiceNetworks);
    }

    [Fact]
    public void DockerComposeOrchestrator_RendersOrchestratorServiceShape()
    {
        var orchestrator = new DockerComposeResourceOrchestrator(
            new DockerComposeOrchestratorOptions
            {
                ProjectName = "sample"
            },
            [],
            new TestContainerHostResolver(
                new ContainerHostDescriptor(
                    "docker",
                    "Docker",
                    ContainerHostKind.Docker,
                    "unix:///var/run/docker.sock")));
        var render = typeof(DockerComposeResourceOrchestrator).GetMethod(
            "RenderComposeDocument",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var service = new ResourceOrchestratorService(
            "application:api",
            "api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "API",
                Image: "example/api:dev",
                Registry: "ghcr.io",
                Replicas: 3,
                EnvironmentVariables:
                [
                    new EnvironmentVariableAssignment("ASPNETCORE_ENVIRONMENT", "Development")
                ]),
            DependsOn:
            [
                "application:db"
            ],
            Networks:
            [
                "network:app"
            ],
            Ports:
            [
                new ServicePort("http", 8080, 5080, "http")
            ]);

        Assert.NotNull(render);
        var yaml = Assert.IsType<string>(render.Invoke(
            orchestrator,
            [
                new[] { service },
                Array.Empty<ServiceResourceDefinition>(),
                new[] { new NetworkResourceDefinition("network:app", "App Network") }
            ]));

        Assert.Contains("name: \"sample\"", yaml);
        Assert.Contains("  api:", yaml);
        Assert.Contains("    image: \"ghcr.io/example/api:dev\"", yaml);
        Assert.Contains("    depends_on:", yaml);
        Assert.Contains("      - db", yaml);
        Assert.Contains("    labels:", yaml);
        Assert.Contains("      - \"traefik.enable=true\"", yaml);
        Assert.Contains("traefik.http.routers.api-http-8080.rule", yaml);
        Assert.Contains("traefik.http.services.api-http-8080.loadbalancer.server.port=8080", yaml);
        Assert.Contains("    networks:", yaml);
        Assert.Contains("      - app", yaml);
        Assert.Contains("    deploy:", yaml);
        Assert.Contains("      replicas: 3", yaml);
        Assert.Contains("  api-ingress:", yaml);
        Assert.Contains("    image: \"traefik:v3.0\"", yaml);
        Assert.Contains("      - \"--entrypoints.api-http-5080.address=:5080\"", yaml);
        Assert.Contains("      - target: 5080", yaml);
        Assert.Contains("        published: 5080", yaml);
        Assert.Contains("      - \"/var/run/docker.sock:/var/run/docker.sock:ro\"", yaml);
    }

    [Fact]
    public async Task DockerComposeOrchestrator_ResolvesDockerHostThroughContainerHostResolver()
    {
        var resource = new Resource(
            "application:api",
            "API",
            "Application",
            "Test",
            "local",
            ResourceState.Stopped,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            []);
        var workload = new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.ContainerImage,
            "API",
            Image: "example/api:dev",
            ContainerHostId: "docker:remote");
        var resolver = new TestContainerHostResolver(
            new ContainerHostDescriptor(
                "docker:remote",
                "Remote Docker",
                ContainerHostKind.Docker,
                "tcp://127.0.0.1:2375"));
        var orchestrator = new DockerComposeResourceOrchestrator(
            new DockerComposeOrchestratorOptions(),
            [new TestDescriptorProvider(CreateDescriptor(resource.Id, ApplicationResourceTypes.ContainerApp, workload))],
            resolver);
        var method = typeof(DockerComposeResourceOrchestrator).GetMethod(
            "ResolveDockerHostAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);
        var task = Assert.IsAssignableFrom<Task<string?>>(method.Invoke(
            orchestrator,
            [
                new ResourceOrchestrationContext(
                    resource,
                    null,
                    null,
                    new StaticResourceManagerStore([resource]),
                    new TestResourceRegistrationStore([]),
                    "docker:preferred"),
                CancellationToken.None
            ]));
        var dockerHost = await task;

        Assert.Equal("tcp://127.0.0.1:2375", dockerHost);
        Assert.NotNull(resolver.Request);
        Assert.Equal("application:api", resolver.Request.TargetResourceId);
        Assert.Equal("docker:remote", resolver.Request.ExplicitHostResourceId);
        Assert.Equal("docker:preferred", resolver.Request.PreferredHostId);
    }

    [Theory]
    [InlineData(ContainerRegistryDefaults.Default, "redis:7.2", "docker.io/redis:7.2")]
    [InlineData(ContainerRegistryDefaults.Default, "mcr.microsoft.com/mssql/server:2022-latest", "mcr.microsoft.com/mssql/server:2022-latest")]
    [InlineData("http://localhost:5000", "team/api:dev", "localhost:5000/team/api:dev")]
    [InlineData("https://registry.example.com", "registry.example.com/team/api:dev", "registry.example.com/team/api:dev")]
    public void ContainerApplicationRegistryImageReference_HandlesDockerHubAndCustomRegistries(
        string registry,
        string image,
        string expected)
    {
        var method = typeof(ApplicationResourceProvider).GetMethod(
            "CreateRegistryImageReference",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [registry, image]));
    }

    [Theory]
    [InlineData("application:api", 1, 1, "cloudshell-application-api")]
    [InlineData("application:api", 1, 3, "cloudshell-application-api-replica-1")]
    [InlineData("application:api", 3, 3, "cloudshell-application-api-replica-3")]
    public void ContainerApplicationReplicaContainerName_UsesParentAppConvention(
        string resourceId,
        int replica,
        int replicas,
        string expected)
    {
        var method = typeof(ApplicationResourceProvider).GetMethod(
            "GetContainerName",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
            [typeof(string), typeof(int), typeof(int)]);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [resourceId, replica, replicas]));
    }

    [Theory]
    [InlineData("http", "tcp")]
    [InlineData("https", "tcp")]
    [InlineData("tcp", "tcp")]
    [InlineData("udp", "udp")]
    [InlineData("bogus", "tcp")]
    public void ContainerApplicationPublishProtocol_MapsApplicationProtocolsToDockerTransports(
        string protocol,
        string expected)
    {
        var method = typeof(ApplicationResourceProvider).GetMethod(
            "NormalizeContainerPublishProtocol",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);
        Assert.Equal(expected, method.Invoke(null, [protocol]));
    }

    [Fact]
    public void ContainerApplicationProvider_CreatesDefaultContainerOrchestratorService()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services.AddSingleton(new ApplicationProviderOptions());
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var application = new ApplicationResourceDefinition(
            "application:api",
            "api",
            executablePath: string.Empty,
            containerImage: "example/api:latest",
            replicas: 3,
            endpointPorts:
            [
                new ServicePort("http", 8080, 5080, "http")
            ],
            resourceType: ApplicationResourceTypes.ContainerApp);
        var method = typeof(ApplicationResourceProvider).GetMethod(
            "CreateDefaultContainerOrchestratorService",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotNull(method);
        var service = Assert.IsType<ResourceOrchestratorService>(method.Invoke(provider, [application]));

        Assert.Equal("application:api", service.ResourceId);
        Assert.Equal("cloudshell-application-api", service.Name);
        Assert.Equal(3, service.Replicas);
        var port = Assert.Single(service.ServicePorts);
        Assert.Equal("http", port.Name);
        Assert.Equal(8080, port.TargetPort);
        Assert.Equal(5080, port.Port);
    }

    [Fact]
    public void ContainerApplicationProvider_CreatesReplicatedContainerAppIngressConfiguration()
    {
        var method = typeof(ApplicationResourceProvider).GetMethod(
            "CreateContainerAppIngressConfiguration",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var service = new ResourceOrchestratorService(
            "application:api",
            "cloudshell-application-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "API",
                Image: "example/api:latest",
                Replicas: 3),
            Ports:
            [
                new ServicePort("http", 80, 5081, "http")
            ]);

        Assert.NotNull(method);
        var yaml = Assert.IsType<string>(method.Invoke(
            null,
            [
                service,
                service.ServicePorts
            ]));

        Assert.Contains("PathPrefix(`/`)", yaml);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-1:80\"", yaml);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-2:80\"", yaml);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-3:80\"", yaml);
    }

    [Fact]
    public async Task ContainerApplicationProvider_UpdatesTopLevelContainerAppImage()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources
                    .AddContainer("api", "example/api:latest")
                    .WithContainerHost("docker");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarationStore = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var registrations = new DeclarationRegistrationStore(declarationStore);
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var originalRevision = resource.ResourceAttributes[ResourceAttributeNames.ContainerRevision];

        var result = await provider.UpdateImageAsync(
            new ResourceProcedureContext(resource, registrations.GetRegistration(resource.Id), null, registrations),
            "example/api:20260608",
            restartIfRunning: false);

        var updated = provider.GetApplication("application:api");
        Assert.NotNull(updated);
        var log = Assert.Single(provider.GetLogs(), log =>
            log.ResourceId == "application:api" &&
            log.Name == "Console logs");

        Assert.Equal(ApplicationResourceTypes.ContainerApp, resource.EffectiveTypeId);
        Assert.Equal("Container app", resource.Kind);
        Assert.True(log.SupportsStreaming);
        Assert.Equal("example/api:20260608", updated.ContainerImage);
        Assert.StartsWith("rev-", updated.ContainerRevision);
        Assert.NotEqual(originalRevision, updated.ContainerRevision);
        Assert.Equal("Updated api to image 'example/api:20260608'.", result.Message);
    }

    [Fact]
    public async Task ContainerApplicationProvider_UpdatesTopLevelContainerAppReplicas()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources
                    .AddContainer("api", "example/api:latest")
                    .WithContainerHost("docker");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarationStore = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var registrations = new DeclarationRegistrationStore(declarationStore);
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");

        var result = await provider.UpdateReplicasAsync(
            new ResourceProcedureContext(resource, registrations.GetRegistration(resource.Id), null, registrations),
            3,
            restartIfRunning: false);

        var updated = provider.GetApplication("application:api");
        var projected = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");

        Assert.NotNull(updated);
        Assert.Equal(3, updated.Replicas);
        Assert.Equal("3", projected.ResourceAttributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("Updated api to 3 replicas.", result.Message);
    }


    private sealed class ParentMetadataExtension : ICloudShellExtension
    {
        public CloudShellExtensionManifest Manifest => new(
            "sample.parent-metadata",
            "Parent Metadata",
            "Adds resources for parent metadata tests.",
            "0.1.0",
            [],
            []);

        public void Configure(ICloudShellExtensionBuilder builder)
        {
            builder.AddResourceProvider<ParentMetadataProvider>();
        }
    }

    private sealed class ParentMetadataProvider : IResourceProvider
    {
        public string Id => "parent-metadata";

        public string DisplayName => "Parent Metadata";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                "sample:parent",
                "Parent",
                "Sample Parent",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                []),
            new(
                "sample:child",
                "Child",
                "Sample Child",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [])
        ];
    }

    private sealed class DeclarationRegistrationStore(
        ResourceDeclarationStore declarations) : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            declarations.GetDeclarations()
                .Select(declaration => new ResourceRegistration(
                    declaration.ResourceId,
                    declaration.ProviderId,
                    declaration.ResourceGroupId,
                    declaration.DeclaredAt,
                    declaration.DependsOn))
                .ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            GetRegistrations().FirstOrDefault(registration =>
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

    private static Resource CreateEndpointResource(string id, string endpointName, string address) =>
        new(
            id,
            id,
            "Application",
            "Test",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.FromAddress(endpointName, address, "http", ResourceExposureScope.Public)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.EndpointSource)]);

    private static LocalProcessDefinition CreateLongRunningProcessDefinition() =>
        OperatingSystem.IsWindows()
            ? new LocalProcessDefinition(
                $"process:test:{Guid.NewGuid():N}",
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                "/d /s /c \"timeout /t 30 /nobreak > nul\"",
                Lifetime: LocalProcessLifetime.ControlPlaneScoped)
            : new LocalProcessDefinition(
                $"process:test:{Guid.NewGuid():N}",
                "/bin/sh",
                "-c \"sleep 30\"",
                Lifetime: LocalProcessLifetime.ControlPlaneScoped);

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            await Task.Delay(50);
        }

        return process.HasExited;
    }

    private static Resource CreateNetworkingProviderResource(string id) =>
        new(
            id,
            id,
            "Networking Provider",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingEndpointMapper)]);

    private static ResourceOrchestrationDescriptor CreateDescriptor(
        string resourceId,
        string resourceType,
        object configuration) =>
        new(
            resourceId,
            resourceType,
            [],
            [],
            [],
            "1.0",
            JsonSerializer.SerializeToElement(configuration, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private sealed class TestDescriptorProvider(ResourceOrchestrationDescriptor descriptor) :
        IResourceOrchestrationDescriptorProvider
    {
        public bool CanDescribe(Resource resource) =>
            string.Equals(resource.Id, descriptor.ResourceId, StringComparison.OrdinalIgnoreCase);

        public Task<ResourceOrchestrationDescriptor> DescribeAsync(
            Resource resource,
            ResourceOrchestrationDescriptorContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(descriptor);
    }

    private sealed class TestContainerHostResolver(ContainerHostDescriptor host) : IContainerHostResolver
    {
        public ContainerHostResolutionRequest? Request { get; private set; }

        public Task<ContainerHostResolutionResult> ResolveAsync(
            ContainerHostResolutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new ContainerHostResolutionResult(host));
        }
    }

    private sealed class TestEndpointMappingProvisioner : IResourceEndpointMappingProvisioner
    {
        public ResourceEndpointMappingProvisioningContext? Context { get; private set; }

        public bool CanProvisionEndpointMapping(ResourceEndpointMappingProvisioningContext context) => true;

        public Task<ResourceProcedureResult> ProvisionEndpointMappingAsync(
            ResourceEndpointMappingProvisioningContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            return Task.FromResult(ResourceProcedureResult.Completed("Provisioned."));
        }
    }

    private sealed class TestResourceRegistrationStore(
        IReadOnlyList<ResourceRegistration> registrations) : IResourceRegistrationStore
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

    private sealed class MutableResourceRegistrationStore : IResourceRegistrationStore
    {
        private readonly List<ResourceRegistration> registrations = [];

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId);
            if (existing is not null)
            {
                registrations.Remove(existing);
            }

            registrations.Add(new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                dependsOn ?? []));
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId);
            if (existing is not null)
            {
                registrations.Remove(existing);
            }

            return Task.CompletedTask;
        }

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");
            registrations.Remove(existing);
            registrations.Add(existing with
            {
                ResourceGroupId = resourceGroupId,
                DependsOn = dependsOn ?? existing.DependsOn
            });
            return Task.CompletedTask;
        }

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                throw new InvalidOperationException($"Resource '{resourceId}' is not registered.");
            registrations.Remove(existing);
            registrations.Add(existing with { DependsOn = dependsOn });
            return Task.CompletedTask;
        }
    }

    private sealed class AutoStartResourceProvider :
        IResourceProvider,
        IResourceProcedureProvider,
        IResourceAutoStartPolicyProvider
    {
        private readonly Dictionary<string, ResourceState> states = new(StringComparer.OrdinalIgnoreCase)
        {
            ["dependency"] = ResourceState.Stopped,
            ["target"] = ResourceState.Stopped
        };

        public string Id => "auto-start";

        public string DisplayName => "Auto Start";

        public List<string> ExecutedResources { get; } = [];

        public string? FailedResourceId { get; init; }

        public string FailureMessage { get; init; } = "Resource failed to start.";

        public bool DefaultStartOnControlPlaneStart { get; init; } = true;

        public bool DefaultDependencyAutoStart { get; init; } = true;

        public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
            CanApplyDeclaration(declaration);

        public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
            new(DefaultStartOnControlPlaneStart, DefaultDependencyAutoStart, StartAfterCreate: false);

        public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
            string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<Resource> GetResources() =>
            states
                .Select(item => new Resource(
                    item.Key,
                    item.Key == "dependency" ? "Dependency" : "Target",
                    "Sample",
                    DisplayName,
                    "local",
                    item.Value,
                    [],
                    "1.0",
                    DateTimeOffset.UtcNow,
                    [],
                    Actions: [ResourceAction.Run]))
                .ToArray();

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedResources.Add(context.Resource.Id);
            if (string.Equals(context.Resource.Id, FailedResourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(FailureMessage);
            }

            states[context.Resource.Id] = ResourceState.Running;
            return Task.FromResult(ResourceProcedureResult.Completed("Completed."));
        }
    }

    private sealed class StaticResourceManagerStore(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<IResourceProvider>? providers = null) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => providers ?? [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }

    private sealed class TestResourceManagerStore(
        IResourceProvider provider,
        IResourceRegistrationStore registrations) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [provider];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() =>
            provider.GetResources();

        public IReadOnlyList<Resource> GetResources() =>
            GetAvailableResources()
                .Select(resource =>
                {
                    var registration = registrations.GetRegistration(resource.Id);
                    return registration is null
                        ? resource
                        : resource with
                        {
                            DependsOn = resource.DependsOn
                                .Concat(registration.DependsOn)
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToArray()
                        };
                })
                .ToArray();

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            GetResources().FirstOrDefault(resource =>
                string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            registrations.GetRegistration(resourceId) is not null;
    }

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) :
        Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class EmptyResourceGroupStore : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestHostLocalNetworkEnvironment : IHostLocalNetworkEnvironment
    {
        public string DefaultHost => "loopback.test";

        public ResourceEndpoint ResolveNetworkEndpoint(
            string networkId,
            ResourceEndpointRequest request,
            int autoLocalPortStart,
            int autoLocalPortEnd) =>
            ResourceEndpoint.FromAddress(
                request.Name,
                $"{request.ProtocolName}://{DefaultHost}:4123",
                request.ProtocolName,
                request.Exposure);

        public ResourceEndpoint ResolveServiceEndpoint(
            string serviceId,
            ServicePort port,
            int autoLocalPortStart,
            int autoLocalPortEnd) =>
            ResourceEndpoint.FromAddress(
                port.Name,
                $"{port.Protocol}://{DefaultHost}:4124",
                port.Protocol,
                port.Exposure);
    }
}
