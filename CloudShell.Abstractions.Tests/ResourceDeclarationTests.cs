using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;
using CloudShell.Components;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.Components.Pages.Resources;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;
using CloudShell.Providers.DockerCompose;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DockerContainerListResponse = Docker.DotNet.Models.ContainerListResponse;
using DockerPort = Docker.DotNet.Models.Port;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDeclarationTests
{
    [Fact]
    public void ResourceId_CreatesTypedIdFromScopedName()
    {
        var id = ResourceId.FromName("application", "Orders API");

        Assert.Equal("application:orders-api", id.Value);
        Assert.Equal("application", id.Scope);
        Assert.Equal("orders-api", id.Name);
        Assert.True(id.IsQualified);
        Assert.Equal(id.Value, id.ToString());
    }

    [Fact]
    public void ResourceId_PreservesQualifiedId()
    {
        var id = ResourceId.FromName("application", "configuration:orders--api");

        Assert.Equal("configuration:orders--api", id.Value);
        Assert.Equal("configuration", id.Scope);
        Assert.Equal("orders--api", id.Name);
    }

    [Fact]
    public void ResourceId_RejectsControlCharacters()
    {
        Assert.False(ResourceId.TryParse("application:bad\nname", out _));
        Assert.Throws<ArgumentException>(() => new ResourceId("application:bad\nname"));
    }

    [Fact]
    public void ResourceId_DefaultValueIsEmpty()
    {
        var id = default(ResourceId);

        Assert.True(id.IsEmpty);
        Assert.False(id.IsQualified);
        Assert.Null(id.Scope);
        Assert.Equal(string.Empty, id.Name);
        Assert.Equal(string.Empty, id.ToString());
    }

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
                    .Allow(api.Principal, "Database/databases/readWrite/action")
                    .Allow(api.Principal, "Database/databases/readWrite/action");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var grant = Assert.Single(store.GetPermissionGrants());

        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("application:api", grant.Principal.SourceResourceId);
        Assert.Equal("api-service", grant.Principal.SourceIdentityName);
        Assert.Equal("configuration:database", grant.TargetResourceId);
        Assert.Equal("Database/databases/readWrite/action", grant.Permission);
    }

    [Fact]
    public void ResourcePermissionGrant_ProjectsResourceIdentityPrincipal()
    {
        var grant = new ResourcePermissionGrant(
            ResourcePrincipalReference.ForResourceIdentity("application:api", "api-service"),
            "configuration:database",
            "Database/databases/read/action");

        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("application:api/identities/api-service", grant.Principal.Id);
        Assert.Equal("application:api", grant.Principal.SourceResourceId);
        Assert.Equal("api-service", grant.Principal.SourceIdentityName);
    }

    [Fact]
    public void ResourceIdentityDirectoryQuery_NormalizesPrincipalSearch()
    {
        var principal = new ResourcePrincipal(
            new ResourcePrincipalReference(
                ResourcePrincipalKind.ServicePrincipal,
                "  spn:api  ",
                DisplayName: "  API service principal  ",
                ProviderId: "  identity:entra  "),
            "  API service principal  ",
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["appId"] = "client-id"
            });
        var query = new ResourceIdentityDirectoryQuery(
            "  api  ",
            new HashSet<ResourcePrincipalKind>
            {
                ResourcePrincipalKind.ServicePrincipal,
                ResourcePrincipalKind.ManagedIdentity
            },
            25);

        Assert.Equal(ResourcePrincipalKind.ServicePrincipal, principal.Reference.Kind);
        Assert.Equal("spn:api", principal.Reference.Id);
        Assert.Equal("API service principal", principal.DisplayName);
        Assert.Equal("identity:entra", principal.Reference.ProviderId);
        Assert.Equal("client-id", principal.PrincipalAttributes["appId"]);
        Assert.Equal("api", query.SearchText);
        Assert.Contains(ResourcePrincipalKind.ManagedIdentity, query.PrincipalKinds);
        Assert.Equal(25, query.Limit);
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

        Assert.Equal("application:api", grant.Principal.SourceResourceId);
        Assert.Equal("configuration:database", grant.TargetResourceId);
        Assert.Equal("Database/databases/read/action", grant.Permission);
    }

    [Fact]
    public void ResourcePermissionGrantExtensions_CanGrantAccessPermissionSet()
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
                    .Declare("applications", "application:frontend")
                    .Allow(api, ResourceAccessPermissions.Reference);

                resources
                    .Declare("configuration", "configuration:app")
                    .Allow(api.Principal, ResourceAccessPermissions.Read);

                resources
                    .Declare("applications", "application:worker")
                    .Allow(api.Principal, ResourceAccessPermissions.Manage);
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var grants = store.GetPermissionGrants();

        Assert.Contains(
            grants,
            grant => grant.TargetResourceId == "application:frontend" &&
                     grant.Permission == CloudShellPermissions.Resources.Reference);
        Assert.Contains(
            grants,
            grant => grant.TargetResourceId == "configuration:app" &&
                     grant.Permission == CloudShellPermissions.Resources.Read);
        Assert.Contains(
            grants,
            grant => grant.TargetResourceId == "application:worker" &&
                     grant.Permission == CloudShellPermissions.Resources.Manage);
    }

    [Fact]
    public void ResourcePermissionGrantExtensions_CanGrantOperatePermissionSet()
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
                    .Declare("applications", "application:frontend")
                    .Allow(api, ResourceAccessPermissions.Operate(
                        CommonResourceOperationPermissions.LifecycleAction,
                        CommonResourceOperationPermissions.ExecuteCustomAction,
                        CommonResourceOperationPermissions.ExecuteCustomAction));
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var grants = store.GetPermissionGrants();

        Assert.Equal(2, grants.Count);
        Assert.Contains(
            grants,
            grant => grant.Permission == CommonResourceOperationPermissions.LifecycleAction);
        Assert.Contains(
            grants,
            grant => grant.Permission == CommonResourceOperationPermissions.ExecuteCustomAction);
    }

    [Fact]
    public void ResourcePermissionGrantEvaluator_EvaluatesDeclaredGrants()
    {
        var evaluator = new ResourcePermissionGrantEvaluator(
            [
                new(
                    ResourcePrincipalReference.ForResourceIdentity("application:api", "api-service"),
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
                    ResourcePrincipalReference.ForResourceIdentity("application:api"),
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
            ResourcePrincipalReference.ForResourceIdentity("application:api"),
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
                    .AddConfigurationStore("configuration:example").WithDisplayName("Example Configuration")
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
                    .AddConfigurationStore("configuration:example").WithDisplayName("Example Configuration")
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
                ResourceAction.Start,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains(
            "Could not auto-start dependency 'Dependency' for resource 'Target'.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependency path: Target -> Dependency.",
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
            ResourceAction.Start,
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
                ResourceAction.Start,
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
                ResourceAction.Start,
                startDependencies: true,
                new AllowAllAuthorizationService()));

        Assert.Equal(ControlPlaneErrorCodes.DependencyAutoStartFailed, exception.Error.Code);
        Assert.Contains(
            "Could not auto-start dependency 'Dependency' for resource 'Target'.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.Contains(
            "Dependency path: Target -> Dependency.",
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
                    .AddConfigurationStore("configuration:example").WithDisplayName("Example Configuration")
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
    public void WithDisplayName_RecordsOptionalPresentationLabel()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:example")
                    .WithDisplayName("Example Configuration");
            });

        var declaration = Assert.Single(services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>()
            .GetDeclarations());

        Assert.Equal("configuration:example", declaration.ResourceId);
        Assert.Equal("Example Configuration", declaration.DisplayName);
    }

    [Fact]
    public void ProgrammaticResources_DefaultNameUsesScopedResourceName()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider()
            .AddConfigurationProvider()
            .AddDockerProvider()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:application-topology-frontend",
                    "src/Frontend/Frontend.csproj");
                resources.AddConfigurationStore("configuration:sample-app");
                resources.AddSecretsVault("secrets-vault:sample-app");
                resources.AddDocker("docker:sample")
                    .AddDockerContainer("redis", "redis:7.2");
                resources.AddNetwork("network:app");
                resources.AddLoadBalancer("load-balancer:public");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var appProvider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var configurationProvider = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        var secretsProvider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var dockerProvider = serviceProvider.GetRequiredService<DockerContainerResourceProvider>();
        var platformOptions = serviceProvider.GetRequiredService<PlatformResourceOptions>();

        Assert.Equal(
            "application-topology-frontend",
            Assert.Single(appProvider.GetResources(), resource => resource.Id == "application:application-topology-frontend").Name);
        var configurationResource = Assert.Single(
            configurationProvider.GetResources(),
            resource => resource.Id == "configuration:sample-app");
        var secretsResource = Assert.Single(
            secretsProvider.GetResources(),
            resource => resource.Id == "secrets-vault:sample-app");
        Assert.Equal(
            "sample-app",
            configurationResource.Name);
        Assert.Equal(
            "sample-app",
            secretsResource.Name);
        AssertConfigurationProviderEndpointProjection(configurationResource, "entries");
        AssertConfigurationProviderEndpointProjection(secretsResource, "secrets");
        Assert.Equal(
            "redis",
            Assert.Single(dockerProvider.GetResources(), resource => resource.Id == "docker:container:redis").Name);
        Assert.Equal(
            "app",
            Assert.Single(platformOptions.DeclaredNetworks).Definition.Name);
        Assert.Equal(
            "public",
            Assert.Single(platformOptions.DeclaredLoadBalancers).Definition.Name);
    }

    [Fact]
    public void ConfigurationResources_DisplayNameDoesNotReplaceResourceName()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider()
            .AddSecretsProvider()
            .Resources(resources =>
            {
                resources
                    .AddConfigurationStore("configuration:application-topology")
                    .WithDisplayName("Settings");
                resources
                    .AddSecretsVault("secrets-vault:application-topology")
                    .WithDisplayName("Secrets");
                resources
                    .AddHostConfigurationSource("configuration:host-development")
                    .WithDisplayName("Host Development Settings");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var configurationProvider = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        var secretsProvider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var hostConfigurationProvider = serviceProvider.GetRequiredService<HostConfigurationSourceProvider>();

        var configuration = Assert.Single(
            configurationProvider.GetResources(),
            resource => resource.Id == "configuration:application-topology");
        var secrets = Assert.Single(
            secretsProvider.GetResources(),
            resource => resource.Id == "secrets-vault:application-topology");
        var hostConfiguration = Assert.Single(
            hostConfigurationProvider.GetResources(),
            resource => resource.Id == "configuration:host-development");

        Assert.Equal("application-topology", configuration.Name);
        Assert.Equal("Settings", configuration.DisplayName);
        Assert.Equal("application-topology", secrets.Name);
        Assert.Equal("Secrets", secrets.DisplayName);
        Assert.Equal("host-development", hostConfiguration.Name);
        Assert.Equal("Host Development Settings", hostConfiguration.DisplayName);
    }

    [Fact]
    public void ResourceGraphBuilder_DeclaresResourceGroups()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddResourceGroup(
                    "group:sample",
                    "Sample",
                    "Sample resources.");
                resources
                    .AddConfigurationStore("configuration:sample")
                    .WithResourceGroup("group:sample");
            });

        var store = services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDeclarationStore>();
        var group = Assert.Single(store.GetResourceGroups());
        var declaration = Assert.Single(store.GetDeclarations());

        Assert.Equal("group:sample", group.Id);
        Assert.Equal("Sample", group.Name);
        Assert.Equal("Sample resources.", group.Description);
        Assert.Equal("group:sample", declaration.ResourceGroupId);
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
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings");

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
                    .AddHostConfigurationSource("configuration:host-dev").WithDisplayName("Host Development Settings")
                    .WithEntry("ExternalApi:BaseUrl");

                reference = hostSettings.Entry("ExternalApi:BaseUrl");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "configuration:host-dev");
        var provider = serviceProvider.GetRequiredService<HostConfigurationSourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);

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
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("db-password", "local-dev-password");

                reference = vault.Secret("db-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "secrets-vault:app");
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);

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
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("db-password", "local-dev-password");

                reference = vault.Secret("db-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resolver = Assert.Single(serviceProvider.GetServices<ISecretReferenceResolver>());
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);

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
    public void ApplicationProviderExtension_RegistersApplicationResourceTabs()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var resourceTypes = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .ToDictionary(resourceType => resourceType.Id, StringComparer.OrdinalIgnoreCase);

        var containerAppType = resourceTypes[ApplicationResourceTypes.ContainerApp];
        AssertDeploymentTab(containerAppType);
        AssertScalingTab(containerAppType);
        AssertNoStandaloneReplicaTab(containerAppType);
        AssertContainerAppMonitoringTab(containerAppType);
        AssertContainerAppStorageTab(containerAppType);
        AssertApplicationExposureSection(containerAppType);
        var sqlServerType = resourceTypes[ApplicationResourceTypes.SqlServer];
        Assert.Equal(ResourceClass.Service, sqlServerType.ResourceClass);
        AssertStorageTab(sqlServerType);
        AssertApplicationExposureSection(sqlServerType);
        AssertApplicationExposureSection(resourceTypes[ApplicationResourceTypes.ExecutableApplication]);
        var aspNetCoreProjectType = resourceTypes[ApplicationResourceTypes.AspNetCoreProject];
        AssertApplicationExposureSection(aspNetCoreProjectType);
        AssertResourceEndpointDescriptor(aspNetCoreProjectType, "http", 80, "http");
        AssertResourceEndpointDescriptor(containerAppType, "http", 80, "http");
        AssertResourceEndpointDescriptor(sqlServerType, "tds", 1433, "tcp");
        Assert.DoesNotContain(
            sqlServerType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "replicas"));
        Assert.DoesNotContain(
            sqlServerType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"));
        Assert.DoesNotContain(
            sqlServerType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "deployment"));
        Assert.DoesNotContain(
            resourceTypes[ApplicationResourceTypes.AspNetCoreProject].ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "replicas"));
        Assert.DoesNotContain(
            resourceTypes[ApplicationResourceTypes.AspNetCoreProject].ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"));
        Assert.DoesNotContain(
            resourceTypes[ApplicationResourceTypes.AspNetCoreProject].ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Storage, "storage"));
        Assert.DoesNotContain(
            resourceTypes[ApplicationResourceTypes.AspNetCoreProject].ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "deployment"));
    }

    private static void AssertResourceEndpointDescriptor(
        ResourceTypeContribution resourceType,
        string name,
        int targetPort,
        string protocol)
    {
        var descriptor = Assert.Single(
            resourceType.ResourceEndpointDescriptors,
            descriptor => string.Equals(descriptor.Name, name, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(targetPort, descriptor.TargetPort);
        Assert.Equal(protocol, descriptor.Protocol);
        Assert.True(descriptor.SupportsPortRemapping);
    }

    [Fact]
    public void DockerProviderExtension_RegistersHostContainersTab()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<DockerProviderExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var hostType = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .Single(resourceType => string.Equals(
                resourceType.Id,
                DockerContainerResourceProvider.HostResourceType,
                StringComparison.OrdinalIgnoreCase));

        var overviewTab = Assert.Single(
            hostType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "overview"));
        var containersTab = Assert.Single(
            hostType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Runtime, "containers"));
        var configurationTab = Assert.Single(
            hostType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "configuration"));

        Assert.Equal(typeof(CloudShell.Providers.Docker.Pages.DockerEngineOverview), overviewTab.ComponentType);
        Assert.Equal(typeof(CloudShell.Providers.Docker.Pages.DockerContainers), containersTab.ComponentType);
        Assert.Equal("Containers", containersTab.Title);
        Assert.Equal(20, containersTab.Order);
        Assert.False(containersTab.ShowsApplyButton);
        Assert.Equal(30, configurationTab.Order);
        Assert.True(configurationTab.ShowsApplyButton);
    }

    [Fact]
    public void ResourceManagerExtension_RegistersVolumeResourceTypeAndTabs()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<ResourceManagerExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var resourceTypes = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .ToDictionary(resourceType => resourceType.Id, StringComparer.OrdinalIgnoreCase);

        var volumeType = resourceTypes["cloudshell.volume"];

        Assert.Equal("Volume", volumeType.DisplayName);
        Assert.Equal(ResourceClass.Storage, volumeType.ResourceClass);
        Assert.Equal(typeof(RegisterVolumeResource), volumeType.RegistrationComponentType);
        Assert.Equal(typeof(UpdateVolumeResource), volumeType.UpdateComponentType);

        var overview = Assert.Single(
            volumeType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "overview"));
        var configuration = Assert.Single(
            volumeType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "configuration"));

        Assert.Equal(typeof(VolumeOverview), overview.ComponentType);
        Assert.False(overview.ShowsApplyButton);
        Assert.Equal(typeof(UpdateVolumeResource), configuration.ComponentType);
        Assert.True(configuration.ShowsApplyButton);
    }

    [Fact]
    public void ResourceManagerExtension_RegistersStorageResourceTypeAndTabs()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<ResourceManagerExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var resourceTypes = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .ToDictionary(resourceType => resourceType.Id, StringComparer.OrdinalIgnoreCase);

        var storageType = resourceTypes["cloudshell.storage"];

        Assert.Equal("Local Storage", storageType.DisplayName);
        Assert.Equal(ResourceClass.Storage, storageType.ResourceClass);
        Assert.Equal(typeof(RegisterLocalStorageResource), storageType.RegistrationComponentType);
        Assert.Equal(typeof(UpdateLocalStorageResource), storageType.UpdateComponentType);

        var overview = Assert.Single(
            storageType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "overview"));
        var configuration = Assert.Single(
            storageType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "configuration"));
        var volumes = Assert.Single(
            storageType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Storage, "volumes"));

        Assert.Equal(typeof(LocalStorageOverview), overview.ComponentType);
        Assert.False(overview.ShowsApplyButton);
        Assert.Equal(typeof(StorageVolumes), volumes.ComponentType);
        Assert.False(volumes.ShowsApplyButton);
        Assert.Equal(20, volumes.Order);
        Assert.Equal(typeof(UpdateLocalStorageResource), configuration.ComponentType);
        Assert.True(configuration.ShowsApplyButton);
        Assert.Equal(30, configuration.Order);
    }

    [Fact]
    public void ResourceManagerExtension_RegistersDnsResourceTypes()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<ResourceManagerExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var resourceTypes = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .ToDictionary(resourceType => resourceType.Id, StringComparer.OrdinalIgnoreCase);

        var dnsZoneType = resourceTypes["cloudshell.dnsZone"];
        var nameMappingType = resourceTypes["cloudshell.nameMapping"];

        Assert.Equal("DNS Zone", dnsZoneType.DisplayName);
        Assert.Equal(ResourceClass.Network, dnsZoneType.ResourceClass);
        Assert.Equal(typeof(RegisterDnsZoneResource), dnsZoneType.RegistrationComponentType);
        var dnsZoneOverview = Assert.Single(
            dnsZoneType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.General, "overview"));
        Assert.Equal(typeof(DnsZoneOverview), dnsZoneOverview.ComponentType);
        Assert.False(dnsZoneOverview.ShowsApplyButton);

        Assert.Equal("Name Mapping", nameMappingType.DisplayName);
        Assert.Equal(ResourceClass.Network, nameMappingType.ResourceClass);
        Assert.Equal(typeof(RegisterNameMappingResource), nameMappingType.RegistrationComponentType);
        Assert.Equal(typeof(UpdateNameMappingResource), nameMappingType.UpdateComponentType);
        Assert.Empty(nameMappingType.ResourceTabs);
    }

    [Fact]
    public void ResourceManagerExtension_RegistersLoadBalancerUpdateComponent()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .AddExtension<ResourceManagerExtension>();

        using var serviceProvider = services.BuildServiceProvider();
        var registry = serviceProvider.GetRequiredService<CloudShellExtensionRegistry>();
        var resourceTypes = registry.Extensions
            .SelectMany(extension => extension.ResourceTypes)
            .ToDictionary(resourceType => resourceType.Id, StringComparer.OrdinalIgnoreCase);

        var loadBalancerType = resourceTypes["cloudshell.loadBalancer"];

        Assert.Equal("Load Balancer", loadBalancerType.DisplayName);
        Assert.Equal(ResourceClass.Network, loadBalancerType.ResourceClass);
        Assert.Equal(typeof(RegisterLoadBalancerResource), loadBalancerType.RegistrationComponentType);
        Assert.Equal(typeof(UpdateLoadBalancerResource), loadBalancerType.UpdateComponentType);
    }

    private static void AssertStorageTab(ResourceTypeContribution resourceType)
    {
        var storageTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Storage, "storage"));

        Assert.Equal("Storage", storageTab.Title);
        Assert.True(storageTab.ShowsApplyButton);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationStorage), storageTab.ComponentType);
    }

    private static void AssertContainerAppStorageTab(ResourceTypeContribution resourceType)
    {
        var storageTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "storage"));

        Assert.Equal("Storage", storageTab.Title);
        Assert.Equal(ResourceTabGroupTitles.Application, storageTab.GroupTitle);
        Assert.True(storageTab.ShowsApplyButton);
        Assert.Equal("storage", storageTab.Icon);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationStorage), storageTab.ComponentType);
    }

    private static void AssertApplicationExposureSection(ResourceTypeContribution resourceType)
    {
        var section = Assert.Single(
            resourceType.ResourcePredefinedViewSections,
            section => section.Id == "application.exposure-actions");

        Assert.Equal(ResourcePredefinedViewIds.Endpoints, section.ViewId);
        Assert.Equal("Application exposure", section.Title);
        Assert.Equal(10, section.Order);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationEndpointActions), section.ComponentType);
    }

    private static void AssertDeploymentTab(ResourceTypeContribution resourceType)
    {
        var deploymentTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "deployment"));

        Assert.Equal("Deployment", deploymentTab.Title);
        Assert.Equal(ResourceTabGroupTitles.Application, deploymentTab.GroupTitle);
        Assert.False(deploymentTab.ShowsApplyButton);
        Assert.Equal(20, deploymentTab.Order);
        Assert.Equal("deployment", deploymentTab.Icon);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationDeployment), deploymentTab.ComponentType);
    }

    private static void AssertNoStandaloneReplicaTab(ResourceTypeContribution resourceType)
    {
        Assert.DoesNotContain(
            resourceType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "replicas"));
    }

    private static void AssertContainerAppMonitoringTab(ResourceTypeContribution resourceType)
    {
        var monitoringTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id == ResourcePredefinedViewIds.Monitoring);

        Assert.Equal("Monitoring", monitoringTab.Title);
        Assert.False(monitoringTab.ShowsApplyButton);
        Assert.Equal(45, monitoringTab.Order);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationMonitoring), monitoringTab.ComponentType);
    }

    private static void AssertScalingTab(ResourceTypeContribution resourceType)
    {
        var scalingTab = Assert.Single(
            resourceType.ResourceTabs,
            tab => tab.Id == new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"));

        Assert.Equal("Scale and replicas", scalingTab.Title);
        Assert.Equal(ResourceTabGroupTitles.Application, scalingTab.GroupTitle);
        Assert.True(scalingTab.ShowsApplyButton);
        Assert.Equal(30, scalingTab.Order);
        Assert.Equal("scale", scalingTab.Icon);
        Assert.Equal(typeof(CloudShell.Providers.Applications.Pages.ApplicationScaling), scalingTab.ComponentType);
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
                    .AddSecretsVault("secrets-vault:one").WithDisplayName("One")
                    .WithSecret("token", "one-token");
                resources
                    .AddSecretsVault("secrets-vault:two").WithDisplayName("Two")
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
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("token", "secret-token")
                    .Allow(api.Principal, SecretsVaultResourceOperationPermissions.ReadSecrets);
                resources
                    .AddSecretsVault("secrets-vault:other").WithDisplayName("Other Secrets")
                    .WithSecret("token", "other-token");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var context = new ResourceSettingResolutionContext(
            "application:api",
            "group-1",
            "run",
            ResourceIdentityReference.ForResource("application:api", "api-service"),
            "api/api-service");

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
        Assert.Contains("Identity 'api/api-service'", denied.ErrorMessage);
        Assert.Contains("Other Secrets", denied.ErrorMessage);
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
                        executablePath: "dotnet")
                    .WithIdentity("identity:development", name: "api-service")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:app", "sample-api-key"));

                resources
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("sample-api-key", "secret");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var exception = await Assert.ThrowsAsync<ResourceSettingResolutionException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start));

        Assert.Equal("SAMPLE_API_KEY", exception.SettingName);
        Assert.Equal("secret", exception.ReferenceKind);
        Assert.Contains("is not allowed to read secrets", exception.Message);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingAspNetCoreProjectPathAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var missingProjectPath = Path.Combine("src", "API", "API.csproj");
        var services = new ServiceCollection();
        Directory.CreateDirectory(contentRoot);

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
                resources.AddAspNetCoreProject("api", missingProjectPath);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Equal(
            $"Project-backed application resource 'api' cannot start because project path '{missingProjectPath}' was not found at '{Path.GetFullPath(missingProjectPath, contentRoot)}'.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_AllowsAspNetCoreProjectStartWhenProjectPathExists()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine("src", "API", "API.csproj");
        var projectFullPath = Path.GetFullPath(projectPath, contentRoot);
        var services = new ServiceCollection();
        Directory.CreateDirectory(Path.GetDirectoryName(projectFullPath)!);
        await File.WriteAllTextAsync(projectFullPath, "<Project Sdk=\"Microsoft.NET.Sdk.Web\" />");

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
                resources.AddAspNetCoreProject("api", projectPath);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Null(reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingWorkingDirectoryAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var workingDirectory = Path.Combine("missing", "api");
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
                    executablePath: "dotnet",
                    workingDirectory: workingDirectory);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Equal(
            $"Application resource 'api' cannot start because working directory '{Path.GetFullPath(workingDirectory, contentRoot)}' was not found.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingExplicitExecutablePathAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var executablePath = Path.Combine("bin", "api");
        var services = new ServiceCollection();
        Directory.CreateDirectory(contentRoot);

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
                    executablePath: executablePath);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Equal(
            $"Executable application resource 'api' cannot start because executable path '{executablePath}' was not found at '{Path.GetFullPath(executablePath, contentRoot)}'.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_AllowsExplicitExecutablePathWhenFileExists()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var executablePath = Path.Combine("bin", "api");
        var executableFullPath = Path.GetFullPath(executablePath, contentRoot);
        var services = new ServiceCollection();
        Directory.CreateDirectory(Path.GetDirectoryName(executableFullPath)!);
        await File.WriteAllTextAsync(executableFullPath, "#!/bin/sh");

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
                    executablePath: executablePath);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Null(reason);
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
                        executablePath: "dotnet")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:missing", "sample-api-key"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.Equal(
            "Setting 'SAMPLE_API_KEY' references Secrets Vault 'secrets-vault:missing', but that resource is not available.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingConfigurationEntryAsActionUnavailable()
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
                var settings = resources
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings")
                    .WithEntry("Message", "hello");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        executablePath: "dotnet")
                    .WithEnvironment(
                        "WELCOME_MESSAGE",
                        settings.Entry("Missing"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore(resources, providers);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains(
            "Could not resolve configuration-entry reference for setting 'WELCOME_MESSAGE'.",
            reason);
        Assert.Contains(
            "Configuration entry 'Missing' was not found in 'App Settings'.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingSecretAsActionUnavailable()
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
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("sample-api-key", "secret");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        executablePath: "dotnet")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:app", "missing"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore(resources, providers);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains(
            "Could not resolve secret reference for setting 'SAMPLE_API_KEY'.",
            reason);
        Assert.Contains(
            "Secret 'missing' was not found in Secrets Vault 'App Secrets'.",
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
                        executablePath: "dotnet")
                    .WithIdentity("identity:development", name: "api-service")
                    .WithEnvironment(
                        "SAMPLE_API_KEY",
                        new SecretReference("secrets-vault:app", "sample-api-key"));

                resources
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("sample-api-key", "secret");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var resource = Assert.Single(resources, resource => resource.Id == "application:api");
        var registrations = new TestResourceRegistrationStore(
            [
                new ResourceRegistration(
                    resource.Id,
                    resourceProvider.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    [])
            ]);
        var resourceManager = new StaticResourceManagerStore(resources, providers);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            ResourceAction.Start);

        Assert.NotNull(reason);
        Assert.Contains("Setting 'SAMPLE_API_KEY' references 'App Secrets'", reason);
        Assert.Contains("identity 'api/api-service' is not allowed to read secrets", reason);
        Assert.Contains("on resource 'App Secrets'", reason);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, reason);
    }

    [Fact]
    public async Task ApplicationProvider_ReportsOccupiedLocalEndpointAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var services = new ServiceCollection();

        try
        {
            Directory.CreateDirectory(contentRoot);
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
                            executablePath: "dotnet")
                        .WithEndpoint($"http://127.0.0.1:{port}");
                });

            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceProvider = serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>();
            var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
            var endpoint = Assert.Single(resource.Endpoints);
            Assert.Equal("application", endpoint.Name);
            Assert.Equal("http", endpoint.Protocol);
            Assert.Equal(port, endpoint.TargetPort);
            var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
            Assert.Equal("application", mapping.Name);
            Assert.Equal(new ResourceEndpointReference(resource.Id, endpoint.Name), mapping.Target);
            Assert.Equal($"http://127.0.0.1:{port}", mapping.Address);
            var registrations = new TestResourceRegistrationStore(
                [
                    new ResourceRegistration(
                        resource.Id,
                        resourceProvider.Id,
                        null,
                        DateTimeOffset.UtcNow,
                        [])
                ]);
            var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

            var reason = await resourceProvider.GetActionUnavailableReasonAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start);

            Assert.Equal(
                $"Endpoint mapping 'application' for application resource 'application:api' cannot use http://127.0.0.1:{port} because the address is already in use.",
                reason);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ApplicationProvider_ReportsOccupiedContainerEndpointAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var services = new ServiceCollection();

        try
        {
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
            services
                .AddControlPlane()
                .AddApplicationProvider(options =>
                {
                    options.DefinitionsPath = "application-resources.json";
                    options.RuntimeStatePath = "application-runtime-state.json";
                    options.LogDirectory = "application-logs";
                });

            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
            var registrations = new MutableResourceRegistrationStore();
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    string.Empty,
                    containerImage: "nginx:1.27",
                    endpointPorts:
                    [
                        new ServicePort("http", 80, port, "http")
                    ],
                    resourceType: ApplicationResourceTypes.ContainerApp),
                resourceGroupId: null,
                registrations);

            var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
            var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

            var reason = await resourceProvider.GetActionUnavailableReasonAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start);

            Assert.Equal(
                $"Endpoint 'http' for container app resource 'application:api' cannot use local port {port} because the address is already in use.",
                reason);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ApplicationProvider_ReportsMissingRegistryCredentialEnvironmentAsActionUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var passwordVariable = $"CLOUDSHELL_TEST_REGISTRY_PASSWORD_{Guid.NewGuid():N}";
        var services = new ServiceCollection();

        try
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
            services
                .AddControlPlane()
                .UseContainerHost(new ContainerHostDescriptor(
                    "docker:dev",
                    "Docker",
                    ContainerHostKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true,
                    Capabilities: [ContainerHostCapabilityIds.ContainerImage]))
                .AddApplicationProvider();

            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
            var registrations = new MutableResourceRegistrationStore();
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    string.Empty,
                    containerImage: "registry.example.com/team/api:dev",
                    containerRegistry: "https://registry.example.com",
                    containerRegistryCredentials: new ContainerRegistryCredentials(
                        "registry-user",
                        passwordVariable),
                    resourceType: ApplicationResourceTypes.ContainerApp),
                resourceGroupId: null,
                registrations);

            var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
            var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

            var reason = await resourceProvider.GetActionUnavailableReasonAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start);

            Assert.Equal(
                $"Container app resource 'API' cannot access registry 'registry.example.com' because credential environment variable '{passwordVariable}' is not configured.",
                reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
        }
    }

    [Fact]
    public async Task ApplicationProvider_AllowsRegistryCredentialWhenEnvironmentIsConfigured()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var passwordVariable = $"CLOUDSHELL_TEST_REGISTRY_PASSWORD_{Guid.NewGuid():N}";
        var services = new ServiceCollection();

        try
        {
            Environment.SetEnvironmentVariable(passwordVariable, "secret-value");
            services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
            services
                .AddControlPlane()
                .UseContainerHost(new ContainerHostDescriptor(
                    "docker:dev",
                    "Docker",
                    ContainerHostKind.Docker,
                    "unix:///var/run/docker.sock",
                    IsDefault: true,
                    Capabilities: [ContainerHostCapabilityIds.ContainerImage]))
                .AddApplicationProvider();

            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
            var registrations = new MutableResourceRegistrationStore();
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    string.Empty,
                    containerImage: "registry.example.com/team/api:dev",
                    containerRegistry: "https://registry.example.com",
                    containerRegistryCredentials: new ContainerRegistryCredentials(
                        "registry-user",
                        passwordVariable),
                    resourceType: ApplicationResourceTypes.ContainerApp),
                resourceGroupId: null,
                registrations);

            var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
            var resourceManager = new StaticResourceManagerStore([resource], [resourceProvider]);

            var reason = await resourceProvider.GetActionUnavailableReasonAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start);

            Assert.Null(reason);
        }
        finally
        {
            Environment.SetEnvironmentVariable(passwordVariable, null);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
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
    [Trait("Category", "Integration")]
    public async Task LocalProcessRunner_LogsTrackedProcessExitObservationOnce()
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
        var definition = CreateShortLivedProcessDefinition();
        using var logProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(logProvider);
        });

        try
        {
            using var runner = new LocalProcessRunner(runtimeStates, options, environment, loggerFactory);
            await runner.StartAsync(definition);
            var runtimeState = runtimeStates.Get(definition.Id);
            Assert.NotNull(runtimeState?.LastKnownProcessId);
            using var process = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            Assert.True(await WaitForExitAsync(process, TimeSpan.FromSeconds(5)));

            Assert.False(runner.IsRunning(definition));
            Assert.False(runner.IsRunning(definition));

            Assert.Equal(
                1,
                logProvider.Messages.Count(message =>
                    message.Contains("Observed previously tracked local process", StringComparison.Ordinal)));
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
    [Trait("Category", "Integration")]
    public async Task LocalProcessRunner_ReturnsMonitoringSnapshotForRunningProcess()
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

        try
        {
            using var runner = new LocalProcessRunner(runtimeStates, options, environment);
            await runner.StartAsync(definition);

            var snapshot = await runner.GetMonitoringSnapshotAsync(definition);

            Assert.NotNull(snapshot);
            Assert.True(snapshot.ProcessId > 0);
            Assert.True(snapshot.WorkingSetBytes > 0);
            Assert.True(snapshot.PrivateMemoryBytes >= 0);
            Assert.True(snapshot.ThreadCount > 0);
            Assert.True(snapshot.TotalProcessorTime >= TimeSpan.Zero);
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
    [Trait("Category", "Integration")]
    public async Task LocalProcessRunner_RunCommandUsesContentRootByDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var outputPath = Path.Combine(contentRoot, "working-directory.txt");
        var options = new LocalProcessOptions
        {
            RuntimeStatePath = "application-runtime-state.json",
            LogDirectory = "application-logs"
        };
        var environment = new TestHostEnvironment(contentRoot);
        var runtimeStates = new ApplicationRuntimeStateStore(options, environment);

        try
        {
            using var runner = new LocalProcessRunner(runtimeStates, options, environment);

            var exitCode = await runner.RunCommandAsync(
                "application:test",
                "/bin/sh",
                ["-c", $"pwd > {QuoteShellArgument(outputPath)}"]);

            Assert.Equal(0, exitCode);
            Assert.Equal(
                Path.GetFileName(contentRoot),
                Path.GetFileName(File.ReadAllText(outputPath).Trim()));
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
    [Trait("Category", "Integration")]
    public async Task LocalProcessRunner_DisposeStopsControlPlaneScopedProcessTree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var options = new LocalProcessOptions
        {
            RuntimeStatePath = "application-runtime-state.json",
            LogDirectory = "application-logs"
        };
        var environment = new TestHostEnvironment(contentRoot);
        var runtimeStates = new ApplicationRuntimeStateStore(options, environment);
        var childPidPath = Path.Combine(contentRoot, "child.pid");
        var definition = CreateProcessTreeDefinition(contentRoot, childPidPath);
        Process? childProcess = null;

        try
        {
            var runner = new LocalProcessRunner(runtimeStates, options, environment);
            await runner.StartAsync(definition);
            var childPid = await WaitForProcessIdFileAsync(childPidPath, TimeSpan.FromSeconds(5));
            childProcess = Process.GetProcessById(childPid);
            Assert.False(childProcess.HasExited);

            runner.Dispose();

            Assert.True(await WaitForExitAsync(childProcess, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (childProcess is not null)
            {
                try
                {
                    if (!childProcess.HasExited)
                    {
                        childProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    childProcess.Dispose();
                }
            }

            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task LocalProcessRunner_StopAsyncStopsControlPlaneScopedProcessTree()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var options = new LocalProcessOptions
        {
            RuntimeStatePath = "application-runtime-state.json",
            LogDirectory = "application-logs"
        };
        var environment = new TestHostEnvironment(contentRoot);
        var runtimeStates = new ApplicationRuntimeStateStore(options, environment);
        var childPidPath = Path.Combine(contentRoot, "child.pid");
        var definition = CreateProcessTreeDefinition(contentRoot, childPidPath);
        Process? childProcess = null;

        try
        {
            using var runner = new LocalProcessRunner(runtimeStates, options, environment);
            await runner.StartAsync(definition);
            var childPid = await WaitForProcessIdFileAsync(childPidPath, TimeSpan.FromSeconds(5));
            childProcess = Process.GetProcessById(childPid);
            Assert.False(childProcess.HasExited);

            await runner.StopAsync(definition);

            Assert.True(await WaitForExitAsync(childProcess, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            if (childProcess is not null)
            {
                try
                {
                    if (!childProcess.HasExited)
                    {
                        childProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    childProcess.Dispose();
                }
            }

            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
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
                    executablePath: "dotnet");
                resources.AddAspNetCoreProject(
                    "application:api",
                    "src/API/API.csproj");
                resources.AddContainer(
                    "redis",
                    "redis:7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();

        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:worker")?.Lifetime);
        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:api")?.Lifetime);
        Assert.Equal(ApplicationLifetime.ControlPlaneScoped, provider.GetApplication("application:redis")?.Lifetime);
    }

    [Fact]
    public async Task ApplicationProvider_ProjectsResourcesThroughTypeSpecificProviders()
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

        using var serviceProvider = services.BuildServiceProvider();
        var applications = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var registrations = new MutableResourceRegistrationStore();

        await applications.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:worker",
                "Worker",
                "dotnet",
                resourceType: ApplicationResourceTypes.ExecutableApplication),
            resourceGroupId: null,
            registrations);
        await applications.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                resourceType: ApplicationResourceTypes.AspNetCoreProject,
                projectPath: "src/API/API.csproj"),
            resourceGroupId: null,
            registrations);
        await applications.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:redis",
                "Redis",
                string.Empty,
                containerImage: "redis:7.2",
                resourceType: ApplicationResourceTypes.ContainerApp),
            resourceGroupId: null,
            registrations);
        await applications.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:sql",
                "SQL Server",
                string.Empty,
                containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
                resourceType: ApplicationResourceTypes.SqlServer),
            resourceGroupId: null,
            registrations);

        Assert.DoesNotContain(
            serviceProvider.GetServices<IResourceProvider>(),
            provider => string.Equals(
                provider.Id,
                ApplicationResourceProviderIds.Applications,
                StringComparison.OrdinalIgnoreCase));
        Assert.IsNotAssignableFrom<IResourceProvider>(applications);
        Assert.IsNotAssignableFrom<IResourceProcedureProvider>(applications);
        Assert.IsNotAssignableFrom<IResourceTemplateProvider>(applications);
        Assert.IsNotAssignableFrom<IProgrammaticResourceDeclarationProvider>(applications);
        Assert.IsNotAssignableFrom<IResourceOrchestrationDescriptorProvider>(applications);
        Assert.IsNotAssignableFrom<IResourceActionAvailabilityProvider>(applications);
        Assert.Equal(
            ["application:worker"],
            serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>()
                .GetResources()
                .Select(resource => resource.Id)
                .ToArray());
        Assert.Equal(
            ["application:api"],
            serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>()
                .GetResources()
                .Select(resource => resource.Id)
                .ToArray());
        Assert.Equal(
            ["application:redis"],
            serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>()
                .GetResources()
                .Select(resource => resource.Id)
                .ToArray());
        Assert.Equal(
            ["application:sql"],
            serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>()
                .GetResources()
                .Select(resource => resource.Id)
                .ToArray());
        Assert.DoesNotContain(
            new IResourceProvider[]
            {
                serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>(),
                serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>(),
                serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>()
            },
            provider =>
                provider is IResourceImageUpdateProvider or
                IResourceReplicaUpdateProvider or
                IResourceOrchestratorServiceProcedureProvider);
        Assert.IsAssignableFrom<IResourceImageUpdateProvider>(
            serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>());
        Assert.IsAssignableFrom<IResourceReplicaUpdateProvider>(
            serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>());
        Assert.IsAssignableFrom<IResourceOrchestratorServiceProcedureProvider>(
            serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>());
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ApplicationProvider_ThrowsWhenContainerHostProcessExitsDuringStartup()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var fakeDocker = CreateFailingContainerHostExecutable(contentRoot);
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:test",
                "Test Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cloudshell.executable"] = fakeDocker
                },
                Capabilities:
                [
                    ContainerHostCapabilityIds.ContainerImage,
                    ContainerHostCapabilityIds.StorageMountFileSystem
                ]))
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
                options.ContainerStartConfirmationDelay = TimeSpan.FromMilliseconds(25);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        try
        {
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:sql",
                    "SQL Server",
                    string.Empty,
                    containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
                    containerHostId: "docker:test",
                    resourceType: ApplicationResourceTypes.SqlServer),
                resourceGroupId: null,
                registrations);
            var resourceManager = new TestResourceManagerStore(resourceProvider, registrations);
            var resource = Assert.Single(resourceManager.GetResources());
            var orchestrator = new DefaultResourceOrchestrator();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ExecuteActionAsync(
                    new ResourceOrchestrationContext(
                        resource,
                        registrations.GetRegistration(resource.Id),
                        null,
                        resourceManager,
                        registrations),
                    ResourceAction.Start));

            Assert.Contains("exited during startup", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Cannot connect to Docker daemon", exception.Message, StringComparison.Ordinal);
            Assert.Equal(ResourceState.Stopped, Assert.Single(resourceManager.GetResources()).State);
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
    [Trait("Category", "Integration")]
    public async Task ApplicationProvider_RemovesStaleControlPlaneScopedContainerBeforeStart()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var commandLogPath = Path.Combine(contentRoot, "fake-docker-commands.log");
        var fakeDocker = CreateRecordingContainerHostExecutable(contentRoot, commandLogPath);
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:test",
                "Test Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cloudshell.executable"] = fakeDocker
                },
                Capabilities:
                [
                    ContainerHostCapabilityIds.ContainerImage,
                    ContainerHostCapabilityIds.StorageMountFileSystem
                ]))
            .AddApplicationProvider(options =>
            {
                options.DefinitionsPath = "application-resources.json";
                options.RuntimeStatePath = "application-runtime-state.json";
                options.LogDirectory = "application-logs";
                options.ContainerStartConfirmationDelay = TimeSpan.FromMilliseconds(25);
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceProvider = serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>();
            var registrations = new MutableResourceRegistrationStore();
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:sql",
                    "SQL Server",
                    string.Empty,
                    containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
                    containerHostId: "docker:test",
                    lifetime: ApplicationLifetime.ControlPlaneScoped,
                    resourceType: ApplicationResourceTypes.SqlServer),
                resourceGroupId: null,
                registrations);
            var resourceManager = new TestResourceManagerStore(resourceProvider, registrations);
            var resource = Assert.Single(resourceManager.GetResources());
            var orchestrator = new DefaultResourceOrchestrator();

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                orchestrator.ExecuteActionAsync(
                    new ResourceOrchestrationContext(
                        resource,
                        registrations.GetRegistration(resource.Id),
                        null,
                        resourceManager,
                        registrations),
                    ResourceAction.Start));

            var commands = File.ReadAllLines(commandLogPath);
            var rmIndex = Array.FindIndex(
                commands,
                command => command == "rm -f cloudshell-application-sql");
            var runIndex = Array.FindIndex(
                commands,
                command => command.StartsWith("run --name cloudshell-application-sql ", StringComparison.Ordinal));

            Assert.True(rmIndex >= 0, string.Join(Environment.NewLine, commands));
            Assert.True(runIndex >= 0, string.Join(Environment.NewLine, commands));
            Assert.True(rmIndex < runIndex, string.Join(Environment.NewLine, commands));
            Assert.Contains(
                " --rm ",
                commands[runIndex],
                StringComparison.Ordinal);
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
    [Trait("Category", "Integration")]
    public async Task ApplicationProvider_StopRemovesControlPlaneScopedContainer()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var commandLogPath = Path.Combine(contentRoot, "fake-docker-commands.log");
        var fakeDocker = CreateRecordingContainerHostExecutable(contentRoot, commandLogPath);
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
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var engine = new ContainerHostDescriptor(
                "docker:test",
                "Test Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["cloudshell.executable"] = fakeDocker
                });
            var definition = new ApplicationResourceDefinition(
                "application:sql",
                "SQL Server",
                string.Empty,
                containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
                lifetime: ApplicationLifetime.ControlPlaneScoped,
                resourceType: ApplicationResourceTypes.SqlServer);
            var method = typeof(ApplicationResourceService).GetMethod(
                "StopContainerApplicationInstanceAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                [
                    typeof(ApplicationResourceDefinition),
                    typeof(ContainerHostDescriptor),
                    typeof(ApplicationProcessLog),
                    typeof(ResourceOrchestratorServiceInstance),
                    typeof(CancellationToken),
                    typeof(ResourceProcedureContext)
                ]);

            Assert.NotNull(method);

            var task = Assert.IsAssignableFrom<Task>(method.Invoke(
                provider,
                [
                    definition,
                    engine,
                    new ApplicationProcessLog(),
                    new ResourceOrchestratorServiceInstance(
                        "cloudshell-application-sql",
                        1,
                        1),
                    CancellationToken.None,
                    null
                ]));
            await task;

            var commands = File.ReadAllLines(commandLogPath);

            Assert.Contains("stop cloudshell-application-sql", commands);
            Assert.Contains("rm -f cloudshell-application-sql", commands);
            Assert.True(
                Array.IndexOf(commands, "stop cloudshell-application-sql") <
                Array.IndexOf(commands, "rm -f cloudshell-application-sql"));
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
    [Trait("Category", "Integration")]
    public async Task ApplicationProvider_CancellingContainerHostCommandKillsProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        var pidPath = Path.Combine(contentRoot, "fake-docker.pid");
        var fakeDocker = CreateHangingContainerHostExecutable(contentRoot, pidPath);
        var engine = new ContainerHostDescriptor(
            "docker:test",
            "Test Docker",
            ContainerHostKind.Docker,
            "unix:///var/run/docker.sock",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["cloudshell.executable"] = fakeDocker
            });
        var method = typeof(ApplicationResourceService).GetMethod(
            "RunContainerHostCommandAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        using var cancellation = new CancellationTokenSource();
        var task = Assert.IsAssignableFrom<Task<int>>(method.Invoke(
            null,
            [
                engine,
                new[] { "version" },
                new ApplicationProcessLog(),
                cancellation.Token,
                null
            ]));
        var processId = await WaitForProcessIdFileAsync(pidPath, TimeSpan.FromSeconds(5));
        using var process = Process.GetProcessById(processId);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.True(await WaitForExitAsync(process, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ApplicationProvider_FormatsContainerHostCommandLineForLogs()
    {
        var commandLine = ApplicationResourceService.FormatContainerHostCommandLine(
            ["rm", "-f", "application topology api"]);

        Assert.Equal("rm -f \"application topology api\"", commandLine);
    }

    [Fact]
    public void LocalProcessRunner_FormatsProcessCommandLineForLogs()
    {
        var commandLine = LocalProcessRunner.FormatLocalProcessCommandLine(
            "dotnet",
            ["run", "--project", "samples/Application Topology/Host"]);

        Assert.Equal("dotnet run --project \"samples/Application Topology/Host\"", commandLine);
    }

    [Fact]
    public void ApplicationProvider_RegistersResourceMonitoringProvider()
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

        using var serviceProvider = services.BuildServiceProvider();

        Assert.Contains(
            serviceProvider.GetRequiredService<IEnumerable<IResourceMonitoringProvider>>(),
            provider => provider is ApplicationResourceService);
    }

    [Fact]
    public async Task ApplicationProvider_MonitorsStoppedProcessResourcesAsUnavailable()
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
                    executablePath: "dotnet");
                resources.AddAspNetCoreProject(
                    "application:api",
                    "src/API/API.csproj");
                resources.AddContainer(
                    "redis",
                    "redis:7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resources = provider.GetResources();
        var worker = Assert.Single(resources, resource => resource.Id == "application:worker");
        var api = Assert.Single(resources, resource => resource.Id == "application:api");
        var redis = Assert.Single(resources, resource => resource.Id == "application:redis");

        Assert.True(provider.CanMonitor(worker));
        Assert.True(provider.CanMonitor(api));
        Assert.True(provider.CanMonitor(redis));
        Assert.True(worker.HasCapability(ResourceCapabilityIds.LogSources));
        Assert.True(api.HasCapability(ResourceCapabilityIds.LogSources));
        Assert.True(redis.HasCapability(ResourceCapabilityIds.LogSources));
        Assert.True(worker.SupportsLogSources);
        Assert.True(api.SupportsLogSources);
        Assert.True(redis.SupportsLogSources);
        Assert.True(worker.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.True(api.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.True(redis.HasCapability(ResourceCapabilityIds.Monitoring));

        var snapshot = await provider.GetMonitoringSnapshotAsync(worker);

        Assert.NotNull(snapshot);
        Assert.Equal(worker.Id, snapshot.ResourceId);
        Assert.Equal("Applications", snapshot.Provider);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
    }

    [Fact]
    public void ApplicationContainerMonitoringMetrics_ParsesContainerStatsJson()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var json = """
            {"Name":"app","CPUPerc":"12.5%","MemUsage":"49.13MiB / 2GiB","NetIO":"1.5kB / 2MB","BlockIO":"3MiB / 4MiB","PIDs":"26"}
            """;

        var parsed = ApplicationContainerMonitoringMetrics.TryParseStatsJson(
            json,
            timestamp,
            out var snapshot);
        var samples = ApplicationContainerMonitoringMetrics.CreateMetricSamples(snapshot)
            .ToDictionary(metric => metric.Name, StringComparer.OrdinalIgnoreCase);

        Assert.True(parsed);
        Assert.Equal("app", snapshot.ContainerName);
        Assert.Equal(12.5, snapshot.CpuUsagePercent);
        Assert.Equal(26, snapshot.ProcessCount);
        Assert.Equal(49.13 * 1024 * 1024, snapshot.MemoryUsageBytes, precision: 1);
        Assert.Equal(2d * 1024 * 1024 * 1024, snapshot.MemoryLimitBytes, precision: 1);
        Assert.Contains("resource.cpu.usage", samples.Keys);
        Assert.Contains("resource.network.rxBytes", samples.Keys);
        Assert.Contains("resource.block.writeBytes", samples.Keys);
    }

    [Fact]
    public async Task ApplicationProvider_MonitorsStoppedSingleContainerResourcesAsUnavailable()
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
                resources.AddContainer(
                    "redis",
                    "redis:7.2");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:redis");

        Assert.True(provider.CanMonitor(resource));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.LogSources));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.Monitoring));

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Applications", snapshot.Provider);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
    }

    [Fact]
    public async Task ApplicationProvider_UsesReplicaChildrenForReplicaModeContainerMonitoring()
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
                resources.AddContainer(
                    "api",
                    "example/api:latest",
                    replicas: 2);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");
        var replica = provider.GetResources()
            .Where(resource =>
                string.Equals(resource.ParentResourceId, "application:api", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .First();

        Assert.False(provider.CanMonitor(resource));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.Null(await provider.GetMonitoringSnapshotAsync(resource));
        Assert.True(replica.HasCapability(ResourceCapabilityIds.Monitoring));
        Assert.True(provider.CanMonitor(replica));

        var snapshot = await provider.GetMonitoringSnapshotAsync(replica);

        Assert.NotNull(snapshot);
        Assert.Equal(replica.Id, snapshot.ResourceId);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
    }

    [Fact]
    public async Task ApplicationProvider_ProjectsSqlServerAsServiceResource()
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

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:sql-server",
                "SQL Server",
                string.Empty,
                containerImage: "mcr.microsoft.com/mssql/server:2022-latest",
                resourceType: ApplicationResourceTypes.SqlServer),
            resourceGroupId: null,
            registrations: new MutableResourceRegistrationStore());

        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:sql-server");

        Assert.Equal(ApplicationResourceTypes.SqlServer, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Service, resource.ResourceClass);
        Assert.Equal(
            "mcr.microsoft.com/mssql/server:2022-latest",
            resource.ResourceAttributes[ResourceAttributeNames.ContainerImage]);
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
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();

        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");

        Assert.Equal(ResourceState.Starting, resource.State);
        Assert.True(resource.HasAction(ResourceActionIds.Stop));
        Assert.True(resource.HasAction(ResourceActionIds.Restart));
        Assert.False(resource.HasAction(ResourceActionIds.Start));
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
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();

        var resource = Assert.Single(provider.GetResources(), resource => resource.Id == "application:api");

        Assert.Equal(ResourceState.Stopped, resource.State);
        Assert.True(resource.HasAction(ResourceActionIds.Start));
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
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets")
                    .WithSecret("db-password", "local-dev-password");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);

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
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
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
    public void ConfigurationStores_FilterUnsupportedEntryAndSecretNames()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.DefinitionsPath = "configuration-stores.json";
                options.SecretsVaultDefinitionsPath = "secrets-vaults.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        var configurationStore = serviceProvider.GetRequiredService<ConfigurationStore>();
        var secretsVaultStore = serviceProvider.GetRequiredService<SecretsVaultStore>();

        configurationStore.Save(
            new ConfigurationStoreDefinition(
                "configuration:orders",
                "Orders Configuration",
                [
                    new(" Orders--Api--BaseUrl ", "http://localhost:5080"),
                    new("Orders:Api:Timeout", "00:00:30"),
                    new("Bad\nKey", "ignored"),
                    new("Bad%Key", "ignored"),
                    new(".", "ignored"),
                    new("..", "ignored"),
                    new(" ", "ignored")
                ]));
        secretsVaultStore.Save(
            new SecretsVaultDefinition(
                "secrets-vault:orders",
                "Orders Secrets",
                [
                    new(" Orders--Api--ClientSecret ", "secret"),
                    new("Orders:Api:InvalidSecret", "ignored"),
                    new("Orders_Api_InvalidSecret", "ignored"),
                    new("Bad\nSecret", "ignored"),
                    new(" ", "ignored")
                ]));

        var configurationEntries =
            configurationStore.GetStore("configuration:orders")?.Entries ?? [];
        var secret = Assert.Single(
            secretsVaultStore.GetVault("secrets-vault:orders")?.Secrets ?? []);

        Assert.Collection(
            configurationEntries,
            entry => Assert.Equal("Orders--Api--BaseUrl", entry.Name),
            entry => Assert.Equal("Orders:Api:Timeout", entry.Name));
        Assert.Equal("Orders--Api--ClientSecret", secret.Name);
    }

    [Fact]
    public void ConfigurationProviders_RegisterResourceMonitoringProviders()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.DefinitionsPath = "configuration-stores.json";
                options.SecretsVaultDefinitionsPath = "secrets-vaults.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        var providers = serviceProvider.GetRequiredService<IEnumerable<IResourceMonitoringProvider>>();

        Assert.Contains(providers, provider => provider is ConfigurationResourceProvider);
        Assert.Contains(providers, provider => provider is SecretsVaultProvider);
    }

    [Fact]
    public async Task ConfigurationProvider_MonitorsStoppedServiceResourceAsUnavailable()
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
        var resource = Assert.Single(provider.GetResources());

        Assert.True(provider.CanMonitor(resource));

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Configuration Store", snapshot.Provider);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
    }

    [Fact]
    public async Task SecretsVaultProvider_MonitorsStoppedServiceResourceAsUnavailable()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddConfigurationProvider(options =>
            {
                options.SecretsVaultDefinitionsPath = "secrets-vaults.json";
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<SecretsVaultProvider>();
        serviceProvider.GetRequiredService<SecretsVaultStore>().Save(
            new SecretsVaultDefinition(
                "secrets-vault:example",
                "Example Secrets"));
        var resource = Assert.Single(provider.GetResources());

        Assert.True(provider.CanMonitor(resource));

        var snapshot = await provider.GetMonitoringSnapshotAsync(resource);

        Assert.NotNull(snapshot);
        Assert.Equal(resource.Id, snapshot.ResourceId);
        Assert.Equal("Secrets Vault", snapshot.Provider);
        Assert.Equal("Unavailable", snapshot.Status);
        Assert.Empty(snapshot.Metrics);
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
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings")
                    .WithEntry("Message", "hello")
                    .Allow(api.Principal, ConfigurationStoreResourceOperationPermissions.ReadEntries);
                resources
                    .AddConfigurationStore("configuration:other").WithDisplayName("Other Settings")
                    .WithEntry("Message", "other");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var resolver = serviceProvider.GetRequiredService<ConfigurationResourceProvider>();
        var context = new ResourceSettingResolutionContext(
            "application:api",
            "group-1",
            "run",
            ResourceIdentityReference.ForResource("application:api", "api-service"),
            "api/api-service");

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
        Assert.Contains("Identity 'api/api-service'", denied.ErrorMessage);
        Assert.Contains("Other Settings", denied.ErrorMessage);
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
                    .AddHostConfigurationSource("configuration:host-dev").WithDisplayName("Host Development Settings")
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
                    .AddHostConfigurationSource("configuration:host-dev").WithDisplayName("Host Development Settings")
                    .WithEntry("ExternalApi:BaseUrl");

                resources
                    .AddExecutableApplication(
                        "application:api",
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
                var settings = resources
                    .AddConfigurationStore("configuration:settings")
                    .WithDisplayName("Settings");
                var postgres = resources.Declare("managed", "postgres-main");

                resources
                    .AddExecutableApplication(
                        "application:api",
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
    public void TypedExecutableBuilder_TreatsConfigurationAndSecretsAsServiceDiscoveryReferences()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var settings = resources
                    .AddConfigurationStore("configuration:settings")
                    .WithDisplayName("Settings");
                var secrets = resources
                    .AddSecretsVault("secrets-vault:app")
                    .WithDisplayName("App Secrets");

                resources
                    .AddExecutableApplication(
                        "application:api",
                        executablePath: "dotnet")
                    .WithReference(settings)
                    .WithReference(secrets)
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

        Assert.Empty(declaration.DependsOn);
        Assert.Equal(
            ["configuration:settings", "secrets-vault:app"],
            application.References);
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
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings");
                var secrets = resources
                    .AddSecretsVault("secrets-vault:app").WithDisplayName("App Secrets");

                resources
                    .AddExecutableApplication(
                        "application:api",
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
        services.AddSingleton<RecordingResourceEventSink>();
        services.AddSingleton<IResourceEventSink>(
            serviceProvider => serviceProvider.GetRequiredService<RecordingResourceEventSink>());
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
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceEvents = serviceProvider.GetRequiredService<RecordingResourceEventSink>();
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
            var resourceEvent = Assert.Single(resourceEvents.Events);
            Assert.Equal("application:api", resourceEvent.ResourceId);
            Assert.Equal(
                ResourceEventTypes.Events.Configuration.EnvironmentVariablesUpdated,
                resourceEvent.EventType);
            Assert.Equal("Updated 3 environment variables.", resourceEvent.Message);
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
        services.AddSingleton<RecordingResourceEventSink>();
        services.AddSingleton<IResourceEventSink>(
            serviceProvider => serviceProvider.GetRequiredService<RecordingResourceEventSink>());
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
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resourceEvents = serviceProvider.GetRequiredService<RecordingResourceEventSink>();
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
            var resourceEvent = Assert.Single(resourceEvents.Events);
            Assert.Equal("application:api", resourceEvent.ResourceId);
            Assert.Equal(
                ResourceEventTypes.Events.Configuration.AppSettingsUpdated,
                resourceEvent.EventType);
            Assert.Equal("Updated 3 app settings.", resourceEvent.Message);
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
                var settings = resources
                    .AddConfigurationStore("configuration:settings")
                    .WithDisplayName("Settings");

                resources
                    .AddAspNetCoreProject(
                        "application:api",
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
        Assert.False(application.AspNetCoreHotReload);
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
        Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
        Assert.Equal("localhost", port.Host);
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
                        "src/API/API.csproj")
                    .WithOtlpExporter(headers: "x-otlp-api-key=test-key");
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
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
            Assert.Equal("false", resource.ResourceAttributes[ResourceAttributeNames.ProjectHotReload]);
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
                Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
            },
            port =>
            {
                Assert.Equal("http", port.Name);
                Assert.Equal(80, port.TargetPort);
                Assert.Equal(5127, port.Port);
                Assert.Equal("http", port.Protocol);
                Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
            });
    }

    [Fact]
    public void TypedAspNetCoreProjectBuilder_CanDeclareAutoAssignedEndpoint()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj")
                    .WithHttpEndpoint();
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
        Assert.Equal(ResourceEndpointAssignment.Auto, port.Assignment);
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
    public void TypedAspNetCoreProjectBuilder_LeavesEndpointPortUnsetWhenOmitted()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
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
        Assert.Empty(application.EndpointPorts);
    }

    [Fact]
    public void ApplicationProvider_AssignsStableEndpointWhenProjectEndpointIsOmitted()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));

        services
            .AddControlPlane()
            .AddApplicationProvider(options => options.DefinitionsPath = "application-resources.json")
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
                    "src/API/API.csproj");
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "application:api");
            var endpoint = Assert.Single(resource.Endpoints);

            Assert.Equal("http", endpoint.Name);
            Assert.Equal("http", endpoint.Protocol);
            Assert.Equal(80, endpoint.TargetPort);

            var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
            Assert.Equal("http", mapping.Name);
            Assert.Equal(new ResourceEndpointReference(resource.Id, endpoint.Name), mapping.Target);
            Assert.StartsWith("http://localhost:", mapping.Address, StringComparison.Ordinal);
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
    public async Task ApplicationProvider_ProjectsEndpointNetworkAssignmentMetadata()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));

        services
            .AddControlPlane()
            .AddApplicationProvider(options => options.DefinitionsPath = "application-resources.json");

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            await provider.SetupApplicationAsync(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    string.Empty,
                    endpointPorts:
                    [
                        new ServicePort(
                            "http",
                            80,
                            6000,
                            "http",
                            ResourceExposureScope.Local,
                            ResourceEndpointAssignment.Manual,
                            "network:tenant",
                            "127.0.0.2")
                    ],
                    resourceType: ApplicationResourceTypes.AspNetCoreProject,
                    projectPath: "src/API/API.csproj"),
                resourceGroupId: null,
                registrations: new MutableResourceRegistrationStore());

            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "application:api");
            var endpoint = Assert.Single(resource.Endpoints);
            var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);

            Assert.Equal(80, endpoint.TargetPort);
            Assert.Equal("network:tenant", mapping.NetworkResourceId);
            Assert.Equal("http://127.0.0.2:6000", mapping.Address);
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
    public void TypedAspNetCoreProjectBuilder_CanOptIntoLaunchSettingsEndpoints()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj")
                    .WithLaunchSettingsEndpoints();
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

        Assert.True(application.UseLaunchSettingsEndpoints);
        Assert.Empty(application.EndpointPorts);
    }

    [Fact]
    public void ApplicationProvider_UsesLaunchSettingsEndpointsWhenOptedIn()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        WriteLaunchSettings(
            contentRoot,
            "src/API/API.csproj",
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "https://localhost:7123;http://localhost:5123"
                }
              }
            }
            """);

        services
            .AddControlPlane()
            .AddApplicationProvider(options => options.DefinitionsPath = "application-resources.json")
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj")
                    .WithLaunchSettingsEndpoints();
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "application:api");

            Assert.Collection(
                resource.Endpoints.OrderBy(endpoint => endpoint.Name, StringComparer.OrdinalIgnoreCase),
                endpoint =>
                {
                    Assert.Equal("http", endpoint.Name);
                    Assert.Equal("http", endpoint.Protocol);
                },
                endpoint =>
                {
                    Assert.Equal("https", endpoint.Name);
                    Assert.Equal("https", endpoint.Protocol);
                });
            Assert.Collection(
                resource.ResourceEndpointNetworkMappings.OrderBy(mapping => mapping.Name, StringComparer.OrdinalIgnoreCase),
                mapping =>
                {
                    Assert.Equal("http", mapping.Name);
                    Assert.Equal("http://localhost:5123", mapping.Address);
                },
                mapping =>
                {
                    Assert.Equal("https", mapping.Name);
                    Assert.Equal("https://localhost:7123", mapping.Address);
                });
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
    public void ApplicationProvider_ProgrammaticEndpointsOverrideLaunchSettingsEndpoints()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        WriteLaunchSettings(
            contentRoot,
            "src/API/API.csproj",
            """
            {
              "profiles": {
                "https": {
                  "commandName": "Project",
                  "applicationUrl": "http://localhost:5123"
                }
              }
            }
            """);

        services
            .AddControlPlane()
            .AddApplicationProvider(options => options.DefinitionsPath = "application-resources.json")
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj")
                    .WithHttpEndpoint(port: 6000)
                    .WithLaunchSettingsEndpoints();
            });

        try
        {
            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "application:api");
            var endpoint = Assert.Single(resource.Endpoints);
            var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);

            Assert.Equal("http", endpoint.Name);
            Assert.Equal("http://localhost:6000", mapping.Address);
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
    public void TypedAspNetCoreProjectBuilder_UsesStableHttpEndpointNameForHttpsEndpoint()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddAspNetCoreProject(
                    "application:api",
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
    public void TypedAspNetCoreProjectBuilder_CanEnableHotReload()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj",
                        hotReload: true)
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
        Assert.True(application.AspNetCoreHotReload);
        Assert.Null(application.Arguments);
    }

    [Fact]
    public void AspNetCoreProjectRunner_UsesDotNetRunByDefault()
    {
        var arguments = BuildDotNetAspNetCoreProjectArguments(
            "src/API/API.csproj",
            hotReload: false,
            applicationArguments: "--seed");

        Assert.Equal("run --project src/API/API.csproj --no-build --no-launch-profile -- --seed", arguments);
    }

    [Fact]
    public void AspNetCoreProjectRunner_UsesNonInteractiveWatchWhenHotReloadIsEnabled()
    {
        var arguments = BuildDotNetAspNetCoreProjectArguments(
            "src/API/API.csproj",
            hotReload: true,
            applicationArguments: "--seed");

        Assert.Equal(
            "watch --non-interactive --project src/API/API.csproj run --no-launch-profile -- --seed",
            arguments);
    }

    [Fact]
    public void AspNetCoreProjectRunner_AddsRudeEditRestartEnvironmentWhenHotReloadIsEnabled()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            endpointPorts:
            [
                new ServicePort("http", 5127, 5127, "http")
            ],
            resourceType: ApplicationResourceTypes.AspNetCoreProject,
            projectPath: "src/API/API.csproj",
            aspNetCoreHotReload: true);
        var method = typeof(ApplicationResourceService).GetMethod(
            "ResolveAspNetCoreProjectEnvironmentVariables",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var variables = Assert.IsAssignableFrom<IReadOnlyList<EnvironmentVariableAssignment>>(
            method!.Invoke(provider, [application, null]));
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal("http://localhost:5127", environment["ASPNETCORE_URLS"]);
        Assert.Equal("true", environment["DOTNET_WATCH_RESTART_ON_RUDE_EDIT"]);
    }

    [Fact]
    public void AspNetCoreProjectRunner_UsesProjectedEndpointNetworkMappingsForUrls()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            string.Empty,
            endpointPorts:
            [
                new ServicePort("http", 80, 5127, "http")
            ],
            resourceType: ApplicationResourceTypes.AspNetCoreProject,
            projectPath: "src/API/API.csproj");
        var resource = new Resource(
            application.Id,
            "api",
            "ASP.NET Core project",
            "Applications",
            "local",
            ResourceState.Stopped,
            [ResourceEndpoint.FromAddress("http", "http://localhost:5127", "http", ResourceExposureScope.Local, 80)],
            "project",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ApplicationResourceTypes.AspNetCoreProject,
            ResourceClass: ResourceClass.Project,
            EndpointNetworkMappings:
            [
                new ResourceEndpointNetworkMapping(
                    "application:api:endpoint-network-mapping:http",
                    "http",
                    new ResourceEndpointReference(application.Id, "http"),
                    "http://127.0.0.2:6000",
                    ResourceExposureScope.Private,
                    SourceEndpointName: "http")
            ]);
        var resourceManager = new StaticResourceManagerStore([resource]);
        var method = typeof(ApplicationResourceService).GetMethod(
            "ResolveAspNetCoreProjectEnvironmentVariables",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        var variables = Assert.IsAssignableFrom<IReadOnlyList<EnvironmentVariableAssignment>>(
            method!.Invoke(provider, [application, resourceManager]));
        var environment = variables.ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal("http://127.0.0.2:6000", environment["ASPNETCORE_URLS"]);
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
        Assert.Equal(ResourceSource.User, resource.Source);
        Assert.Equal(ResourceManagementMode.UserManaged, resource.ManagementMode);
        Assert.Equal(ResourceVisibility.Normal, resource.Visibility);
    }

    [Fact]
    public void DockerProvider_ProjectsDiscoveredContainersAsHiddenRuntimeManagedArtifacts()
    {
        var mapContainer = typeof(DockerContainerResourceProvider)
            .GetMethod("MapContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var container = new DockerContainerListResponse
        {
            ID = "0123456789abcdef",
            Names = ["/redis-runtime"],
            Image = "redis:7.2",
            State = "running",
            Created = DateTime.UtcNow,
            Ports =
            [
                new DockerPort
                {
                    IP = "0.0.0.0",
                    PrivatePort = 6379,
                    PublicPort = 16379,
                    Type = "tcp"
                }
            ]
        };

        var resource = Assert.IsType<Resource>(mapContainer?.Invoke(
            null,
            [container, DockerContainerResourceProvider.DefaultHostResourceId]));

        Assert.Equal("docker:container:0123456789abcdef", resource.Id);
        Assert.Equal("redis-runtime", resource.Name);
        Assert.Equal("docker.container", resource.EffectiveTypeId);
        Assert.Equal(DockerContainerResourceProvider.DefaultHostResourceId, resource.ParentResourceId);
        Assert.Equal(ResourceClass.Container, resource.ResourceClass);
        Assert.Equal(ResourceSource.RuntimeController, resource.Source);
        Assert.Equal(ResourceManagementMode.RuntimeManaged, resource.ManagementMode);
        Assert.Equal(ResourceVisibility.Hidden, resource.Visibility);
        Assert.Equal(ResourceCleanupBehavior.None, resource.CleanupBehavior);
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("port-1", endpoint.Name);
        Assert.Equal("tcp", endpoint.Protocol);
        Assert.Equal(6379, endpoint.TargetPort);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.Equal("tcp://localhost:16379", mapping.Address);
        Assert.Equal(new ResourceEndpointReference(resource.Id, endpoint.Name), mapping.Target);
    }

    [Fact]
    public async Task DockerProvider_ReportsOccupiedDeclaredContainerEndpointAsActionUnavailable()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var services = new ServiceCollection();

        try
        {
            services
                .AddControlPlane()
                .AddDockerProvider()
                .Resources(resources =>
                {
                    resources
                    .AddDocker("docker:sample")
                    .WithDisplayName("Sample Docker")
                    .AddDockerContainer(
                        "sample-registry",
                        "registry:2")
                    .WithDisplayName("Local Registry")
                    .WithEndpoint(
                        "http",
                        targetPort: 5000,
                        port: port,
                        host: "127.0.0.1",
                        protocol: "http",
                        exposure: ResourceExposureScope.Public);
                });

            using var serviceProvider = services.BuildServiceProvider();
            var provider = serviceProvider.GetRequiredService<DockerContainerResourceProvider>();
            var actionAvailabilityProvider = Assert.Single(
                serviceProvider.GetServices<IResourceActionAvailabilityProvider>(),
                candidate => ReferenceEquals(candidate, provider));
            var resource = Assert.Single(provider.GetResources(), resource =>
                resource.Id == "docker:container:sample-registry");
            var endpoint = Assert.Single(resource.Endpoints);
            Assert.Equal("http", endpoint.Name);
            Assert.Equal("http", endpoint.Protocol);
            Assert.Equal(5000, endpoint.TargetPort);
            var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
            Assert.Equal("http", mapping.Name);
            Assert.Equal(new ResourceEndpointReference(resource.Id, endpoint.Name), mapping.Target);
            Assert.Equal($"http://127.0.0.1:{port}", mapping.Address);
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

            var reason = await actionAvailabilityProvider.GetActionUnavailableReasonAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                ResourceAction.Start);

            Assert.Equal(
                $"Endpoint mapping 'http' for Docker container resource 'docker:container:sample-registry' cannot use http://127.0.0.1:{port} because the address is already in use.",
                reason);
        }
        finally
        {
            listener.Stop();
        }
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
                    .AddDocker("docker:dev")
                    .WithDisplayName("Development Docker")
                    .AddContainer("redis-dev", "redis", "7.2");

                resources
                    .AddDocker("docker:test")
                    .WithDisplayName("Test Docker")
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
            [
                ContainerHostCapabilityIds.ContainerBuild,
                ContainerHostCapabilityIds.ContainerImage,
                ContainerHostCapabilityIds.StorageMountFileSystem
            ],
            definition.HostCapabilities.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(DockerContainerResourceProvider.HostResourceType, host.EffectiveTypeId);
        Assert.True(host.HasCapability(ResourceCapabilityIds.ContainerHost));
        var endpoint = Assert.Single(host.Endpoints);
        Assert.Equal("host", endpoint.Name);
        Assert.Equal(provider.Endpoint.Scheme, endpoint.Protocol);
        var endpointMapping = Assert.Single(host.ResourceEndpointNetworkMappings);
        Assert.Equal("host", endpointMapping.Name);
        Assert.Equal(new ResourceEndpointReference(host.Id, "host"), endpointMapping.Target);
        Assert.Equal(provider.Endpoint.ToString().TrimEnd('/'), endpointMapping.Address);
        Assert.Equal("local", host.ResourceAttributes["docker.host.kind"]);
        Assert.Equal(ContainerRegistryDefaults.Default, host.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
    }

    [Fact]
    public async Task DockerProvider_RefreshAfterDisposeDoesNotReconnect()
    {
        var options = new DockerProviderOptions
        {
            Endpoint = new Uri("tcp://127.0.0.1:1"),
            RequestTimeout = TimeSpan.FromMilliseconds(10)
        };
        var provider = new DockerContainerResourceProvider(options);

        provider.Dispose();

        await provider.RefreshAsync();
    }

    [Fact]
    public async Task DockerProvider_ReportsUnavailableHostCredentialsInDescriptor()
    {
        const string passwordVariable = "CLOUDSHELL_TEST_MISSING_DOCKER_HOST_PASSWORD";
        Environment.SetEnvironmentVariable(passwordVariable, null);
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources
                    .AddDocker("docker:build-01")
                    .WithDisplayName("Build Host 01")
                    .UseRemoteHost(new Uri("tcp://build-01.example.com:2375"))
                    .WithHostCredentialsFromEnvironment("docker-user", passwordVariable);
            });

        using var serviceProvider = services.BuildServiceProvider();
        using var provider = new DockerContainerResourceProvider(
            serviceProvider.GetRequiredService<DockerProviderOptions>());
        var host = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "docker:build-01");

        var descriptor = await provider.DescribeAsync(
            host,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var definition = descriptor.Configuration.Deserialize<ContainerHostDescriptor>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(definition);
        Assert.False(definition.CredentialsAvailable);
        Assert.Equal("tcp://build-01.example.com", definition.Endpoint);
        Assert.DoesNotContain(passwordVariable, definition.HostMetadata.Values);
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
                    .AddDocker("docker:build-01")
                    .WithDisplayName("Build Host 01")
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
        var endpoint = Assert.Single(host.Endpoints);
        Assert.Equal("host", endpoint.Name);
        Assert.Equal("tcp", endpoint.Protocol);
        Assert.Equal(2375, endpoint.TargetPort);
        var endpointMapping = Assert.Single(host.ResourceEndpointNetworkMappings);
        Assert.Equal("host", endpointMapping.Name);
        Assert.Equal(new ResourceEndpointReference(host.Id, "host"), endpointMapping.Target);
        Assert.Equal("tcp://build-01.example.com", endpointMapping.Address);
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
            [
                ContainerHostCapabilityIds.ContainerBuild,
                ContainerHostCapabilityIds.ContainerImage,
                ContainerHostCapabilityIds.StorageMountFileSystem
            ],
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
                    .AddDockerContainer("rabbitmq", "rabbitmq:4")
                    .WithDisplayName("RabbitMQ")
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
                var network = resources.AddNetwork("network:app").WithDisplayName("App Network");
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddService("service:api")
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

        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "service:api");
        var endpoint = Assert.Single(resource.Endpoints);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);

        Assert.Equal("http", endpoint.Name);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Equal("http://localhost:5080", mapping.Address);
        Assert.Equal(new ResourceEndpointReference(resource.Id, endpoint.Name), mapping.Target);
    }

    [Fact]
    public void PlatformResources_DeclareVolume()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var app = resources.Declare("applications", "application:postgres");
                resources
                    .AddVolume("postgres-data").WithDisplayName("Postgres Data")
                    .UseHostPath("./data/postgres")
                    .WithAccessMode(VolumeAccessMode.ReadWriteOnce)
                    .Persist()
                    .Allow(app, StorageVolumeResourceOperationPermissions.MountWrite);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "volume:postgres-data");

        Assert.Equal(PlatformResourceProvider.ProviderId, declaration.ProviderId);
        Assert.Equal(ResourceClass.Storage, declaration.ResourceClassOverride);
        Assert.Equal(ResourceDeclarationPersistence.Persisted, declaration.Persistence);
        Assert.Equal(StorageProviderNames.LocalStorage, declaration.ResourceAttributes[ResourceAttributeNames.VolumeProvider]);
        Assert.Equal(StorageMedia.FileSystem, declaration.ResourceAttributes[ResourceAttributeNames.VolumeStorageMedium]);
        Assert.Equal("./data/postgres", declaration.ResourceAttributes[ResourceAttributeNames.VolumeLocation]);
        Assert.Equal("ReadWriteOnce", declaration.ResourceAttributes[ResourceAttributeNames.VolumeAccessMode]);

        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var definition = Assert.Single(options.DeclaredVolumes).Definition;

        Assert.Equal("volume:postgres-data", definition.Id);
        Assert.Equal("Postgres Data", definition.Name);
        Assert.Equal(StorageProviderNames.LocalStorage, definition.Provider);
        Assert.Equal("./data/postgres", definition.Location);
        Assert.True(definition.Persistent);
        Assert.Equal(VolumeAccessMode.ReadWriteOnce, definition.AccessMode);

        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "volume:postgres-data");

        Assert.Equal(PlatformResourceProvider.VolumeResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Storage, resource.ResourceClass);
        Assert.Equal(ResourceState.Running, resource.State);
        Assert.Empty(resource.Endpoints);
        Assert.Equal(StorageProviderNames.LocalStorage, resource.ResourceAttributes[ResourceAttributeNames.VolumeProvider]);
        Assert.Equal(StorageMedia.FileSystem, resource.ResourceAttributes[ResourceAttributeNames.VolumeStorageMedium]);
        Assert.Equal("./data/postgres", resource.ResourceAttributes[ResourceAttributeNames.VolumeLocation]);
        Assert.Equal("ReadWriteOnce", resource.ResourceAttributes[ResourceAttributeNames.VolumeAccessMode]);
        Assert.Equal("true", resource.ResourceAttributes[ResourceAttributeNames.VolumePersistent]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.StorageVolume));
        Assert.True(resource.IsNormalResource);
        Assert.Equal(ResourceVisibility.Normal, resource.Visibility);
        Assert.Null(resource.ParentResourceId);
        Assert.Null(resource.OwnerResourceId);

        var evaluation = store.CreatePermissionGrantEvaluator().Evaluate(
            ResourceIdentityReference.ForResource("application:postgres"),
            "volume:postgres-data",
            StorageVolumeResourceOperationPermissions.MountWrite);

        Assert.True(evaluation.IsAllowed);
    }

    [Fact]
    public void PlatformResources_DeclareLocalStorageWithOwnedVolume()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var storage = resources
                    .AddLocalStorage("local").WithDisplayName("Local Storage")
                    .UseLocation("./storage");

                resources
                    .AddVolume("postgres-data").WithDisplayName("Postgres Data")
                    .UseStorage(storage, "postgres")
                    .WithAccessMode(VolumeAccessMode.ReadWriteOnce);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var storageDeclaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "storage:local");
        var volumeDeclaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "volume:postgres-data");
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var declaredStorage = Assert.Single(options.DeclaredStorages).Definition;
        var declaredVolume = Assert.Single(options.DeclaredVolumes).Definition;
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal(ResourceClass.Storage, storageDeclaration.ResourceClassOverride);
        Assert.Equal(StorageProviderNames.LocalStorage, declaredStorage.Provider);
        Assert.Equal(StorageMedia.FileSystem, declaredStorage.Medium);
        Assert.Equal("./storage", declaredStorage.Location);
        Assert.Equal("storage:local", declaredVolume.StorageResourceId);
        Assert.Equal("postgres", declaredVolume.SubPath);
        Assert.Null(declaredVolume.Location);
        Assert.Equal(["storage:local"], volumeDeclaration.DependsOn);
        Assert.Equal("storage:local", volumeDeclaration.ParentResourceId);

        var storageResource = resources["storage:local"];
        Assert.Equal(PlatformResourceProvider.StorageResourceType, storageResource.EffectiveTypeId);
        Assert.Equal(StorageProviderNames.LocalStorage, storageResource.Kind);
        Assert.Equal(StorageProviderNames.LocalStorage, storageResource.Provider);
        Assert.Equal(StorageMedia.FileSystem, storageResource.Version);
        Assert.Equal(StorageProviderNames.LocalStorage, storageResource.ResourceAttributes[ResourceAttributeNames.StorageProvider]);
        Assert.Equal(StorageMedia.FileSystem, storageResource.ResourceAttributes[ResourceAttributeNames.StorageMedium]);
        Assert.Equal("./storage", storageResource.ResourceAttributes[ResourceAttributeNames.StorageLocation]);
        Assert.Equal("1", storageResource.ResourceAttributes[ResourceAttributeNames.StorageVolumeCount]);
        Assert.True(storageResource.HasCapability(ResourceCapabilityIds.StorageProvider));
        Assert.True(storageResource.HasCapability(ResourceCapabilityIds.StorageMountProvider));

        var volumeResource = resources["volume:postgres-data"];
        Assert.Equal("storage:local", volumeResource.ResourceAttributes[ResourceAttributeNames.VolumeStorageResourceId]);
        Assert.Equal(StorageProviderNames.LocalStorage, volumeResource.ResourceAttributes[ResourceAttributeNames.VolumeProvider]);
        Assert.Equal(StorageMedia.FileSystem, volumeResource.ResourceAttributes[ResourceAttributeNames.VolumeStorageMedium]);
        Assert.Equal("postgres", volumeResource.ResourceAttributes[ResourceAttributeNames.VolumeSubPath]);
        Assert.Equal(["storage:local"], volumeResource.DependsOn);
        Assert.Equal("storage:local", volumeResource.ParentResourceId);
        Assert.Equal("storage:local", volumeResource.OwnerResourceId);
        Assert.Equal(ResourceVisibility.Hidden, volumeResource.Visibility);
        Assert.False(volumeResource.IsNormalResource);
    }

    [Fact]
    public void PlatformResources_ProjectLocalStorageRuntimeStatus()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var availablePath = Path.Combine(contentRoot, "Data/storage");
        Directory.CreateDirectory(availablePath);
        var options = new PlatformResourceOptions();
        options.DeclaredStorages.Add(new DeclaredStorageResource(new StorageResourceDefinition(
            "storage:available",
            "Available",
            Location: "./Data/storage")));
        options.DeclaredStorages.Add(new DeclaredStorageResource(new StorageResourceDefinition(
            "storage:missing",
            "Missing",
            Location: "./Data/missing")));
        var environment = new TestHostEnvironment(contentRoot);
        var platformStore = new PlatformResourceStore(options, environment);
        var provider = new PlatformResourceProvider(platformStore, options, environment: environment);

        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        var available = resources["storage:available"];
        Assert.Equal("available", available.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatus]);
        Assert.Equal(
            $"Local Storage root '{Path.GetFullPath(availablePath)}' exists.",
            available.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatusReason]);

        var missing = resources["storage:missing"];
        var expectedMissingPath = Path.GetFullPath(Path.Combine(contentRoot, "Data/missing"));
        Assert.Equal("unavailable", missing.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatus]);
        Assert.Equal(
            $"Local Storage root '{expectedMissingPath}' does not exist yet. Start or restart a consumer to let the runtime materialize it, or create the directory before attaching volumes.",
            missing.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatusReason]);
    }

    [Fact]
    public void PlatformResources_ProjectVolumeRuntimeStatus()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var availablePath = Path.Combine(contentRoot, "Data/volume");
        var storageRoot = Path.Combine(contentRoot, "Data/storage");
        Directory.CreateDirectory(availablePath);
        Directory.CreateDirectory(storageRoot);
        var options = new PlatformResourceOptions();
        options.DeclaredStorages.Add(new DeclaredStorageResource(new StorageResourceDefinition(
            "storage:local",
            "Local Storage",
            Location: "./Data/storage")));
        options.DeclaredVolumes.Add(new DeclaredVolumeResource(new VolumeResourceDefinition(
            "volume:available",
            "Available",
            Location: "./Data/volume")));
        options.DeclaredVolumes.Add(new DeclaredVolumeResource(new VolumeResourceDefinition(
            "volume:missing",
            "Missing",
            Location: "./Data/missing")));
        options.DeclaredVolumes.Add(new DeclaredVolumeResource(new VolumeResourceDefinition(
            "volume:escape",
            "Escape",
            StorageResourceId: "storage:local",
            SubPath: "../escape")));
        var environment = new TestHostEnvironment(contentRoot);
        var platformStore = new PlatformResourceStore(options, environment);
        var provider = new PlatformResourceProvider(platformStore, options, environment: environment);

        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        var available = resources["volume:available"];
        Assert.Equal("available", available.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatus]);
        Assert.Equal(
            $"Local volume path '{Path.GetFullPath(availablePath)}' exists.",
            available.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatusReason]);

        var missing = resources["volume:missing"];
        var expectedMissingPath = Path.GetFullPath(Path.Combine(contentRoot, "Data/missing"));
        Assert.Equal("unavailable", missing.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatus]);
        Assert.Equal(
            $"Local volume path '{expectedMissingPath}' does not exist yet. Start or restart a consumer to let the runtime materialize it, or create the directory before attaching the volume.",
            missing.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatusReason]);

        var escape = resources["volume:escape"];
        Assert.Equal("unavailable", escape.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatus]);
        Assert.Equal(
            "Volume resource 'volume:escape' has subpath '../escape' outside storage resource 'storage:local'.",
            escape.ResourceAttributes[ResourceAttributeNames.StorageRuntimeStatusReason]);
    }

    [Fact]
    public void PlatformResources_DeclareDnsZoneWithNameMapping()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddDnsZone("local", zoneName: "local").WithDisplayName("Local DNS")
                    .MapHost("api.local", api, "http");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>()
            .GetDeclarations()
            .ToDictionary(declaration => declaration.ResourceId, StringComparer.OrdinalIgnoreCase);
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var declaredZone = Assert.Single(options.DeclaredDnsZones).Definition;
        var declaredMapping = Assert.Single(declaredZone.DnsNameMappings);
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        Assert.Equal("dns:local", declaredZone.Id);
        Assert.Equal("local", declaredZone.ZoneName);
        Assert.Equal("api.local", declaredMapping.HostName);
        Assert.Equal("application:api", declaredMapping.TargetResourceId);
        Assert.Equal("http", declaredMapping.TargetEndpointName);
        Assert.Equal(["application:api"], declarations["dns:local"].DependsOn);

        var zoneResource = resources["dns:local"];
        Assert.Equal(PlatformResourceProvider.DnsZoneResourceType, zoneResource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, zoneResource.ResourceClass);
        Assert.Null(zoneResource.State);
        Assert.Equal("local", zoneResource.ResourceAttributes[ResourceAttributeNames.DnsZoneName]);
        Assert.Equal("logical", zoneResource.ResourceAttributes[ResourceAttributeNames.DnsProvider]);
        Assert.Equal("1", zoneResource.ResourceAttributes[ResourceAttributeNames.DnsRecordCount]);
        Assert.True(zoneResource.HasCapability(ResourceCapabilityIds.NetworkingDnsZone));
        Assert.Empty(zoneResource.ResourceActions);

        var mappingResource = resources["dns:local:name:api-local"];
        Assert.Equal(PlatformResourceProvider.NameMappingResourceType, mappingResource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Network, mappingResource.ResourceClass);
        Assert.Equal("dns:local", mappingResource.ParentResourceId);
        Assert.Equal(["application:api"], mappingResource.DependsOn);
        Assert.Null(mappingResource.State);
        Assert.Equal("api.local", mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingHostName]);
        Assert.Equal("application:api", mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Equal("http", mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingTargetEndpointName]);
        Assert.Equal(ResourceExposureScope.Public.ToString(), mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingExposure]);
        Assert.Equal("Ready", mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingStatus]);
        Assert.Equal(
            "LogicalOnly",
            mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatus]);
        Assert.Equal(
            "No DNS publishing provider is selected. CloudShell models the name mapping, but it will not publish DNS records for this host.",
            mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatusReason]);
        Assert.True(mappingResource.HasCapability(ResourceCapabilityIds.NetworkingNameMapping));
    }

    [Fact]
    public void PlatformResources_ProjectDnsNameMappingMaterializationProvider()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddDnsZone("local", zoneName: "local").WithDisplayName("Local DNS")
                    .UseProvider("hosts-file")
                    .MapHost("api.local", api, "http");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        var zoneResource = resources["dns:local"];
        Assert.Equal("hosts-file", zoneResource.ResourceAttributes[ResourceAttributeNames.DnsProvider]);
        var action = Assert.Single(zoneResource.ResourceActions);
        Assert.Equal(PlatformResourceProvider.ReconcileNameMappingsActionId, action.Id);
        Assert.Equal("Reconcile name mappings", action.DisplayName);
        Assert.Equal(
            NetworkResourceOperationPermissions.ReconcileNameMappings,
            action.RequiredPermission);

        var mappingResource = resources["dns:local:name:api-local"];
        Assert.Equal(
            "ProviderSelected",
            mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatus]);
        Assert.Equal(
            "DNS provider 'hosts-file' is responsible for publishing this name.",
            mappingResource.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatusReason]);
    }

    [Fact]
    public void PlatformResources_DeclareDnsZoneWithLocalHostNameProvider()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");

                resources
                    .AddDnsZone("dev", zoneName: "cloudshell.local").WithDisplayName("Development DNS")
                    .UseLocalHostNames()
                    .MapHost("api.cloudshell.local", api, "http");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var declaredZone = Assert.Single(options.DeclaredDnsZones).Definition;

        Assert.Equal(LocalHostNamePublishingProvider.DefaultProviderName, declaredZone.Provider);
        Assert.Equal("api.cloudshell.local", Assert.Single(declaredZone.DnsNameMappings).HostName);
    }

    [Fact]
    public async Task PlatformProvider_ReconcilesDnsNameMappingsWithActivatedProvider()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: "hosts-file",
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var namePublisher = new TestNamePublishingProvider("hosts-file");
        var provider = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [namePublisher]);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResource("application:api", "http", "http://localhost:8080")
            ],
            [provider]);

        var result = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(
                zone,
                new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            zone.ResourceActions.Single());

        Assert.Equal("Reconciled name mappings.", result.Message);
        Assert.NotNull(namePublisher.Context);
        Assert.Equal("dns:local", namePublisher.Context.Definition.Id);
        Assert.Equal("dns:local", namePublisher.Context.DnsZoneResource.Id);
        Assert.Empty(namePublisher.Context.PublisherResources);
        var mapping = Assert.Single(namePublisher.Context.Mappings);
        Assert.Equal("dns:local:name:api-local", mapping.Mapping.Id);
        Assert.Equal("application:api", mapping.TargetResource.Id);
        Assert.Equal("http", mapping.TargetEndpoint?.Name);
        Assert.Null(mapping.PublisherResource);

        var projectedMapping = Assert.Single(
            provider.GetResources(),
            resource => resource.Id == "dns:local:name:api-local");
        Assert.Equal(
            "Published",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatus]);
        Assert.Contains(
            "Reconciled name mappings.",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatusReason],
            StringComparison.Ordinal);
        Assert.Contains(
            "provider 'hosts-file'",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatusReason],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_ProjectsDnsNameMappingPublishFailureObservation()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: "hosts-file",
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var namePublisher = new TestNamePublishingProvider(
            "hosts-file",
            exception: new InvalidOperationException("Could not update hosts file."));
        var provider = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [namePublisher]);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResource("application:api", "http", "http://localhost:8080")
            ],
            [provider]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ExecuteActionAsync(
                new ResourceProcedureContext(
                    zone,
                    new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                    null,
                    new TestResourceRegistrationStore([]),
                    resourceManager),
                zone.ResourceActions.Single()));

        Assert.Equal("Could not update hosts file.", exception.Message);
        var projectedMapping = Assert.Single(
            provider.GetResources(),
            resource => resource.Id == "dns:local:name:api-local");
        Assert.Equal(
            "PublishFailed",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatus]);
        Assert.Contains(
            "Could not update hosts file.",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingMaterializationStatusReason],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenDnsPublisherIsMissing()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: "hosts-file",
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResource("application:api", "http", "http://localhost:8080")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                zone,
                new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            zone.ResourceActions.Single());

        Assert.Equal(
            "No activated DNS publishing provider can reconcile name mappings for DNS zone resource 'dns:local'.",
            reason);
    }

    [Fact]
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenDnsNameTargetEndpointIsMissing()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: "hosts-file",
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "https")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [new TestNamePublishingProvider("hosts-file")]);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResource("application:api", "http", "http://localhost:8080")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                zone,
                new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            zone.ResourceActions.Single());

        Assert.Equal(
            "DNS zone resource 'dns:local' name mapping 'dns:local:name:api-local' target endpoint 'https' could not be found on resource 'application:api'.",
            reason);
    }

    [Fact]
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenLocalHostNameMappingUsesWildcard()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: LocalHostNamePublishingProvider.DefaultProviderName,
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:wildcard",
                    "*.local",
                    "*.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [new LocalHostNamePublishingProvider(options)]);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResource("application:api", "http", "http://localhost:8080")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                zone,
                new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            zone.ResourceActions.Single());

        Assert.Equal(
            "Name mapping 'dns:local:name:wildcard' uses wildcard host '*.local', but provider 'local-hostnames' only supports exact host mappings.",
            reason);
    }

    [Fact]
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenLocalHostNameTargetHostIsNotPublishable()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Provider: LocalHostNamePublishingProvider.DefaultProviderName,
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [new LocalHostNamePublishingProvider(options)]);
        var zone = Assert.Single(provider.GetResources(), resource => resource.Id == "dns:local");
        var resourceManager = new StaticResourceManagerStore(
            [
                zone,
                CreateEndpointResourceWithNetworkMapping("application:api", "http", "http://api.internal:8080")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                zone,
                new ResourceRegistration(zone.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            zone.ResourceActions.Single());

        Assert.Equal(
            "Name mapping 'dns:local:name:api-local' target endpoint 'http' host 'api.internal' is not a local or IP address that can be published through provider 'local-hostnames'.",
            reason);
    }

    [Fact]
    public async Task PlatformProvider_DeletesDnsNameMappingFromParentZone()
    {
        var options = new PlatformResourceOptions();
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var registrations = new MutableResourceRegistrationStore();

        await provider.SetupDnsZoneAsync(
            new DnsZoneResourceDefinition(
                "dns:local",
                "Local DNS",
                "local",
                Mappings:
                [
                    new DnsNameMappingDefinition(
                        "dns:local:name:api-local",
                        "api.local",
                        "api.local",
                        "application:api",
                        "http"),
                    new DnsNameMappingDefinition(
                        "dns:local:name:app-local",
                        "app.local",
                        "app.local",
                        "application:web",
                        "http")
                ]),
            null,
            registrations);
        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(registrations.GetRegistration("dns:local:name:api-local"));
        Assert.Equal(
            ["application:api", "application:web"],
            registrations.GetRegistration("dns:local")?.DependsOn.Order(StringComparer.OrdinalIgnoreCase));

        var result = await provider.DeleteAsync(
            new ResourceProcedureContext(
                resources["dns:local:name:api-local"],
                registrations.GetRegistration("dns:local:name:api-local"),
                null,
                registrations,
                new StaticResourceManagerStore(resources.Values.ToArray(), [provider])));

        Assert.Equal("Name mapping 'api.local' removed from DNS zone 'Local DNS'.", result.Message);
        Assert.Null(registrations.GetRegistration("dns:local:name:api-local"));
        Assert.Equal(
            ["application:web"],
            registrations.GetRegistration("dns:local")?.DependsOn);

        var updatedResources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        Assert.False(updatedResources.ContainsKey("dns:local:name:api-local"));
        Assert.True(updatedResources.ContainsKey("dns:local:name:app-local"));
        Assert.Equal("1", updatedResources["dns:local"].ResourceAttributes[ResourceAttributeNames.DnsRecordCount]);
    }

    [Fact]
    public async Task PlatformProvider_UpdatesDnsNameMappingWithoutMovingParentZoneGroup()
    {
        var options = new PlatformResourceOptions();
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var registrations = new MutableResourceRegistrationStore();

        await provider.SetupDnsZoneAsync(
            new DnsZoneResourceDefinition(
                "dns:local",
                "Local DNS",
                "local",
                Mappings:
                [
                    new DnsNameMappingDefinition(
                        "dns:local:name:api-local",
                        "api.local",
                        "api.local",
                        "application:api",
                        "http")
                ]),
            "group:network",
            registrations);

        await provider.SetupNameMappingAsync(
            new DnsNameMappingResourceDefinition(
                "dns:local",
                "dns:local:name:api-local",
                "Web local",
                "web.local",
                "application:web",
                "https",
                ResourceExposureScope.Private),
            "group:other",
            registrations);

        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var mapping = resources["dns:local:name:api-local"];

        Assert.Equal("group:network", registrations.GetRegistration("dns:local")?.ResourceGroupId);
        Assert.Equal("group:network", registrations.GetRegistration("dns:local:name:api-local")?.ResourceGroupId);
        Assert.Equal(["application:web"], registrations.GetRegistration("dns:local")?.DependsOn);
        Assert.Equal("api-local", mapping.Name);
        Assert.Equal("Web local", mapping.DisplayName);
        Assert.Equal("web.local", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingHostName]);
        Assert.Equal("application:web", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingTargetResourceId]);
        Assert.Equal("https", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingTargetEndpointName]);
        Assert.Equal(ResourceExposureScope.Private.ToString(), mapping.ResourceAttributes[ResourceAttributeNames.NameMappingExposure]);
    }

    [Fact]
    public async Task PlatformProvider_RejectsAuthoredDnsNameMappingDuplicateHostAndExposure()
    {
        var options = new PlatformResourceOptions();
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var registrations = new MutableResourceRegistrationStore();

        await provider.SetupDnsZoneAsync(
            new DnsZoneResourceDefinition(
                "dns:local",
                "Local DNS",
                "local",
                Mappings:
                [
                    new DnsNameMappingDefinition(
                        "dns:local:name:api-local",
                        "api.local",
                        "api.local",
                        "application:api",
                        "http")
                ]),
            "group:network",
            registrations);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupNameMappingAsync(
                new DnsNameMappingResourceDefinition(
                    "dns:local",
                    "dns:local:name:api-local-copy",
                    "API local copy",
                    "API.LOCAL",
                    "application:web",
                    "http",
                    ResourceExposureScope.Public),
                "group:network",
                registrations));

        Assert.Equal(
            "DNS zone resource 'dns:local' already has a name mapping for host 'API.LOCAL' in exposure scope 'Public': dns:local:name:api-local.",
            exception.Message);
        Assert.Null(registrations.GetRegistration("dns:local:name:api-local-copy"));
        var resources = provider.GetResources()
            .Where(resource => resource.EffectiveTypeId == PlatformResourceProvider.NameMappingResourceType)
            .ToArray();
        Assert.Single(resources);
    }

    [Fact]
    public async Task PlatformProvider_AppliesDnsZoneDeclarationWithNameMappingRegistrations()
    {
        var definition = new DnsZoneResourceDefinition(
            "dns:local",
            "Local DNS",
            "local",
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:local:name:api-local",
                    "api.local",
                    "api.local",
                    "application:api",
                    "http")
            ]);
        var options = new PlatformResourceOptions();
        options.DeclaredDnsZones.Add(new DeclaredDnsZoneResource(definition));
        var store = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(store, options);
        var registrations = new MutableResourceRegistrationStore();

        await provider.ApplyDeclarationAsync(
            new ResourceDeclaration(
                PlatformResourceProvider.ProviderId,
                "dns:local",
                null,
                "group:app",
                DateTimeOffset.UtcNow,
                ["application:frontend"],
                ResourceDeclarationPersistence.Transient),
            registrations);

        var zoneRegistration = registrations.GetRegistration("dns:local");
        var mappingRegistration = registrations.GetRegistration("dns:local:name:api-local");
        Assert.NotNull(zoneRegistration);
        Assert.NotNull(mappingRegistration);
        Assert.Equal("group:app", zoneRegistration.ResourceGroupId);
        Assert.Equal("group:app", mappingRegistration.ResourceGroupId);
        Assert.Equal(
            ["application:api", "application:frontend"],
            zoneRegistration.DependsOn.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(["application:api"], mappingRegistration.DependsOn);
    }

    [Fact]
    public void PlatformResources_ProjectDnsNameMappingConflicts()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                var api = resources.Declare("applications", "application:api");
                var frontend = resources.Declare("applications", "application:frontend");

                resources
                    .AddDnsZone("local", zoneName: "local").WithDisplayName("Local DNS")
                    .MapHost("app.local", api, "http", id: "dns:local:name:app-api")
                    .MapHost("app.local", frontend, "http", id: "dns:local:name:app-frontend");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var platformStore = new PlatformResourceStore(
            options,
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        var provider = new PlatformResourceProvider(platformStore, options);
        var resources = provider.GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);

        var zone = resources["dns:local"];
        Assert.Equal("2", zone.ResourceAttributes[ResourceAttributeNames.DnsConflictCount]);

        var mappings = resources.Values
            .Where(resource => string.Equals(
                resource.EffectiveTypeId,
                PlatformResourceProvider.NameMappingResourceType,
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(2, mappings.Length);
        Assert.All(mappings, mapping =>
        {
            Assert.Equal("Conflict", mapping.ResourceAttributes[ResourceAttributeNames.NameMappingStatus]);
            Assert.Equal(
                "Host name 'app.local' is used by multiple Public mappings in DNS zone 'local'.",
                mapping.ResourceAttributes[ResourceAttributeNames.NameMappingStatusReason]);
        });
    }

    [Fact]
    public void ApplicationVolumeDisplay_TreatsOnlyVolumeResourcesAsMountableCandidates()
    {
        var storage = new Resource(
            "storage:local",
            "Local Storage",
            StorageProviderNames.LocalStorage,
            StorageProviderNames.LocalStorage,
            "local",
            ResourceState.Running,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem
            },
            Capabilities: [new(ResourceCapabilityIds.StorageMountProvider)]);
        var volume = new Resource(
            "volume:postgres-data",
            "Postgres Data",
            "Volume",
            StorageProviderNames.LocalStorage,
            "local",
            ResourceState.Running,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            ["storage:local"],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);

        Assert.False(ApplicationVolumeResourceDisplay.IsMountableVolumeResource(storage));
        Assert.True(ApplicationVolumeResourceDisplay.IsMountableVolumeResource(volume));
        Assert.Equal("Postgres Data (FileSystem)", ApplicationVolumeResourceDisplay.GetVolumeOptionLabel(volume));

        var mount = new ResourceVolumeMount("volume:postgres-data", "/var/lib/postgresql/data", ReadOnly: true);
        Assert.Equal(
            "Postgres Data (FileSystem)",
            ApplicationVolumeResourceDisplay.GetMountSourceLabel(mount, volume));
        Assert.Equal(
            "Postgres Data (FileSystem) -> /var/lib/postgresql/data",
            ApplicationVolumeResourceDisplay.GetMountSummary(mount, volume));
        Assert.Equal("Read-only", ApplicationVolumeResourceDisplay.GetMountAccessLabel(mount));
        Assert.Equal(
            "docker-volume",
            ApplicationVolumeResourceDisplay.GetMountSourceLabel(
                new ResourceVolumeMount("docker-volume", "/data"),
                null));
    }

    [Fact]
    public void ApplicationVolumeMountReplicaWarning_ShowsWhenReplicasHaveAssignedMounts()
    {
        var readWriteOnce = CreateVolumeResource("volume:single-writer", VolumeAccessMode.ReadWriteOnce);
        var readOnlyMany = CreateVolumeResource("volume:read-many", VolumeAccessMode.ReadOnlyMany);
        var readWriteMany = CreateVolumeResource("volume:write-many", VolumeAccessMode.ReadWriteMany);

        Assert.False(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ApplicationVolumeMountInput("volume:single-writer", string.Empty)],
            [readWriteOnce]));
        Assert.False(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: false,
            [new ApplicationVolumeMountInput("volume:single-writer", "/data")],
            [readWriteOnce]));
        Assert.True(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ApplicationVolumeMountInput("volume:single-writer", "/data")],
            [readWriteOnce]));
        Assert.False(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ApplicationVolumeMountInput("volume:read-many", "/data", readOnly: true)],
            [readOnlyMany]));
        Assert.True(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ResourceVolumeMount("volume:read-many", "/data")],
            [readOnlyMany]));
        Assert.False(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ResourceVolumeMount("volume:write-many", "/data")],
            [readWriteMany]));
        Assert.False(ApplicationVolumeMountReplicaWarning.ShouldShow(
            replicasEnabled: true,
            [new ResourceVolumeMount("volume:unknown", "/data")],
            [readWriteOnce]));
    }

    private static Resource CreateVolumeResource(string id, VolumeAccessMode accessMode) =>
        new(
            id,
            id,
            "Volume",
            "CloudShell",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeAccessMode] = accessMode.ToString(),
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);

    [Fact]
    public void ResourceNameMappingDisplay_TreatsNameMappingsAsInboundApplicationExposure()
    {
        var mapping = new Resource(
            "dns:local:name:api-local",
            "api.local",
            "Name mapping",
            "cloudshell.platform",
            "local",
            ResourceState.Running,
            [],
            "api.local",
            DateTimeOffset.UtcNow,
            ["application:api"],
            TypeId: PlatformResourceProvider.NameMappingResourceType,
            ResourceClass: ResourceClass.Network,
            ParentResourceId: "dns:local",
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.NameMappingHostName] = "api.local",
                [ResourceAttributeNames.NameMappingTargetResourceId] = "application:api",
                [ResourceAttributeNames.NameMappingTargetEndpointName] = "http",
                [ResourceAttributeNames.NameMappingExposure] = ResourceExposureScope.Public.ToString(),
                [ResourceAttributeNames.NameMappingMaterializationStatus] = "ProviderSelected",
                [ResourceAttributeNames.DnsProvider] = "logical"
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);

        Assert.True(ResourceNameMappingDisplay.IsNameMappingResource(mapping));
        Assert.True(ResourceNameMappingDisplay.TargetsResource(mapping, "application:api"));
        Assert.False(ResourceNameMappingDisplay.TargetsResource(mapping, "application:worker"));
        Assert.Equal("api.local", ResourceNameMappingDisplay.GetHostName(mapping));
        Assert.Equal("http", ResourceNameMappingDisplay.GetTargetEndpointName(mapping));
        Assert.Equal(ResourceExposureScope.Public.ToString(), ResourceNameMappingDisplay.GetExposureLabel(mapping));
        Assert.Equal("logical", ResourceNameMappingDisplay.GetProviderLabel(mapping));
        Assert.Equal("provider selected", ResourceNameMappingDisplay.GetMaterializationLabel(mapping));
        Assert.Equal("api.local -> API/http", ResourceNameMappingDisplay.GetSummary(mapping, "API"));
    }

    [Fact]
    public void LocalContainerVolumeArguments_PreserveUnmanagedVolumeReference()
    {
        var arguments = ApplicationResourceService.CreateLocalContainerVolumeArguments(
            [new ResourceVolumeMount("docker-sql-data", "/var/opt/mssql", true)],
            null,
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));

        Assert.Equal(["docker-sql-data:/var/opt/mssql:ro"], arguments);
    }

    [Fact]
    public void LocalContainerVolumeArguments_ResolveLocalStorageOwnedVolume()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = new Resource(
            "storage:local",
            "Local Storage",
            StorageProviderNames.LocalStorage,
            StorageProviderNames.LocalStorage,
            "local",
            ResourceState.Running,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.StorageLocation] = "./Data/storage"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageMountProvider)]);
        var volume = new Resource(
            "volume:sql-data",
            "SQL Data",
            "Volume",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            StorageProviderNames.LocalStorage,
            DateTimeOffset.UtcNow,
            ["storage:local"],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.VolumeStorageResourceId] = "storage:local",
                [ResourceAttributeNames.VolumeSubPath] = "sql-server"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([storage, volume]);

        var arguments = ApplicationResourceService.CreateLocalContainerVolumeArguments(
            [new ResourceVolumeMount("volume:sql-data", "/var/opt/mssql", false, "data")],
            resourceManager,
            contentRoot);

        var expectedPath = Path.GetFullPath(Path.Combine(contentRoot, "Data/storage/sql-server"));
        Assert.Equal([$"{expectedPath}:/var/opt/mssql"], arguments);
        Assert.True(Directory.Exists(expectedPath));
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsUnsupportedVolumeMediumReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                resourceType: ApplicationResourceTypes.ContainerApp,
                volumeMounts:
                [
                    new ResourceVolumeMount("volume:data", "/data")
                ]),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var volume = new Resource(
            "volume:data",
            "Data",
            "Volume",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "NFS",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = "NFS"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([resource, volume]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Volume resource 'volume:data' uses storage medium 'NFS', which cannot be mounted by the current container materializer.",
            reason);
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsContainerHostStorageCapabilityReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:limited",
                "Limited Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Capabilities:
                [
                    ContainerHostCapabilityIds.ContainerImage,
                    ContainerHostCapabilityIds.ContainerBuild
                ]))
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                containerHostId: "docker:limited",
                resourceType: ApplicationResourceTypes.ContainerApp,
                volumeMounts:
                [
                    new ResourceVolumeMount("volume:data", "/data")
                ]),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var volume = new Resource(
            "volume:data",
            "Data",
            "Volume",
            "Test",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([resource, volume]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Container host 'docker:limited' does not advertise required storage capability 'storage.mount.filesystem' for volume resource 'volume:data'.",
            reason);
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsContainerHostImageCapabilityReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:build-only",
                "Build-only Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Capabilities: [ContainerHostCapabilityIds.ContainerBuild]))
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                containerHostId: "docker:build-only",
                resourceType: ApplicationResourceTypes.ContainerApp),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var resourceManager = new StaticResourceManagerStore([resource]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Container host 'docker:build-only' does not advertise required capability 'container.image'.",
            reason);
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsContainerHostBuildCapabilityReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var projectPath = Path.Combine("src", "API", "API.csproj");
        var resolvedProjectPath = Path.Combine(contentRoot, projectPath);

        Directory.CreateDirectory(Path.GetDirectoryName(resolvedProjectPath)!);
        await File.WriteAllTextAsync(resolvedProjectPath, "<Project />");
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:run-only",
                "Run-only Docker",
                ContainerHostKind.Docker,
                "unix:///var/run/docker.sock",
                Capabilities: [ContainerHostCapabilityIds.ContainerImage]))
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                projectPath: projectPath,
                projectContainerBuild: true,
                containerHostId: "docker:run-only",
                resourceType: ApplicationResourceTypes.ContainerApp),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var resourceManager = new StaticResourceManagerStore([resource]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Container host 'docker:run-only' does not advertise required capability 'container.build'.",
            reason);
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsContainerHostCredentialReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .UseContainerHost(new ContainerHostDescriptor(
                "docker:remote",
                "Remote Docker",
                ContainerHostKind.Docker,
                "tcp://docker.example.test:2376",
                CredentialsAvailable: false,
                Capabilities: [ContainerHostCapabilityIds.ContainerImage]))
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                containerHostId: "docker:remote",
                resourceType: ApplicationResourceTypes.ContainerApp),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var resourceManager = new StaticResourceManagerStore([resource]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Container host 'docker:remote' credentials are unavailable.",
            reason);
    }

    [Fact]
    public async Task ApplicationActionAvailability_ReturnsUnavailableContainerHostResourceReason()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var host = new Resource(
            "docker:remote",
            "Remote Docker",
            "Container host",
            "Test",
            "local",
            ResourceState.Stopped,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.ContainerHost)]);
        var descriptor = CreateDescriptor(
            host.Id,
            ContainerHostResourceTypes.ContainerHost,
            new ContainerHostDescriptor(
                host.Id,
                "Remote Docker",
                ContainerHostKind.Docker,
                "tcp://docker.example.test:2376",
                Capabilities: [ContainerHostCapabilityIds.ContainerImage]));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services.AddSingleton<IResourceOrchestrationDescriptorProvider>(new TestDescriptorProvider(descriptor));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var resourceProvider = serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                containerHostId: host.Id,
                resourceType: ApplicationResourceTypes.ContainerApp),
            resourceGroupId: null,
            registrations);
        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);
        var resourceManager = new StaticResourceManagerStore([resource, host], [resourceProvider]);

        var reason = await resourceProvider.GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                resource,
                registrations.GetRegistration(resource.Id),
                null,
                registrations,
                resourceManager),
            resource.ResourceActions.Single(action => action.Kind == ResourceActionKind.Start));

        Assert.Equal(
            "Container host 'docker:remote' is unavailable.",
            reason);
    }

    [Fact]
    public async Task ApplicationProvider_ProjectsVolumeMountMaterializationStatus()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider();

        using var serviceProvider = services.BuildServiceProvider();
        var provider = serviceProvider.GetRequiredService<ApplicationResourceService>();
        var runtimeStates = serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>();
        var registrations = new MutableResourceRegistrationStore();
        await provider.SetupApplicationAsync(
            new ApplicationResourceDefinition(
                "application:api",
                "API",
                string.Empty,
                containerImage: "redis:7.2",
                resourceType: ApplicationResourceTypes.ContainerApp,
                volumeMounts:
                [
                    new ResourceVolumeMount("volume:data", "/data")
                ]),
            resourceGroupId: null,
            registrations);
        runtimeStates.Save(new ApplicationRuntimeState(
            "application:api",
            1234,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            LogPath: "api.log",
            VolumeMounts:
            [
                new ResourceVolumeMountMaterialization(
                    "volume:data",
                    "/data",
                    "/tmp/cloudshell/data",
                    ReadOnly: false)
            ]));

        var resource = Assert.Single(provider.GetResources(), resource => resource.IsNormalResource);

        Assert.Equal(
            "1",
            resource.ResourceAttributes[ResourceAttributeNames.VolumeMountMaterializedCount]);
        Assert.Equal(
            "materialized",
            resource.ResourceAttributes[ResourceAttributeNames.VolumeMountMaterializationStatus]);
    }

    [Fact]
    public void VolumeMountValidation_ReturnsUnsupportedStorageMediumReason()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = new Resource(
            "storage:remote",
            "Remote Storage",
            "Remote Storage",
            "Test",
            "remote",
            ResourceState.Running,
            [],
            "NFS",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageMedium] = "NFS"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageMountProvider)]);
        var volume = new Resource(
            "volume:data",
            "Data",
            "Volume",
            "Test",
            "remote",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            ["storage:remote"],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageResourceId] = "storage:remote",
                [ResourceAttributeNames.VolumeSubPath] = "data"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([storage, volume]);

        var reason = ApplicationResourceService.GetVolumeMountUnavailableReason(
            [new ResourceVolumeMount("volume:data", "/data")],
            resourceManager,
            contentRoot);

        Assert.Equal(
            "Storage resource 'storage:remote' uses storage medium 'NFS', which cannot be mounted by the current container materializer.",
            reason);
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
    public void AddIdentityProvider_DeclaresProvisioningResourceBoundary()
    {
        var services = new ServiceCollection();

        services
            .AddControlPlane()
            .Resources(resources =>
            {
                resources.AddIdentityProvider(
                    "identity:keycloak",
                    "Keycloak",
                    ResourceIdentityProviderKind.Oidc,
                    provisioningResourceId: "identity-provisioning:keycloak",
                    useAsDefault: true);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var provisioning = declarations.GetDeclaration("identity-provisioning:keycloak");

        Assert.NotNull(provisioning);
        Assert.Equal(ResourceIdentityProvisioningResources.ProviderId, provisioning.ProviderId);
        Assert.Equal(ResourceClass.Infrastructure, provisioning.ResourceClassOverride);
        Assert.Equal(
            "identity-provisioning",
            provisioning.ResourceAttributes[ResourceAttributeNames.InfrastructureKind]);
        Assert.Equal("Keycloak", provisioning.ResourceAttributes["identity.provider"]);
        Assert.Equal("identity:keycloak", declarations.DefaultIdentityProviderId);
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
                    .AddNetwork("network:app", isDefault: true).WithDisplayName("App Network");
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
                Assert.Equal("http", endpoint.Protocol);
            },
            endpoint =>
            {
                Assert.Equal("public", endpoint.Name);
                Assert.Equal("tcp", endpoint.Protocol);
            });
        Assert.Collection(
            resource.ResourceEndpointNetworkMappings.OrderBy(mapping => mapping.Name, StringComparer.OrdinalIgnoreCase),
            mapping =>
            {
                Assert.Equal("api", mapping.Name);
                Assert.StartsWith("http://localhost:", mapping.Address);
            },
            mapping =>
            {
                Assert.Equal("public", mapping.Name);
                Assert.Equal("tcp://localhost:4040", mapping.Address);
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
                    .UseContainerHost(dockerHost)
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
        Assert.Equal("public", definition.Name);
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
        Assert.Null(resource.State);
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
                Assert.Equal("http", endpoint.Protocol);
                Assert.Equal(80, endpoint.TargetPort);
            },
            endpoint =>
            {
                Assert.Equal("https", endpoint.Name);
                Assert.Equal("https", endpoint.Protocol);
                Assert.Equal(443, endpoint.TargetPort);
            });
        Assert.Collection(
            resource.ResourceEndpointNetworkMappings.OrderBy(mapping => mapping.Name, StringComparer.OrdinalIgnoreCase),
            mapping =>
            {
                Assert.Equal("http", mapping.Name);
                Assert.Equal("http://localhost:80", mapping.Address);
            },
            mapping =>
            {
                Assert.Equal("https", mapping.Name);
                Assert.Equal("https://localhost:443", mapping.Address);
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
                    .AddVirtualNetwork("network:app", isDefault: true).WithDisplayName("App Network");
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
        Assert.Equal("api", Assert.Single(resource.Endpoints).Name);
        Assert.StartsWith("http://localhost:", Assert.Single(resource.ResourceEndpointNetworkMappings).Address);
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
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("network", endpoint.Name);
        Assert.Equal("network", endpoint.Protocol);
    }

    [Fact]
    public void StaticResourceProviders_ProjectEndpointAddressesAsMappings()
    {
        var cloudShellResource = new CloudShellResourceProvider()
            .GetResources()
            .Single(resource => resource.Id == "api-gateway");
        var managedResource = new ManagedResourceProvider()
            .GetResources()
            .Single(resource => resource.Id == "postgres-main");

        var cloudShellEndpoint = Assert.Single(
            cloudShellResource.Endpoints,
            endpoint => endpoint.Name == "public");
        Assert.Equal("https", cloudShellEndpoint.Protocol);
        Assert.Equal("https://api.cloudshell.local", cloudShellResource.GetEndpointNetworkAddress("public"));

        var managedEndpoint = Assert.Single(managedResource.Endpoints);
        Assert.Equal("postgres", managedEndpoint.Name);
        Assert.Equal("postgres://main.internal", managedResource.GetEndpointNetworkAddress("postgres"));
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

        Assert.Equal("api", Assert.Single(resource.Endpoints).Name);
        Assert.Equal("http://loopback.test:4123", Assert.Single(resource.ResourceEndpointNetworkMappings).Address);
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
                    "networking:host-local")
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
                CreateNetworkingProviderResource("networking:host-local")
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
        Assert.Equal("networking:host-local", provisioner.Context.ProviderResource.Id);
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
                    "networking:host-local")
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
                CreateNetworkingProviderResource("networking:host-local")
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
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenVirtualEndpointMappingProviderIsNotActivated()
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
                    "networking:host-local")
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
                CreateNetworkingProviderResource("networking:host-local")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                network,
                new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            network.ResourceActions.Single());

        Assert.Equal(
            "Endpoint mapping 'mapping:api' requires provider resource 'networking:host-local', but no activated host networking service can materialize it.",
            reason);
    }

    [Fact]
    public async Task PlatformProvider_ReturnsUnavailableReasonWhenEndpointMappingTargetEndpointIsMissing()
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
                    "network:app")
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
                CreateEndpointResource("application:api", "admin", "http://localhost:8080")
            ],
            [provider]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(
                network,
                new ResourceRegistration(network.Id, PlatformResourceProvider.ProviderId, null, DateTimeOffset.UtcNow, []),
                null,
                new TestResourceRegistrationStore([]),
                resourceManager),
            network.ResourceActions.Single());

        Assert.Equal(
            "Endpoint mapping 'mapping:api' target endpoint 'http' could not be found on resource 'application:api'.",
            reason);
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
        Assert.True(application.ReplicasEnabled);
    }

    [Fact]
    public async Task ProjectResourceBuilder_AsContainerConvertsProjectToContainerBuildWorkload()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.ResourceIdentityTokenEndpoint = "http://localhost:5011/api/auth/token";
                options.ResourceIdentityDefaultScope = "ControlPlane.Access";
            })
            .Resources(resources =>
            {
                var identityProvider = resources.AddIdentityProvider(
                    "identity:development",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    new Dictionary<string, string>
                    {
                        ["clientSecret"] = "local-development-api-secret"
                    },
                    useAsDefault: true);

                resources
                    .AddAspNetCoreProject(
                        "application:api",
                        "src/API/API.csproj")
                    .WithIdentity(identityProvider, name: "api-service")
                    .AsContainer(registry: "registry.local:5000", replicas: 2)
                    .WithContainerHost("docker:dev");
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
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var liveDatabases = await provider.QuerySqlServerDatabasesAsync("application:sql");
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var environment = workload?.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(ApplicationResourceTypes.ContainerApp, application.ResourceType);
        Assert.True(application.ProjectContainerBuild);
        Assert.Equal("src/API/API.csproj", application.ProjectPath);
        Assert.Null(application.ContainerImage);
        Assert.Null(application.ContainerBuildContext);
        Assert.Null(application.ContainerDockerfile);
        Assert.Equal("docker:dev", application.ContainerHostId);
        Assert.Equal(ApplicationResourceTypes.ContainerApp, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Container, resource.ResourceClass);
        Assert.Equal(ResourceWorkloadKind.ContainerBuild.ToString(), resource.ResourceAttributes[ResourceAttributeNames.WorkloadKind]);
        Assert.Equal("src/API/API.csproj", resource.ResourceAttributes[ResourceAttributeNames.ProjectPath]);
        Assert.Equal("2", resource.ResourceAttributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("true", resource.ResourceAttributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal("registry.local:5000", resource.ResourceAttributes[ResourceAttributeNames.ContainerRegistry]);
        Assert.Equal("docker:dev", resource.ResourceAttributes[ResourceAttributeNames.ContainerHostId]);
        Assert.Equal(ResourceWorkloadKind.ContainerBuild, workload?.Kind);
        Assert.Equal("src/API/API.csproj", workload?.ProjectPath);
        Assert.Null(workload?.BuildContext);
        Assert.Null(workload?.Dockerfile);
        Assert.Equal("registry.local:5000", workload?.Registry);
        Assert.Equal("docker:dev", workload?.ContainerHostId);
        Assert.Equal(2, workload?.Replicas);
        Assert.True(workload?.ReplicasEnabled == true);
        Assert.Equal(
            "application:api/api-service",
            environment?[EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable]);
        Assert.Equal(
            "local-development-api-secret",
            environment?[EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable]);
    }

    [Theory]
    [InlineData(null, "cloudshell-application-api:rev-test")]
    [InlineData("registry.local:5000", "registry.local:5000/cloudshell-application-api:rev-test")]
    public void ProjectContainerImageReference_UsesLocalReferenceForDefaultRegistry(
        string? registry,
        string expectedReference)
    {
        var method = typeof(ApplicationResourceService).GetMethod(
            "CreateProjectContainerImageReference",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var application = new ApplicationResourceDefinition(
            "application:api",
            "API",
            executablePath: string.Empty,
            resourceType: ApplicationResourceTypes.ContainerApp,
            projectPath: "src/API/API.csproj",
            containerRegistry: registry,
            containerRevision: "rev-test",
            projectContainerBuild: true);

        Assert.NotNull(method);
        var imageReference = method.Invoke(null, [application]);
        var reference = imageReference
            ?.GetType()
            .GetProperty("Reference")
            ?.GetValue(imageReference);

        Assert.Equal(expectedReference, reference);
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
                var volume = resources.AddVolume("volume:sql-data").WithDisplayName("SQL Data");
                var container = resources
                    .AddContainer(
                        "sql",
                        "mcr.microsoft.com/mssql/server:2022-latest",
                        replicas: 3)
                    .WithImage("example/sql-server:dev")
                    .WithRegistry("https://registry.example.com")
                    .WithRegistryCredentialsFromEnvironment("registry-user", "REGISTRY_PASSWORD")
                    .WithEndpoint("tds", targetPort: 1433, port: 14333)
                    .WithVolume(volume, "/var/opt/mssql", name: "data")
                    .WithContainerHost("docker:dev")
                    .WithLifetime(ResourceLifetime.Detached);

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<IContainerResourceBuilder>>(container);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:sql");
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:sql");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(ApplicationResourceProviderIds.ContainerApplication, declaration.ProviderId);
        Assert.Null(declaration.ParentResourceId);
        Assert.Equal(["volume:sql-data"], declaration.DependsOn);
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
        Assert.True(workload?.ReplicasEnabled == true);
        Assert.Equal("3", resource.ResourceAttributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("true", resource.ResourceAttributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal("1", resource.ResourceAttributes[ResourceAttributeNames.VolumeMountCount]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.StorageVolumeConsumer));
        Assert.Equal("registry-user", provider.GetApplication("application:sql")?.ContainerRegistryCredentials?.Username);
        Assert.Equal(
            "REGISTRY_PASSWORD",
            provider.GetApplication("application:sql")?.ContainerRegistryCredentials?.PasswordEnvironmentVariable);
        Assert.Equal("docker:dev", workload?.ContainerHostId);
        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
        var mount = Assert.Single(workload?.WorkloadVolumeMounts ?? []);
        Assert.Equal("volume:sql-data", mount.VolumeReference);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);
        Assert.False(mount.ReadOnly);
        Assert.Equal("data", mount.Name);
        Assert.Equal(StorageVolumeResourceOperationPermissions.MountWrite, mount.RequiredPermission);
        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("tds", port.Name);
        Assert.Equal(1433, port.TargetPort);
        Assert.Equal(14333, port.Port);
        Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
        var endpoint = Assert.Single(resource.Endpoints);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.Equal("tcp://localhost:14333", mapping.Address);
    }

    [Fact]
    public async Task SqlServerBuilder_DeclaresServiceResourceWithStorageAndDatabaseGrant()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .ConfigureInMemoryIdentity(setup =>
            {
                setup.ProviderId = "identity:development";
                setup.ProviderName = "Development Identity";
                setup.UseAsDefaultProvider = true;
            })
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                var identityProvider = resources.GetIdentityProvider();
                var volume = resources.AddVolume("volume:sql-data").WithDisplayName("SQL Data");
                var api = resources
                    .AddExecutableApplication("application:api", "dotnet")
                    .WithIdentity(identityProvider, name: "api");
                var sql = resources
                    .AddSqlServer(
                        "sql",
                        administratorPassword: "CloudShell-Passw0rd!",
                        dataVolume: volume,
                        port: 14333)
                    .WithDatabase("appdb", "Application DB")
                    .WithIdentity(identityProvider)
                    .WithContainerHost("docker:dev")
                    .WithLifetime(ResourceLifetime.Detached);

                sql.Allow(api.Principal, DatabaseResourceOperationPermissions.ReadWrite);

                Assert.IsAssignableFrom<ILifetimeBoundResourceBuilder<ISqlServerResourceBuilder>>(sql);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var store = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var declaration = Assert.Single(store.GetDeclarations(), declaration =>
            declaration.ResourceId == "application:sql");
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:sql");
        var database = Assert.Single(provider.GetResources(), resource =>
            resource.ParentResourceId == "application:sql" &&
            resource.EffectiveTypeId == ApplicationResourceTypes.SqlDatabase);
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var liveDatabases = await provider.QuerySqlServerDatabasesAsync("application:sql");
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var environment = workload?.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value);
        var grant = Assert.Single(store.GetPermissionGrants(), grant =>
            string.Equals(grant.TargetResourceId, "application:sql", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ApplicationResourceProviderIds.SqlServer, declaration.ProviderId);
        Assert.Equal(["volume:sql-data"], declaration.DependsOn);
        Assert.Equal("identity:development", declaration.IdentityBinding?.ProviderId);
        Assert.Equal(ApplicationResourceTypes.SqlServer, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Service, resource.ResourceClass);
        Assert.Equal(
            ResourceWorkloadKind.ContainerImage.ToString(),
            resource.ResourceAttributes[ResourceAttributeNames.WorkloadKind]);
        Assert.Equal(
            ApplicationProviderServiceCollectionExtensions.DefaultSqlServerImage,
            resource.ResourceAttributes[ResourceAttributeNames.ContainerImage]);
        Assert.Equal("docker:dev", resource.ResourceAttributes[ResourceAttributeNames.ContainerHostId]);
        Assert.Equal("1", resource.ResourceAttributes[ResourceAttributeNames.VolumeMountCount]);
        Assert.Equal("1", resource.ResourceAttributes[ResourceAttributeNames.DatabaseCount]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.StorageVolumeConsumer));
        Assert.Empty(liveDatabases);
        var application = provider.GetApplication("application:sql");
        Assert.Equal(ApplicationLifetime.Detached, application?.Lifetime);
        var declaredDatabase = Assert.Single(application?.SqlDatabases ?? []);
        Assert.Equal("appdb", declaredDatabase.Name);
        Assert.Equal("Application DB", declaredDatabase.DisplayName);
        Assert.Equal(ResourceWorkloadKind.ContainerImage, workload?.Kind);
        Assert.Equal(ApplicationProviderServiceCollectionExtensions.DefaultSqlServerImage, workload?.Image);
        Assert.Equal("docker:dev", workload?.ContainerHostId);
        Assert.Equal(ResourceLifetime.Detached, workload?.Lifetime);
        Assert.Equal("Y", environment?["ACCEPT_EULA"]);
        Assert.Equal("CloudShell-Passw0rd!", environment?["MSSQL_SA_PASSWORD"]);
        var mount = Assert.Single(workload?.WorkloadVolumeMounts ?? []);
        Assert.Equal("volume:sql-data", mount.VolumeReference);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);
        Assert.False(mount.ReadOnly);
        Assert.Equal("data", mount.Name);
        Assert.Equal(StorageVolumeResourceOperationPermissions.MountWrite, mount.RequiredPermission);
        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("tds", port.Name);
        Assert.Equal(1433, port.TargetPort);
        Assert.Equal(14333, port.Port);
        Assert.Equal("tcp", port.Protocol);
        Assert.Equal(ResourceEndpointAssignment.Manual, port.Assignment);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.Equal("tcp://localhost:14333", mapping.Address);
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, grant.Principal.Kind);
        Assert.Equal("application:api", grant.Principal.SourceResourceId);
        Assert.Equal("api", grant.Principal.SourceIdentityName);
        Assert.Equal(DatabaseResourceOperationPermissions.ReadWrite, grant.Permission);

        Assert.Equal("application:sql/database:appdb", database.Id);
        Assert.Equal("appdb", database.Name);
        Assert.Equal("Application DB", database.DisplayName);
        Assert.Equal("SQL database", database.Kind);
        Assert.Equal(ResourceClass.Service, database.ResourceClass);
        Assert.Equal(ResourceSource.Provider, database.Source);
        Assert.Equal(ResourceManagementMode.ProviderManaged, database.ManagementMode);
        Assert.Equal(ResourceVisibility.Diagnostic, database.Visibility);
        Assert.Equal("application:sql", database.OwnerResourceId);
        Assert.Equal(ResourceCleanupBehavior.DeleteWithOwner, database.CleanupBehavior);
        Assert.Equal("appdb", database.ResourceAttributes[ResourceAttributeNames.DatabaseName]);
        Assert.Equal("application:sql", database.ResourceAttributes[ResourceAttributeNames.DatabaseServerResourceId]);
        Assert.Equal("declared", database.ResourceAttributes[ResourceAttributeNames.DatabaseSource]);
    }

    [Fact]
    public void ContainerApplicationProvider_ProjectsHiddenRuntimeReplicaResources()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources
                    .AddContainer(
                        "api",
                        "example/api:latest",
                        replicas: 3)
                    .WithContainerHost("docker:dev");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resources = provider.GetResources();
        var app = Assert.Single(resources, resource => resource.Id == "application:api");
        var replicas = resources
            .Where(resource => string.Equals(resource.ParentResourceId, app.Id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(resource => resource.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal])
            .ToArray();

        Assert.True(app.IsNormalResource);
        Assert.Equal("cloudshell-application-api-deployment", app.ResourceAttributes[ResourceAttributeNames.DeploymentId]);
        Assert.Equal("cloudshell-application-api", app.ResourceAttributes[ResourceAttributeNames.DeploymentServiceId]);
        Assert.Equal("pending", app.ResourceAttributes[ResourceAttributeNames.DeploymentStatus]);
        Assert.Equal("3", app.ResourceAttributes[ResourceAttributeNames.DeploymentDesiredReplicas]);
        Assert.Equal("3", app.ResourceAttributes[ResourceAttributeNames.DeploymentProjectedReplicas]);
        Assert.Equal("true", app.ResourceAttributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal(
            app.ResourceAttributes[ResourceAttributeNames.ContainerRevision],
            app.ResourceAttributes[ResourceAttributeNames.DeploymentRevision]);
        Assert.Equal(
            app.ResourceAttributes[ResourceAttributeNames.ContainerRevision],
            app.ResourceAttributes[ResourceAttributeNames.DeploymentWorkloadVersion]);
        Assert.Equal(3, replicas.Length);
        Assert.All(replicas, replica =>
        {
            Assert.Equal(ResourceSource.Orchestrator, replica.Source);
            Assert.Equal(ResourceManagementMode.RuntimeManaged, replica.ManagementMode);
            Assert.Equal(ResourceVisibility.Hidden, replica.Visibility);
            Assert.Equal(ResourceCleanupBehavior.DeleteWithOwner, replica.CleanupBehavior);
            Assert.Equal(app.Id, replica.OwnerResourceId);
            Assert.Equal(app.Id, replica.ParentResourceId);
            Assert.Equal("runtime.container", replica.EffectiveTypeId);
            Assert.Equal(ResourceClass.Container, replica.ResourceClass);
            Assert.True(replica.HasCapability(ResourceCapabilityIds.Monitoring));
            Assert.Equal("containerReplica", replica.ResourceAttributes[ResourceAttributeNames.RuntimeKind]);
            Assert.Equal("3", replica.ResourceAttributes[ResourceAttributeNames.RuntimeReplicaCount]);
            Assert.Equal(
                app.ResourceAttributes[ResourceAttributeNames.DeploymentId],
                replica.ResourceAttributes[ResourceAttributeNames.DeploymentId]);
            Assert.Equal(
                app.ResourceAttributes[ResourceAttributeNames.DeploymentServiceId],
                replica.ResourceAttributes[ResourceAttributeNames.DeploymentServiceId]);
            Assert.Equal(
                app.ResourceAttributes[ResourceAttributeNames.DeploymentRevision],
                replica.ResourceAttributes[ResourceAttributeNames.DeploymentRevision]);
            Assert.Equal(
                app.ResourceAttributes[ResourceAttributeNames.ContainerRevision],
                replica.ResourceAttributes[ResourceAttributeNames.RuntimeRevision]);
            Assert.Equal(
                "orchestratorProjection",
                replica.ResourceAttributes[ResourceAttributeNames.RuntimeMaterialization]);
            Assert.False(replica.IsNormalResource);
            Assert.True(replica.IsRuntimeManaged);
        });
        Assert.Equal("1", replicas[0].ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
        Assert.Equal("2", replicas[1].ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
        Assert.Equal("3", replicas[2].ResourceAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
    }

    [Fact]
    public void ContainerApplicationProvider_DoesNotProjectReplicasForSingleInstanceApps()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources
                    .AddContainer("api", "example/api:latest")
                    .WithContainerHost("docker:dev");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resources = provider.GetResources();
        var app = Assert.Single(resources, resource => resource.Id == "application:api");

        Assert.Equal("1", app.ResourceAttributes[ResourceAttributeNames.ContainerReplicas]);
        Assert.Equal("false", app.ResourceAttributes[ResourceAttributeNames.ContainerReplicasEnabled]);
        Assert.Equal("1", app.ResourceAttributes[ResourceAttributeNames.DeploymentDesiredReplicas]);
        Assert.Equal("0", app.ResourceAttributes[ResourceAttributeNames.DeploymentProjectedReplicas]);
        Assert.DoesNotContain(
            resources,
            resource => string.Equals(resource.ParentResourceId, app.Id, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContainerApplicationBuilder_CanUseAddresslessEndpointContracts()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources.AddContainer(
                    "cache",
                    "redis:7",
                    endpoints:
                    [
                        ResourceEndpoint.Contract(
                            "redis",
                            "tcp",
                            ResourceExposureScope.Private,
                            6379)
                    ]);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:cache");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("redis", port.Name);
        Assert.Equal(6379, port.TargetPort);
        Assert.Null(port.Port);
        Assert.Equal("tcp", port.Protocol);
        Assert.Equal(ResourceExposureScope.Private, port.Exposure);
        Assert.Equal(ResourceEndpointAssignment.ProviderDefault, port.Assignment);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_IgnoresAddressBearingEndpointAddresses()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .Resources(resources =>
            {
                resources.AddContainer(
                    "registry",
                    "registry:2",
                    endpoints:
                    [
                        ResourceEndpoint.Http(
                            "http",
                            "127.0.0.1",
                            5055,
                            ResourceExposureScope.Public,
                            5000)
                    ]);
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var application = provider.GetApplication("application:registry");
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:registry");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(application);
        Assert.Null(application.Endpoint);
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5000, endpoint.TargetPort);
        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.StartsWith("http://localhost:", mapping.Address, StringComparison.Ordinal);
        Assert.True(ResourceEndpoint.TryGetPort(mapping.Address, out var mappedPort));

        var port = Assert.Single(workload?.WorkloadPorts ?? []);
        Assert.Equal("http", port.Name);
        Assert.Equal(5000, port.TargetPort);
        Assert.Null(port.Port);
        Assert.Equal("http", port.Protocol);
        Assert.Equal(ResourceExposureScope.Public, port.Exposure);
        Assert.Equal(ResourceEndpointAssignment.ProviderDefault, port.Assignment);
        Assert.Null(port.Host);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_DescribesResourceIdentityCredentialEnvironment()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.ResourceIdentityTokenEndpoint = "http://localhost:5011/api/auth/token";
                options.ResourceIdentityDefaultScope = "ControlPlane.Access";
            })
            .Resources(resources =>
            {
                var identityProvider = resources.AddIdentityProvider(
                    "identity:development",
                    "Development identity",
                    ResourceIdentityProviderKind.BuiltIn,
                    new Dictionary<string, string>
                    {
                        ["clientSecret"] = "local-development-api-secret"
                    },
                    useAsDefault: true);

                resources
                    .AddContainer("api", "example/api:dev")
                    .WithIdentity(identityProvider, name: "api-service");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var environment = workload?.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(
            "http://localhost:5011/api/auth/token",
            environment?[EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable]);
        Assert.Equal(
            "application:api/api-service",
            environment?[EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable]);
        Assert.Equal(
            "local-development-api-secret",
            environment?[EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable]);
        Assert.Equal(
            "ControlPlane.Access",
            environment?[EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable]);
        Assert.DoesNotContain(
            environment ?? [],
            variable => variable.Key.StartsWith("CLOUDSHELL_SECRETS_", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ContainerApplicationBuilder_UsesExternalResourceIdentityCredentialEnvironmentProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services.AddSingleton<IResourceIdentityCredentialEnvironmentProvider>(
            new TestResourceIdentityCredentialEnvironmentProvider());
        services
            .AddControlPlane()
            .AddApplicationProvider(options =>
            {
                options.ResourceIdentityDefaultScope = "ControlPlane.Access";
            })
            .Resources(resources =>
            {
                var identityProvider = resources.AddIdentityProvider(
                    "identity:external",
                    "External identity",
                    ResourceIdentityProviderKind.Oidc,
                    useAsDefault: true);

                resources
                    .AddContainer("api", "example/api:dev")
                    .WithIdentity(identityProvider, name: "api-service");
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var environment = workload?.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(
            "https://identity.example.test/token",
            environment?[EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable]);
        Assert.Equal(
            "external-api-service",
            environment?[EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable]);
        Assert.Equal(
            "external-api-secret",
            environment?[EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable]);
        Assert.Equal(
            "ControlPlane.Access",
            environment?[EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable]);
    }

    [Fact]
    public async Task ContainerApplicationBuilder_DescribesServiceDiscoveryEnvironment()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IHostEnvironment>(
            new TestHostEnvironment(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        services
            .AddControlPlane()
            .AddApplicationProvider()
            .Resources(resources =>
            {
                var catalog = resources.Declare("service", "service:catalog");

                resources
                    .AddContainer("api", "example/api:dev")
                    .WithReference(catalog)
                    .WithServiceDiscovery();
            });

        using var serviceProvider = services.BuildServiceProvider();
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var catalogResource = new Resource(
            "service:catalog",
            "Catalog API",
            "Service",
            "Test",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Network, 8080)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings:
            [
                new ResourceEndpointNetworkMapping(
                    "service:catalog:endpoint-network-mapping:http",
                    "http",
                    new ResourceEndpointReference("service:catalog", "http"),
                    "http://catalog.local:8080",
                    ResourceExposureScope.Network,
                    NetworkResourceId: "network:internal",
                    SourceEndpointName: "http")
            ]);
        var descriptor = await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(
                null,
                null,
                new StaticResourceManagerStore([catalogResource])));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var environment = workload?.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value);

        Assert.Equal(
            "http://catalog.local:8080",
            environment?["services__catalog-api__http__0"]);
        Assert.Equal(
            "http://catalog.local:8080",
            environment?["services__service-catalog__http__0"]);
        Assert.Equal(
            ["catalog-api", "service-catalog"],
            ApplicationServiceDiscoveryDisplay.GetServiceNames(catalogResource));
        Assert.Contains(
            ApplicationServiceDiscoveryDisplay.GetEndpointBindings(catalogResource),
            binding =>
                binding.EnvironmentVariableName == "services__service-catalog__http__0" &&
                binding.Address == "http://catalog.local:8080");
        var legacyCatalogResource = catalogResource with
        {
            Endpoints = [ResourceEndpoint.Http("http", "legacy-catalog.local", 8080, ResourceExposureScope.Network)],
            EndpointNetworkMappings = []
        };
        Assert.Empty(ApplicationServiceDiscoveryDisplay.GetEndpointBindings(legacyCatalogResource));
        Assert.DoesNotContain(
            environment ?? [],
            variable => variable.Key.StartsWith("CLOUDSHELL_SECRETS_", StringComparison.OrdinalIgnoreCase));
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
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
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
            ],
            VolumeMounts:
            [
                new ResourceVolumeMount("volume:data", "/data", ReadOnly: true)
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
        Assert.Equal(workload.WorkloadVolumeMounts, service.ServiceVolumeMounts);
        Assert.Equal(
            StorageVolumeResourceOperationPermissions.MountRead,
            service.ServiceVolumeMounts.Single().RequiredPermission);
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
                    new EnvironmentVariableAssignment("ASPNETCORE_ENVIRONMENT", "Development"),
                    new EnvironmentVariableAssignment(
                        "services__catalog-api__http__0",
                        "http://catalog.local:8080")
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
                new[] { new NetworkResourceDefinition("network:app", "App Network") },
                null
            ]));

        Assert.Contains("name: \"sample\"", yaml);
        Assert.Contains("  api:", yaml);
        Assert.Contains("    image: \"ghcr.io/example/api:dev\"", yaml);
        Assert.Contains("    environment:", yaml);
        Assert.Contains("      ASPNETCORE_ENVIRONMENT: \"Development\"", yaml);
        Assert.Contains("      services__catalog-api__http__0: \"http://catalog.local:8080\"", yaml);
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
    public void DockerComposeOrchestrator_RendersLocalStorageBackedVolumeMount()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var orchestrator = new DockerComposeResourceOrchestrator(
            new DockerComposeOrchestratorOptions
            {
                ProjectName = "sample",
                WorkingDirectory = workingDirectory
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
        var storage = new Resource(
            "storage:local",
            "Local Storage",
            StorageProviderNames.LocalStorage,
            StorageProviderNames.LocalStorage,
            "local",
            ResourceState.Running,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.storage",
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.StorageLocation] = "./Data/storage"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageMountProvider)]);
        var volume = new Resource(
            "volume:sql-data",
            "SQL Data",
            "Volume",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            StorageProviderNames.LocalStorage,
            DateTimeOffset.UtcNow,
            ["storage:local"],
            TypeId: "cloudshell.volume",
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.VolumeStorageResourceId] = "storage:local",
                [ResourceAttributeNames.VolumeSubPath] = "sql-server"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([storage, volume]);
        var service = new ResourceOrchestratorService(
            "application:sql",
            "sql",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "SQL Server",
                Image: "mcr.microsoft.com/mssql/server:2022-latest",
                VolumeMounts:
                [
                    new ResourceVolumeMount("volume:sql-data", "/var/opt/mssql", false, "data")
                ]));

        Assert.NotNull(render);
        var yaml = Assert.IsType<string>(render.Invoke(
            orchestrator,
            [
                new[] { service },
                Array.Empty<ServiceResourceDefinition>(),
                Array.Empty<NetworkResourceDefinition>(),
                resourceManager
            ]));
        var expectedPath = Path.GetFullPath(Path.Combine(workingDirectory, "Data/storage/sql-server"));

        Assert.Contains("    volumes:", yaml);
        Assert.Contains($"      - \"{expectedPath}:/var/opt/mssql\"", yaml);
        Assert.True(Directory.Exists(expectedPath));
    }

    [Fact]
    public void DockerComposeOrchestrator_CreatesVolumeMountMaterializations()
    {
        var workingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var storage = new Resource(
            "storage:local",
            "Local Storage",
            StorageProviderNames.LocalStorage,
            StorageProviderNames.LocalStorage,
            "local",
            ResourceState.Running,
            [],
            StorageMedia.FileSystem,
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.storage",
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.StorageLocation] = "./Data/storage"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageMountProvider)]);
        var volume = new Resource(
            "volume:sql-data",
            "SQL Data",
            "Volume",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            StorageProviderNames.LocalStorage,
            DateTimeOffset.UtcNow,
            ["storage:local"],
            TypeId: "cloudshell.volume",
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>
            {
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem,
                [ResourceAttributeNames.VolumeStorageResourceId] = "storage:local",
                [ResourceAttributeNames.VolumeSubPath] = "sql-server"
            },
            Capabilities: [new(ResourceCapabilityIds.StorageVolume)]);
        var resourceManager = new StaticResourceManagerStore([storage, volume]);
        var createMaterializations = typeof(DockerComposeResourceOrchestrator).GetMethod(
            "CreateComposeVolumeMaterializations",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(createMaterializations);
        var materializations = Assert.IsAssignableFrom<IReadOnlyList<ResourceVolumeMountMaterialization>>(
            createMaterializations.Invoke(
                null,
                [
                    new[]
                    {
                        new ResourceVolumeMount("volume:sql-data", "/var/opt/mssql", false, "data")
                    },
                    resourceManager,
                    workingDirectory
                ]));
        var materialization = Assert.Single(materializations);
        var expectedPath = Path.GetFullPath(Path.Combine(workingDirectory, "Data/storage/sql-server"));

        Assert.Equal("volume:sql-data", materialization.VolumeReference);
        Assert.Equal("/var/opt/mssql", materialization.TargetPath);
        Assert.Equal(expectedPath, materialization.Source);
        Assert.False(materialization.ReadOnly);
        Assert.Equal(ResourceVolumeMountMaterializationStatus.Materialized, materialization.Status);
        Assert.True(Directory.Exists(expectedPath));
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
        var method = typeof(ApplicationResourceService).GetMethod(
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
        var method = typeof(ApplicationResourceService).GetMethod(
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
        var method = typeof(ApplicationResourceService).GetMethod(
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
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
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
        var method = typeof(ApplicationResourceService).GetMethod(
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
        var method = typeof(ApplicationResourceService).GetMethod(
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
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
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
    public async Task ContainerApplicationProvider_PreflightsRestartBeforeImageUpdate()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var settings = resources
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings")
                    .WithEntry("Message", "hello");

                resources
                    .AddContainer("api", "example/api:latest")
                    .WithContainerHost("docker")
                    .WithEnvironment(
                        "WELCOME_MESSAGE",
                        settings.Entry("Missing"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarationStore = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var registrations = new DeclarationRegistrationStore(declarationStore);
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var runtimeStates = serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>();
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var resourceManager = new StaticResourceManagerStore(resources, providers);
        var currentProcess = Process.GetCurrentProcess();
        runtimeStates.Save(new ApplicationRuntimeState(
            resource.Id,
            currentProcess.Id,
            currentProcess.StartTime,
            DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UpdateImageAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                "example/api:20260608",
                restartIfRunning: true));
        var updated = provider.GetApplication("application:api");

        Assert.Contains(
            "Container app resource 'api' cannot update image and restart because Could not resolve configuration-entry reference for setting 'WELCOME_MESSAGE'.",
            exception.Message);
        Assert.NotNull(updated);
        Assert.Equal("example/api:latest", updated.ContainerImage);
        Assert.Equal(resource.ResourceAttributes[ResourceAttributeNames.ContainerRevision], updated.ContainerRevision);
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
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
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

    [Fact]
    public async Task ContainerApplicationProvider_PreflightsRestartBeforeReplicaUpdate()
    {
        var services = new ServiceCollection();
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddExtension<ApplicationProviderExtension>()
            .AddConfigurationProvider()
            .Resources(resources =>
            {
                var settings = resources
                    .AddConfigurationStore("configuration:app").WithDisplayName("App Settings")
                    .WithEntry("Message", "hello");

                resources
                    .AddContainer("api", "example/api:latest", replicas: 2)
                    .WithContainerHost("docker")
                    .WithEnvironment(
                        "WELCOME_MESSAGE",
                        settings.Entry("Missing"));
            });

        using var serviceProvider = services.BuildServiceProvider();
        var declarationStore = serviceProvider.GetRequiredService<ResourceDeclarationStore>();
        var registrations = new DeclarationRegistrationStore(declarationStore);
        var provider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
        var providers = serviceProvider.GetServices<IResourceProvider>().ToArray();
        var resources = providers
            .SelectMany(provider => provider.GetResources())
            .ToArray();
        var runtimeStates = serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>();
        var resource = Assert.Single(provider.GetResources(), resource =>
            resource.Id == "application:api");
        var resourceManager = new StaticResourceManagerStore(resources, providers);
        var currentProcess = Process.GetCurrentProcess();
        runtimeStates.Save(new ApplicationRuntimeState(
            resource.Id,
            currentProcess.Id,
            currentProcess.StartTime,
            DateTimeOffset.UtcNow));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.UpdateReplicasAsync(
                new ResourceProcedureContext(
                    resource,
                    registrations.GetRegistration(resource.Id),
                    null,
                    registrations,
                    resourceManager),
                3,
                restartIfRunning: true));
        var updated = provider.GetApplication("application:api");

        Assert.Contains(
            "Container app resource 'api' cannot update replicas and restart because Could not resolve configuration-entry reference for setting 'WELCOME_MESSAGE'.",
            exception.Message);
        Assert.NotNull(updated);
        Assert.True(updated.ReplicasEnabled);
        Assert.Equal(2, updated.Replicas);
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

    private static Resource CreateEndpointResourceWithNetworkMapping(string id, string endpointName, string address) =>
        new(
            id,
            id,
            "Application",
            "Test",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract(endpointName, "http", ResourceExposureScope.Public)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.EndpointSource)],
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(id, endpointName, address, ResourceExposureScope.Public)
            ]);

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

    private static LocalProcessDefinition CreateShortLivedProcessDefinition() =>
        OperatingSystem.IsWindows()
            ? new LocalProcessDefinition(
                $"process:test:{Guid.NewGuid():N}",
                Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                "/d /s /c \"timeout /t 1 /nobreak > nul\"",
                Lifetime: LocalProcessLifetime.ControlPlaneScoped)
            : new LocalProcessDefinition(
                $"process:test:{Guid.NewGuid():N}",
                "/bin/sh",
                "-c \"sleep 1\"",
                Lifetime: LocalProcessLifetime.ControlPlaneScoped);

    private static LocalProcessDefinition CreateProcessTreeDefinition(
        string workingDirectory,
        string childPidPath) =>
        new(
            $"process:test:{Guid.NewGuid():N}",
            "/bin/sh",
            $"-c \"sleep 30 & echo $! > {QuoteShellArgument(childPidPath)}; wait\"",
            WorkingDirectory: workingDirectory,
            Lifetime: LocalProcessLifetime.ControlPlaneScoped);

    private static string QuoteShellArgument(string value) =>
        $"'{value.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string CreateFailingContainerHostExecutable(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake container host executable test helper uses Unix file mode.");
        }

        var path = Path.Combine(directory, "fake-docker");
        File.WriteAllText(
            path,
            """
            #!/bin/sh
            echo "Cannot connect to Docker daemon at unix:///var/run/docker.sock. Is the docker daemon running?" >&2
            exit 1
            """);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
        return path;
    }

    private static string CreateHangingContainerHostExecutable(string directory, string pidPath)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake container host executable test helper uses Unix file mode.");
        }

        var path = Path.Combine(directory, "fake-docker-hang");
        File.WriteAllText(
            path,
            $$"""
            #!/bin/sh
            echo $$ > {{QuoteShellArgument(pidPath)}}
            sleep 30
            """);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
        return path;
    }

    private static string CreateRecordingContainerHostExecutable(string directory, string commandLogPath)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The fake container host executable test helper uses Unix file mode.");
        }

        var path = Path.Combine(directory, "fake-docker-record");
        File.WriteAllText(
            path,
            $$"""
            #!/bin/sh
            printf '%s\n' "$*" >> {{QuoteShellArgument(commandLogPath)}}
            exit 0
            """);
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
        return path;
    }

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

    private static async Task<int> WaitForProcessIdFileAsync(string path, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (File.Exists(path) &&
                int.TryParse(await File.ReadAllTextAsync(path), out var processId))
            {
                return processId;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for process id file '{path}'.");
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

    private sealed class TestNamePublishingProvider(
        string providerName,
        ResourceProcedureResult? result = null,
        Exception? exception = null) : INamePublishingProvider
    {
        public string ProviderName => providerName;

        public DnsNamePublishingContext? Context { get; private set; }

        public bool CanPublish(DnsNamePublishingContext context) =>
            string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase) ||
            context.PublisherResources.Count > 0;

        public Task<ResourceProcedureResult> ReconcileAsync(
            DnsNamePublishingContext context,
            CancellationToken cancellationToken = default)
        {
            Context = context;
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(result ?? ResourceProcedureResult.Completed("Reconciled name mappings."));
        }
    }

    private sealed class RecordingResourceEventSink : IResourceEventSink
    {
        private readonly List<ResourceEvent> events = [];

        public IReadOnlyList<ResourceEvent> Events => events;

        public void Append(ResourceEvent resourceEvent)
        {
            events.Add(resourceEvent);
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
                    Actions: [ResourceAction.Start]))
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

    private sealed class TestResourceIdentityCredentialEnvironmentProvider :
        IResourceIdentityCredentialEnvironmentProvider
    {
        public string ProviderId => "external";

        public bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider) =>
            string.Equals(provider.Id, "identity:external", StringComparison.OrdinalIgnoreCase);

        public IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
            ResourceIdentityCredentialEnvironmentRequest request) =>
            [
                new(
                    EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable,
                    "https://identity.example.test/token"),
                new(
                    EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable,
                    "external-api-service"),
                new(
                    EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable,
                    "external-api-secret"),
                new(
                    EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable,
                    request.DefaultScope)
            ];
    }

    private static string BuildDotNetAspNetCoreProjectArguments(
        string projectPath,
        bool hotReload,
        string? applicationArguments)
    {
        var method = typeof(ApplicationResourceService).GetMethod(
            "BuildDotNetAspNetCoreProjectArguments",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        return Assert.IsType<string>(method!.Invoke(null, [projectPath, hotReload, applicationArguments]));
    }

    private static void AssertConfigurationProviderEndpointProjection(
        Resource resource,
        string endpointName)
    {
        var endpoint = Assert.Single(resource.Endpoints);
        Assert.Equal(endpointName, endpoint.Name);
        Assert.Equal("http", endpoint.Protocol);
        Assert.NotNull(endpoint.TargetPort);

        var mapping = Assert.Single(resource.ResourceEndpointNetworkMappings);
        Assert.Equal(endpointName, mapping.Name);
        Assert.Equal(new ResourceEndpointReference(resource.Id, endpointName), mapping.Target);
        Assert.StartsWith("http://localhost:", mapping.Address, StringComparison.Ordinal);
    }

    private static void WriteLaunchSettings(
        string contentRoot,
        string projectPath,
        string json)
    {
        var resolvedProjectPath = Path.GetFullPath(projectPath, contentRoot);
        var launchSettingsDirectory = Path.Combine(
            Path.GetDirectoryName(resolvedProjectPath)!,
            "Properties");
        Directory.CreateDirectory(launchSettingsDirectory);
        File.WriteAllText(Path.Combine(launchSettingsDirectory, "launchSettings.json"), json);
    }

    private sealed class TestOptionsMonitor<TOptions>(TOptions currentValue) :
        Microsoft.Extensions.Options.IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = currentValue;

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = [];

        public IReadOnlyCollection<string> Messages => _messages.ToArray();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull =>
                null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
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

        public ResourceEndpointNetworkMapping ResolveNetworkEndpoint(
            string networkId,
            ResourceEndpointRequest request,
            int autoLocalPortStart,
            int autoLocalPortEnd) =>
            ResourceEndpointNetworkMapping.ForEndpoint(
                networkId,
                request.Name,
                $"{request.ProtocolName}://{DefaultHost}:4123",
                request.Exposure,
                networkResourceId: networkId,
                sourceEndpointName: request.Name);

        public ResourceEndpointNetworkMapping ResolveServiceEndpoint(
            string serviceId,
            ServicePort port,
            int autoLocalPortStart,
            int autoLocalPortEnd) =>
            ResourceEndpointNetworkMapping.ForEndpoint(
                serviceId,
                port.Name,
                $"{port.Protocol}://{DefaultHost}:4124",
                port.Exposure,
                sourceEndpointName: port.Name);
    }
}
