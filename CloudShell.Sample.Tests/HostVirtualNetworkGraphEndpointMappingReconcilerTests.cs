using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.HostVirtualNetwork;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Sample.Tests;

public sealed class HostVirtualNetworkGraphEndpointMappingReconcilerTests
{
    [Fact]
    public async Task ReconcileEndpointMappings_ProvisionsGraphMappingThroughHostNetworkingProvisioner()
    {
        const string graphHostNetworkingResourceId = "networking:graph-host-local";
        const string graphApiResourceId = "application.aspnet-core-project:graph-vnet-api";
        const string graphNetworkResourceId = "network:graph-sample-vnet";
        var provisioner = new RecordingEndpointMappingProvisioner();
        var services = new ServiceCollection();
        services.AddSingleton<IResourceEndpointMappingProvisioner>(provisioner);
        services.AddSingleton<
            IVirtualNetworkEndpointMappingReconciler,
            HostVirtualNetworkGraphEndpointMappingReconciler>();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var graphHostNetwork = graph
            .AddLocalHostNetwork("graph-host-local")
            .WithResourceId(graphHostNetworkingResourceId);
        var graphApi = graph
            .AddAspNetCoreProject(
                "graph-vnet-api",
                "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
            .WithResourceId(graphApiResourceId)
            .WithArguments("--urls http://localhost:5291")
            .UseLaunchSettings(false)
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5291,
                exposure: "Local");

        graph
            .AddVirtualNetwork("graph-sample-vnet")
            .WithResourceId(graphNetworkResourceId)
            .DependsOn(graphHostNetwork, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
            .DependsOn(graphApi, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            .AsDefault()
            .WithHostReadiness("providerRequired")
            .WithMappingProviders(graphHostNetworkingResourceId)
            .AddEndpoint(
                "api-public",
                "http",
                5292,
                "Public")
            .AddEndpointNetworkMapping(
                "api-public",
                "http://localhost:5292",
                name: "Graph API public ingress",
                provider: graphHostNetwork)
            .MapEndpoint(
                "api-public",
                graphApi,
                "http",
                graphHostNetwork,
                "mapping:graph-api-public",
                "Graph API public ingress");

        var apply = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                graph.BuildDeployment("host-virtual-network", environmentId: "local"),
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(apply.HasErrors, FormatDiagnostics(apply.Diagnostics));

        var operationResolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync(
                graphNetworkResourceId,
                VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings);

        Assert.False(operationResolution.HasErrors, FormatDiagnostics(operationResolution.Diagnostics));
        var operation = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            operationResolution.Operation);
        var execution = await operation.ExecuteAsync();

        Assert.False(execution.HasErrors, FormatDiagnostics(execution.Diagnostics));
        var context = Assert.Single(provisioner.Contexts);
        Assert.Equal("mapping:graph-api-public", context.Mapping.Id);
        Assert.Equal(graphNetworkResourceId, context.NetworkResource.Id);
        Assert.Equal(LocalHostNetworkProvider.ResourceId, context.ProviderResource.Id);
        Assert.Equal(LocalHostNetworkProvider.ResourceType, context.ProviderResource.EffectiveTypeId);
        Assert.Equal("api-public", context.SourceEndpoint.Name);
        Assert.Equal("http://localhost:5292", context.SourceEndpointNetworkMapping?.Address);
        Assert.Equal(graphApiResourceId, context.TargetResource.Id);
        Assert.Equal("http", context.TargetEndpoint.Name);
        Assert.Equal("http://localhost:5291", context.TargetEndpointNetworkMapping?.Address);
    }

    private static string FormatDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}"));

    private sealed class RecordingEndpointMappingProvisioner : IResourceEndpointMappingProvisioner
    {
        public List<ResourceEndpointMappingProvisioningContext> Contexts { get; } = [];

        public bool CanProvisionEndpointMapping(
            ResourceEndpointMappingProvisioningContext context) =>
            true;

        public Task<ResourceProcedureResult> ProvisionEndpointMappingAsync(
            ResourceEndpointMappingProvisioningContext context,
            CancellationToken cancellationToken = default)
        {
            Contexts.Add(context);
            return Task.FromResult(ResourceProcedureResult.Completed(
                $"Recorded endpoint mapping '{context.Mapping.Id}'."));
        }
    }
}
