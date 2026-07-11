using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Tests;

public sealed class PythonAppResourceTypeTests
{
    [Fact]
    public async Task ValidateAsync_RequiresProjectPathForLocalSourceMode()
    {
        var provider = new PythonAppResourceTypeProvider();
        var validation = await provider.ValidateAsync(
            Resolve(new ResourceDefinition(
                "api",
                PythonAppResourceTypeProvider.ResourceTypeId,
                ProviderId: PythonAppResourceTypeProvider.ProviderId)),
            new ResourceProviderContext());

        Assert.True(validation.HasErrors);
        Assert.Contains(validation.Diagnostics, diagnostic =>
            diagnostic.Code == "application.pythonApp.pathRequired");
    }

    [Fact]
    public async Task ValidateAsync_AcceptsUploadedArtifactWithoutProjectPath()
    {
        var provider = new PythonAppResourceTypeProvider();
        var validation = await provider.ValidateAsync(
            Resolve(new ResourceDefinition(
                "api",
                PythonAppResourceTypeProvider.ResourceTypeId,
                ProviderId: PythonAppResourceTypeProvider.ProviderId,
                Attributes: new ResourceAttributeValueMap(
                    new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        [ApplicationArtifactAttributeIds.SourceKind] =
                            ResourceAttributeValue.String(DeploymentArtifactSourceKinds.UploadedArtifact),
                        [ApplicationArtifactAttributeIds.SourceOwner] =
                            ResourceAttributeValue.String(ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner),
                        [ApplicationArtifactAttributeIds.Source] =
                            ResourceAttributeValue.FromObject(new ApplicationArtifactReference(
                                "artifact-api",
                                "rev-1",
                                "zip",
                                "sha256:123",
                                1024,
                                ".",
                                "pythonSourceDirectory"))
                    }))),
            new ResourceProviderContext());

