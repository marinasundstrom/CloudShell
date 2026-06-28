using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.HostVirtualNetwork;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Sample.Tests;

public sealed class HostVirtualNetworkEndpointMappingReconcilerTests
{
    [Fact]
    public async Task ReconcileEndpointMappings_ProvisionsMappingThroughHostNetworkingProvisioner()
    {
        const string hostNetworkingResourceId = "networking:host-local";
        const string apiResourceId = "application.aspnet-core-project:vnet-api";
        const string networkResourceId = "network:sample-vnet";
        var provisioner = new RecordingEndpointMappingProvisioner();
        var services = new ServiceCollection();
        services.AddSingleton<IResourceEndpointMappingProvisioner>(provisioner);
        services.AddSingleton<
            IVirtualNetworkEndpointMappingReconciler,
            HostVirtualNetworkEndpointMappingReconciler>();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithResourceId(hostNetworkingResourceId);
        var api = graph
            .AddAspNetCoreProject(
                "vnet-api",
                "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
            .WithResourceId(apiResourceId)
            .WithArguments("--urls http://localhost:5291")
            .UseLaunchSettings(false)
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5291,
                exposure: "Local");

        graph
            .AddVirtualNetwork("sample-vnet")
            .WithResourceId(networkResourceId)
            .DependsOn(hostNetwork, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
            .DependsOn(api, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            .AsDefault()
            .WithHostReadiness("providerRequired")
            .WithMappingProviders(hostNetworkingResourceId)
            .AddEndpoint(
                "api-public",
                "http",
                5292,
                "Public")
            .AddEndpointNetworkMapping(
                "api-public",
                "http://localhost:5292",
                name: "API public ingress",
                provider: hostNetwork)
            .MapEndpoint(
                "api-public",
                api,
                "http",
                hostNetwork,
                "mapping:api-public",
                "API public ingress");

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
                networkResourceId,
                VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings);

        Assert.False(operationResolution.HasErrors, FormatDiagnostics(operationResolution.Diagnostics));
        var operation = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            operationResolution.Operation);
        var execution = await operation.ExecuteAsync();

        Assert.False(execution.HasErrors, FormatDiagnostics(execution.Diagnostics));
        var context = Assert.Single(provisioner.Contexts);
        Assert.Equal("mapping:api-public", context.Mapping.Id);
        Assert.Equal(networkResourceId, context.NetworkResource.Id);
        Assert.Equal(LocalHostNetworkProvider.ResourceId, context.ProviderResource.Id);
        Assert.Equal(LocalHostNetworkProvider.ResourceType, context.ProviderResource.EffectiveTypeId);
        Assert.Equal("api-public", context.SourceEndpoint.Name);
        Assert.Equal("http://localhost:5292", context.SourceEndpointNetworkMapping?.Address);
        Assert.Equal(apiResourceId, context.TargetResource.Id);
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
