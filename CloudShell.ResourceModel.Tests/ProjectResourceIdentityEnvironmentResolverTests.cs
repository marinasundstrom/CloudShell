using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using ResourceModelResource = CloudShell.ResourceModel.Resource;
using ResourceModelState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class ProjectResourceIdentityEnvironmentResolverTests
{
    [Fact]
    public async Task ResolveAsync_DerivesAspNetCoreIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.dotnet-app:api", "api");
        var resolver = CreateResolver(declarations);
        var resource = CreateAspNetCoreProject("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("https://identity.example.test/token", variables["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"]);
        Assert.Equal("application.dotnet-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.dotnet-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
        Assert.Equal("ControlPlane.Access", variables["CLOUDSHELL_IDENTITY_SCOPE"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesJavaIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.java-app:worker", "worker");
        var resolver = CreateResolver(declarations);
        var resource = CreateJavaApp("worker");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("https://identity.example.test/token", variables["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"]);
        Assert.Equal("application.java-app:worker/worker", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.java-app:worker/worker", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesJavaScriptIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.javascript-app:frontend", "frontend");
        var resolver = CreateResolver(declarations);
        var resource = CreateJavaScriptApp("frontend");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("application.javascript-app:frontend/frontend", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.javascript-app:frontend/frontend", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesGoIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.go-app:api", "api");
        var resolver = CreateResolver(declarations);
        var resource = CreateGoApp("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("application.go-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.go-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesPythonIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.python-app:api", "api");
        var resolver = CreateResolver(declarations);
        var resource = CreatePythonApp("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("application.python-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.python-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesContainerAppIdentityEnvironmentFromDeclaration()
    {
        var declarations = CreateDeclarations("application.container-app:api", "api");
        var resolver = CreateResolver(declarations);
        var resource = CreateContainerApp("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("application.container-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.container-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ContainerDeploymentDescriptor_IncludesResolvedIdentityEnvironment()
    {
        var descriptor = new ContainerApplicationResourceModelGraphDeploymentDescriptor(
            goEnvironmentProviders:
            [
                new FixedGoAppRuntimeEnvironmentProvider(
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"] = "https://identity.example.test/token",
                        ["CLOUDSHELL_IDENTITY_CLIENT_ID"] = "application.container-app:api/api",
                        ["CLOUDSHELL_IDENTITY_CLIENT_SECRET"] = "secret-application.container-app:api/api",
                        ["CLOUDSHELL_IDENTITY_SCOPE"] = "ControlPlane.Access"
                    })
            ]);
        var resource = CreateContainerApp("api");

        var deployment = await descriptor.DescribeDeploymentAsync(
            new ResourceModelGraphDeploymentDescriptionContext(
                ToManagerResource(resource),
                resource,
                new ResourceProcedureContext(
                    ToManagerResource(resource),
                    null,
                    null,
                    new EmptyResourceRegistrationStore())));

        Assert.NotNull(deployment);
        var variables = deployment.Spec.Service.Workload.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("application.container-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.container-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
    }

    [Fact]
    public async Task ContainerDeploymentDescriptor_IncludesGoContainerLiteralEnvironment()
    {
        var descriptor = new ContainerApplicationResourceModelGraphDeploymentDescriptor();
        var resource = CreateContainerApp(
            "api",
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [GoAppResourceTypeProvider.Attributes.EnvironmentVariables] =
                    ResourceAttributeValue.FromObject(
                        new Dictionary<string, GoAppEnvironmentVariableValue>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["APP_MODE"] = new("development")
                        })
            });

        var deployment = await descriptor.DescribeDeploymentAsync(
            new ResourceModelGraphDeploymentDescriptionContext(
                ToManagerResource(resource),
                resource,
                new ResourceProcedureContext(
                    ToManagerResource(resource),
                    null,
                    null,
                    new EmptyResourceRegistrationStore())));

        Assert.NotNull(deployment);
        var variables = deployment.Spec.Service.Workload.WorkloadEnvironmentVariables
            .ToDictionary(variable => variable.Name, variable => variable.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("development", variables["APP_MODE"]);
    }

    [Fact]
    public async Task ResolveAsync_DerivesIdentityEnvironmentFromResourceAttributes()
    {
        var resolver = CreateResolver(declarations: null);
        var resource = CreateAspNetCoreProject(
            "api",
            new ResourceIdentityBindingAttribute(
                "identity:built-in",
                Name: "api"));

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("https://identity.example.test/token", variables["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"]);
        Assert.Equal("application.dotnet-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.dotnet-app:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
        Assert.Equal("ControlPlane.Access", variables["CLOUDSHELL_IDENTITY_SCOPE"]);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsEmptyWhenResourceHasNoIdentityDeclaration()
    {
        var resolver = CreateResolver(new ResourceDeclarationStore());
        var resource = CreateAspNetCoreProject("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Empty(variables);
    }

    private static ProjectResourceIdentityEnvironmentResolver CreateResolver(
        ResourceDeclarationStore? declarations) =>
        new(
            [new FixedResourceIdentityCredentialEnvironmentProvider()],
            new ResourceIdentityProviderCatalog(
                [new("identity:built-in", "Built-in", ResourceIdentityProviderKind.BuiltIn)],
                "identity:built-in"),
            declarations);

    private static ResourceDeclarationStore CreateDeclarations(
        string resourceId,
        string identityName)
    {
        var declarations = new ResourceDeclarationStore();
        declarations.Declare(
            new TestCloudShellBuilder(),
            "applications",
            resourceId,
            identity: new ResourceIdentityBinding(
                "identity:built-in",
                Name: identityName));
        return declarations;
    }

    private static ResourceModelResource CreateAspNetCoreProject(
        string name,
        ResourceIdentityBindingAttribute? identity = null)
    {
        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [new AspNetCoreProjectResourceTypeProvider().TypeDefinition]);
        var definition = new ResourceDefinition(
            name,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.dotnet-app:{name}",
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = "src/Api/Api.csproj"
            });
        if (identity is not null)
        {
            definition = definition.WithDeclarationAttributes(identity);
        }

        return resolver.Resolve(ResourceModelState.FromDefinition(definition));
    }

    private static ResourceModelResource CreateJavaApp(string name)
    {
        var resolver = new ResourceResolver(
            [JavaAppResourceTypeProvider.ClassDefinition],
            [new JavaAppResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(new ResourceModelState(
            name,
            JavaAppResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.java-app:{name}",
            ProviderId: JavaAppResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [JavaAppResourceTypeProvider.Attributes.ProjectPath] = "src/JavaApp",
                [JavaAppResourceTypeProvider.Attributes.ArtifactPath] = "target/app.jar",
                [JavaAppResourceTypeProvider.Attributes.MainClass] = "com.example.App"
            }));
    }

    private static ResourceModelResource CreateJavaScriptApp(string name)
    {
        var resolver = new ResourceResolver(
            [JavaScriptAppResourceTypeProvider.ClassDefinition],
            [new JavaScriptAppResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(new ResourceModelState(
            name,
            JavaScriptAppResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.javascript-app:{name}",
            ProviderId: JavaScriptAppResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [JavaScriptAppResourceTypeProvider.Attributes.ProjectPath] = "src/frontend"
            }));
    }

    private static ResourceModelResource CreateGoApp(string name)
    {
        var resolver = new ResourceResolver(
            [GoAppResourceTypeProvider.ClassDefinition],
            [new GoAppResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(new ResourceModelState(
            name,
            GoAppResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.go-app:{name}",
            ProviderId: GoAppResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [GoAppResourceTypeProvider.Attributes.ProjectPath] = "src/api"
            }));
    }

    private static ResourceModelResource CreatePythonApp(string name)
    {
        var resolver = new ResourceResolver(
            [PythonAppResourceTypeProvider.ClassDefinition],
            [new PythonAppResourceTypeProvider().TypeDefinition]);
        return resolver.Resolve(new ResourceModelState(
            name,
            PythonAppResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.python-app:{name}",
            ProviderId: PythonAppResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [PythonAppResourceTypeProvider.Attributes.ProjectPath] = "src/api"
            }));
    }

    private static ResourceModelResource CreateContainerApp(
        string name,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>? additionalAttributes = null)
    {
        var resolver = new ResourceResolver(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider().TypeDefinition]);
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = "example/api:dev",
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = 1
        };
        if (additionalAttributes is not null)
        {
            foreach (var (attributeName, value) in additionalAttributes)
            {
                attributes[attributeName] = value;
            }
        }

        return resolver.Resolve(new ResourceModelState(
            name,
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: $"application.container-app:{name}",
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static CloudShell.Abstractions.ResourceManager.Resource ToManagerResource(
        ResourceModelResource resource) =>
        new(
            resource.EffectiveResourceId,
            resource.Name,
            resource.Type.TypeId.ToString(),
            resource.State.ProviderId ?? resource.Type.Definition.DefaultProviderId ?? string.Empty,
            "local",
            CloudShell.Abstractions.ResourceManager.ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: resource.Type.TypeId.ToString(),
            Attributes: resource.Attributes.ToDictionary(
                attribute => attribute.Name.ToString(),
                attribute => attribute.Value ?? string.Empty));

    private sealed class FixedResourceIdentityCredentialEnvironmentProvider :
        IResourceIdentityCredentialEnvironmentProvider
    {
        public string ProviderId => "identity:built-in";

        public bool CanCreateEnvironment(ResourceIdentityProviderDefinition provider) =>
            provider.Kind == ResourceIdentityProviderKind.BuiltIn;

        public IReadOnlyList<EnvironmentVariableAssignment> CreateEnvironment(
            ResourceIdentityCredentialEnvironmentRequest request)
        {
            var clientId = string.IsNullOrWhiteSpace(request.Identity.Name)
                ? request.Identity.ResourceId
                : $"{request.Identity.ResourceId}/{request.Identity.Name}";
            return
            [
                new("CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT", "https://identity.example.test/token"),
                new("CLOUDSHELL_IDENTITY_CLIENT_ID", clientId),
                new("CLOUDSHELL_IDENTITY_CLIENT_SECRET", $"secret-{clientId}"),
                new("CLOUDSHELL_IDENTITY_SCOPE", request.DefaultScope)
            ];
        }
    }

    private sealed class FixedGoAppRuntimeEnvironmentProvider(
        IReadOnlyDictionary<string, string> variables) : IGoAppRuntimeEnvironmentProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
            ResourceModelResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(variables);
    }

    private sealed class EmptyResourceRegistrationStore : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [];

        public ResourceRegistration? GetRegistration(string resourceId) => null;

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