        Assert.False(validation.HasErrors);
    }

    [Fact]
    public async Task ValidateAsync_AcceptsArtifactModeWithoutCurrentArtifact()
    {
        var provider = new PythonAppResourceTypeProvider();
        var validation = await provider.ValidateAsync(
            Resolve(new ResourceDefinition(
                "api",
                PythonAppResourceTypeProvider.ResourceTypeId,
                ProviderId: PythonAppResourceTypeProvider.ProviderId,
                Attributes: new ResourceAttributeValueMap(
                    new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        [ApplicationArtifactAttributeIds.SourceKind] =
                            ResourceAttributeValue.String(DeploymentArtifactSourceKinds.UploadedArtifact),
                        [ApplicationArtifactAttributeIds.SourceOwner] =
                            ResourceAttributeValue.String(ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner),
                        [ApplicationArtifactAttributeIds.Enabled] =
                            ResourceAttributeValue.Boolean(true)
                    }))),
            new ResourceProviderContext());

        Assert.False(validation.HasErrors);
    }

    [Fact]
    public async Task AddPythonApp_EmitsExpectedResourceDefinition()
    {
        var graph = new ResourceGraphBuilder();

        graph
            .AddPythonApp("api", "samples/python")
            .WithModule("sample.api")
            .WithArguments("--reload")
            .WithHttpEndpoint(host: "localhost", port: 5188, targetPort: 5188)
            .WithEnvironmentVariable("APP_MODE", "development");

        var resource = Assert.Single(
            graph.BuildTemplate("python").Resources,
            resource => resource.EffectiveResourceId == "application.python-app:api");

        Assert.Equal("application.python-app:api", resource.EffectiveResourceId);
        Assert.Equal(PythonAppResourceTypeProvider.ResourceTypeId, resource.TypeId);
        Assert.Equal(PythonAppResourceTypeProvider.ProviderId, resource.ProviderId);
        Assert.Equal(
            "samples/python",
            resource.ResourceAttributeValues.GetValueOrDefault(
                PythonAppResourceTypeProvider.Attributes.ProjectPath));
        Assert.Equal(
            "python3",
            resource.ResourceAttributeValues.GetValueOrDefault(
                PythonAppResourceTypeProvider.Attributes.Command));
        Assert.Equal(
            "sample.api",
            resource.ResourceAttributeValues.GetValueOrDefault(
                PythonAppResourceTypeProvider.Attributes.Module));
        Assert.Equal(
            "--reload",
            resource.ResourceAttributeValues.GetValueOrDefault(
                PythonAppResourceTypeProvider.Attributes.Arguments));

        var endpoints = resource.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            PythonAppResourceTypeProvider.Attributes.EndpointRequests);
        var endpoint = Assert.Single(endpoints!);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5188, endpoint.Port);
        Assert.Equal(5188, endpoint.TargetPort);

        var variables = resource.ResourceAttributeValues.GetObject<Dictionary<string, PythonAppEnvironmentVariableValue>>(
            PythonAppResourceTypeProvider.Attributes.EnvironmentVariables);
        Assert.NotNull(variables);
        Assert.Equal("development", variables["APP_MODE"].Value);

        var services = new ServiceCollection()
            .AddPythonAppResourceType()
            .BuildServiceProvider();
        var provider = Assert.Single(
            services.GetServices<IResourceTypeProvider>(),
            provider => provider.TypeId == PythonAppResourceTypeProvider.ResourceTypeId);

        var validation = await provider.ValidateAsync(
            Resolve(resource),
            new ResourceProviderContext());

        Assert.False(validation.HasErrors);
    }

    [Fact]
    public void ProcessCommandFactory_StartsScriptByDefault()
    {
        var resource = new ResourceGraphBuilder()
            .AddPythonApp("api", "samples/python")
            .Build();
        var resolved = Resolve(resource);
        var factory = new PythonAppProcessCommandFactory();

        var startInfo = factory.CreateStartInfo(
            resolved,
            Path.GetFullPath("samples/python"));

        Assert.Equal("python3", startInfo.FileName);
        Assert.Equal(["app.py"], startInfo.ArgumentList);
        Assert.Equal("application.python-app:api", startInfo.Environment["CLOUDSHELL_RESOURCE_ID"]);
        Assert.Equal("api", startInfo.Environment["CLOUDSHELL_RESOURCE_NAME"]);
    }

    [Fact]
    public void ProcessCommandFactory_StartsModuleWhenConfigured()
    {
        var resource = new ResourceGraphBuilder()
            .AddPythonApp("api", "samples/python")
            .WithModule("sample.api")
            .WithArguments("--port 5188")
            .Build();
        var resolved = Resolve(resource);
        var factory = new PythonAppProcessCommandFactory();

        var startInfo = factory.CreateStartInfo(
            resolved,
            Path.GetFullPath("samples/python"));

        Assert.Equal("python3", startInfo.FileName);
        Assert.Equal(["-m", "sample.api", "--port", "5188"], startInfo.ArgumentList);
    }

    [Fact]
    public async Task StartOperation_AllowsArtifactModeWithoutSourceMetadata()
    {
        var resource = Resolve(new ResourceDefinition(
            "api",
            PythonAppResourceTypeProvider.ResourceTypeId,
            ProviderId: PythonAppResourceTypeProvider.ProviderId,
            Attributes: new ResourceAttributeValueMap(
                new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [ApplicationArtifactAttributeIds.SourceKind] =
                        ResourceAttributeValue.String(DeploymentArtifactSourceKinds.UploadedArtifact),
                    [ApplicationArtifactAttributeIds.SourceOwner] =
                        ResourceAttributeValue.String(ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner),
                    [ApplicationArtifactAttributeIds.Enabled] =
                        ResourceAttributeValue.Boolean(true)
                })));
        var provider = new PythonAppStartOperationProvider();
        var operation = resource.Operations.Resolve(PythonAppResourceTypeProvider.Operations.Start);
        var projection = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            await provider.ProjectAsync(
                resource,
                operation,
                new ResourceOperationProjectionContext()));

        Assert.True(await projection.CanExecuteAsync());
        Assert.Null(projection.UnavailableReason);
    }

    private static CloudShell.ResourceModel.Resource Resolve(ResourceDefinition definition)
    {
        var provider = new PythonAppResourceTypeProvider();
        var resolver = new ResourceResolver(
            [PythonAppResourceTypeProvider.ClassDefinition],
            [provider.TypeDefinition]);

        return resolver.Resolve(definition);
    }
}
