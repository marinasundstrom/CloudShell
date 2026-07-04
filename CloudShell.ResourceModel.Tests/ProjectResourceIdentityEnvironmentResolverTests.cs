using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
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
        var declarations = CreateDeclarations("application.aspnet-core-project:api", "api");
        var resolver = CreateResolver(declarations);
        var resource = CreateAspNetCoreProject("api");

        var variables = await resolver.ResolveAsync(resource);

        Assert.Equal("https://identity.example.test/token", variables["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"]);
        Assert.Equal("application.aspnet-core-project:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.aspnet-core-project:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
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
        Assert.Equal("application.aspnet-core-project:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_ID"]);
        Assert.Equal("secret-application.aspnet-core-project:api/api", variables["CLOUDSHELL_IDENTITY_CLIENT_SECRET"]);
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
            ResourceId: $"application.aspnet-core-project:{name}",
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

    private sealed class TestCloudShellBuilder : ICloudShellBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
    }
}
