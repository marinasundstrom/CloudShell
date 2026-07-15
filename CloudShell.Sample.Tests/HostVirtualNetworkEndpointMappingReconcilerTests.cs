using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Sample.Tests;

public sealed class HostVirtualNetworkEndpointMappingReconcilerTests
{
    [Fact]
    public async Task ReconcileEndpointMappings_ProvisionsMappingThroughHostNetworkingProvisioner()
    {
        const string hostNetworkingResourceId = "cloudshell.hostNetworking.local:host-local";
        const string apiResourceId = "application.dotnet-app:vnet-api";
        const string networkResourceId = "cloudshell.virtualNetwork:sample-vnet";
        var provisioner = new RecordingEndpointMappingProvisioner();
        using var serviceProvider = CreateServiceProvider(provisioner);
        var graph = new ResourceGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithResourceId(hostNetworkingResourceId);
        var api = graph
            .AddDotnetProject(
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
            .DependsOn(hostNetwork)
            .DependsOn(api)
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
            .ApplyTemplateAsync(
                graph.BuildTemplate("host-virtual-network", environmentId: "local"),
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
        Assert.Equal(hostNetworkingResourceId, context.ProviderResource.Id);
        Assert.Equal(LocalHostNetworkResourceTypeProvider.ResourceTypeId.ToString(), context.ProviderResource.EffectiveTypeId);
        Assert.Equal("api-public", context.SourceEndpoint.Name);
        Assert.Equal("http://localhost:5292", context.SourceEndpointNetworkMapping?.Address);
        Assert.Equal(apiResourceId, context.TargetResource.Id);
        Assert.Equal("http", context.TargetEndpoint.Name);
        Assert.Equal("http://localhost:5291", context.TargetEndpointNetworkMapping?.Address);
    }

    [Fact]
    public async Task ReconcileEndpointMappings_ReportsAvailableTargetEndpointsWhenTargetEndpointIsMissing()
    {
        const string hostNetworkingResourceId = "cloudshell.hostNetworking.local:host-local";
        const string apiResourceId = "application.dotnet-app:vnet-api";
        const string networkResourceId = "cloudshell.virtualNetwork:sample-vnet";
        using var serviceProvider = CreateServiceProvider();
        var graph = new ResourceGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithResourceId(hostNetworkingResourceId);
        var api = graph
            .AddDotnetProject(
                "vnet-api",
                "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
            .WithResourceId(apiResourceId)
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5291,
                exposure: "Local");

        graph
            .AddVirtualNetwork("sample-vnet")
            .WithResourceId(networkResourceId)
            .DependsOn(hostNetwork)
            .DependsOn(api)
            .AsDefault()
            .WithMappingProviders(hostNetworkingResourceId)
            .AddEndpoint(
                "api-public",
                "http",
                5292,
                "Public")
            .MapEndpoint(
                "api-public",
                api,
                "missing",
                hostNetwork,
                "mapping:api-public",
                "API public ingress");

        await ApplyTemplateAsync(serviceProvider, graph);

        var execution = await ExecuteReconcileEndpointMappingsAsync(
            serviceProvider,
            networkResourceId);

        var diagnostic = Assert.Single(execution.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("network.endpointMappingProvisioningFailed", diagnostic.Code);
        Assert.Contains("target endpoint 'missing' could not be found", diagnostic.Message);
        Assert.Contains("Available endpoints: 'http'.", diagnostic.Message);
    }

    [Fact]
    public async Task ReconcileEndpointMappings_ReportsAvailableMapperResourcesWhenProviderLacksCapability()
    {
        const string hostNetworkingResourceId = "cloudshell.hostNetworking.local:host-local";
        const string apiResourceId = "application.dotnet-app:vnet-api";
        const string networkResourceId = "cloudshell.virtualNetwork:sample-vnet";
        using var serviceProvider = CreateServiceProvider();
        var graph = new ResourceGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithResourceId(hostNetworkingResourceId);
        var api = graph
            .AddDotnetProject(
                "vnet-api",
                "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
            .WithResourceId(apiResourceId)
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5291,
                exposure: "Local");

        graph
            .AddVirtualNetwork("sample-vnet")
            .WithResourceId(networkResourceId)
            .DependsOn(hostNetwork)
            .DependsOn(api)
            .AsDefault()
            .WithMappingProviders(hostNetworkingResourceId)
            .AddEndpoint(
                "api-public",
                "http",
                5292,
                "Public")
            .MapEndpoint(
                "api-public",
                api,
                "http",
                api,
                "mapping:api-public",
                "API public ingress");

        await ApplyTemplateAsync(serviceProvider, graph);

        var execution = await ExecuteReconcileEndpointMappingsAsync(
            serviceProvider,
            networkResourceId);

        var diagnostic = Assert.Single(execution.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("network.endpointMappingProvisioningFailed", diagnostic.Code);
        Assert.Contains($"provider resource '{apiResourceId}' does not advertise", diagnostic.Message);
        Assert.Contains($"'{ResourceCapabilityIds.NetworkingEndpointMapper}'", diagnostic.Message);
        Assert.Contains($"Available endpoint mapper resources: '{hostNetworkingResourceId}', '{networkResourceId}'.", diagnostic.Message);
    }

    private static string FormatDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}"));

    private static ServiceProvider CreateServiceProvider(
        params IResourceEndpointMappingProvisioner[] provisioners)
    {
        var services = new ServiceCollection();
        foreach (var provisioner in provisioners)
        {
            services.AddSingleton(provisioner);
        }

        services.AddSingleton<
            IVirtualNetworkEndpointMappingReconciler,
            ResourceModelGraphEndpointMappingReconciler>();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddDotnetAppResourceType();
        services.AddResourceModelGraphServices();
        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider("resource-model", "Resource model");
        return services.BuildServiceProvider();
    }

    private static async Task ApplyTemplateAsync(
        IServiceProvider serviceProvider,
        ResourceGraphBuilder graph)
    {
        var apply = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                graph.BuildTemplate("host-virtual-network", environmentId: "local"),
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 27, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(apply.HasErrors, FormatDiagnostics(apply.Diagnostics));
    }

    private static async Task<ResourceOperationExecutionResult> ExecuteReconcileEndpointMappingsAsync(
        IServiceProvider serviceProvider,
        string networkResourceId)
    {
        var operationResolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync(
                networkResourceId,
                VirtualNetworkResourceTypeProvider.Operations.ReconcileEndpointMappings);

        Assert.False(operationResolution.HasErrors, FormatDiagnostics(operationResolution.Diagnostics));
        var operation = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            operationResolution.Operation);
        return await operation.ExecuteAsync();
    }

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
