using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.Sample.Tests;

public sealed class DeferredContainerApplicationRuntimeHandlerTests
{
    [Fact]
    public async Task Handler_AcceptsMappedAppWithoutMaterializingRuntime()
    {
        var handler = CreateHandler();
        var resource = await CreateAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));

        var lifecycleDiagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);
        var imageDiagnostics = await handler.ApplyImageAsync(resource);
        var replicaDiagnostics = await handler.ApplyReplicasAsync(resource);

        Assert.Contains(lifecycleDiagnostics, diagnostic =>
            diagnostic.Code == "application.container.deferredRuntime" &&
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Warning);
        Assert.Contains(imageDiagnostics, diagnostic =>
            diagnostic.Code == "application.container.deferredRuntimeImageAccepted");
        Assert.Contains(replicaDiagnostics, diagnostic =>
            diagnostic.Code == "application.container.deferredRuntimeReplicasAccepted");
    }

    [Fact]
    public async Task Handler_IgnoresUnmappedApp()
    {
        var handler = CreateHandler();
        var resource = await CreateAppResourceAsync(
            name: "other",
            resourceId: "application.container-app:other");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static async Task<ResourceModelResource> CreateAppResourceAsync(
        string name = "sample-api",
        string resourceId = "application.container-app:sample-api",
        string image = "cloudshell/mock-api:20260608.1",
        int replicas = 2)
    {
        IResourceOperationProvider[] operationProviders =
        [
            new ContainerApplicationStartOperationProvider(),
            new ContainerApplicationStopOperationProvider(),
            new ContainerApplicationRestartOperationProvider(),
            new ContainerApplicationImageUpdateOperationProvider(),
            new ContainerApplicationReplicasUpdateOperationProvider()
        ];
        var pipeline = new ResourceDefinitionValidationPipeline(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
        var result = await pipeline.ValidateAsync(
            new ResourceDefinition(
                name,
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: resourceId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image,
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = replicas
                }),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private static DeferredContainerApplicationRuntimeHandler CreateHandler()
    {
        var options = new DeferredContainerApplicationRuntimeOptions();
        options.AddResource("application.container-app:sample-api");
        return new(Microsoft.Extensions.Options.Options.Create(options));
    }
}
