using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.LoadBalancer;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class LoadBalancerGraphNameMappingReconcilerTests
{
    [Fact]
    public async Task ReconcileNameMappings_PublishesGraphMappingsThroughLocalHostNamesProvider()
    {
        const string graphLoadBalancerResourceId = "load-balancer:graph-public";
        const string graphDnsZoneResourceId = "dns:graph-cloudshell-local";
        const string graphNameMappingResourceId = "dns:graph-cloudshell-local:name:api-cloudshell-local";
        var tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var hostsFilePath = Path.Combine(tempDirectory, "cloudshell.hosts");
        var services = new ServiceCollection();
        services.AddSingleton(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsFilePath,
            LocalHostNameResolverRefreshMode = LocalHostNameResolverRefreshMode.Disabled
        });
        services.AddSingleton<LocalHostNamePublishingProvider>();
        services.AddSingleton<INamePublishingProvider>(
            serviceProvider => serviceProvider.GetRequiredService<LocalHostNamePublishingProvider>());
        services.AddInMemoryResourceModelGraph(
        [
            new ResourceGraphState(
                "graph-public",
                LoadBalancerResourceTypeProvider.ResourceTypeId,
                ResourceId: graphLoadBalancerResourceId,
                ProviderId: LoadBalancerResourceTypeProvider.ProviderId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                    [LoadBalancerResourceTypeProvider.Attributes.EntrypointCount] = 1,
                    [LoadBalancerResourceTypeProvider.Attributes.Entrypoints] =
                        ResourceAttributeValue.FromObject(
                            new[]
                            {
                                new LoadBalancerEntrypointValue(
                                    "http",
                                    "Http",
                                    80,
                                    "Public")
                            })
                }),
            new ResourceGraphState(
                "graph-cloudshell-local",
                DnsZoneResourceTypeProvider.ResourceTypeId,
                ResourceId: graphDnsZoneResourceId,
                ProviderId: DnsZoneResourceTypeProvider.ProviderId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "cloudshell.local",
                    [DnsZoneResourceTypeProvider.Attributes.Provider] =
                        LocalHostNamePublishingProvider.DefaultProviderName
                }),
            new ResourceGraphState(
                "graph-api-cloudshell-local",
                NameMappingResourceTypeProvider.ResourceTypeId,
                ResourceId: graphNameMappingResourceId,
                ProviderId: NameMappingResourceTypeProvider.ProviderId,
                DependsOn:
                [
                    ResourceReference.DependsOnResourceId(
                        graphDnsZoneResourceId,
                        DnsZoneResourceTypeProvider.ResourceTypeId),
                    ResourceReference.DependsOnResourceId(
                        graphLoadBalancerResourceId,
                        LoadBalancerResourceTypeProvider.ResourceTypeId)
                ],
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [NameMappingResourceTypeProvider.Attributes.HostName] = "api.cloudshell.local",
                    [NameMappingResourceTypeProvider.Attributes.TargetEndpointName] = "http",
                    [NameMappingResourceTypeProvider.Attributes.Exposure] = "Public"
                })
        ]);
        services
            .AddDockerHostResourceType()
            .AddContainerApplicationResourceType()
            .AddLoadBalancerResourceType()
            .AddDnsZoneResourceType()
            .AddNameMappingResourceType()
            .AddResourceModelGraphServices()
            .AddReferenceProviderResourceManagerProjections();
        services.Replace(
            ServiceDescriptor.Singleton<IDnsZoneNameMappingReconciler, LoadBalancerGraphNameMappingReconciler>());
        using var serviceProvider = services.BuildServiceProvider();

        var operationResolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveOperationAsync(
                graphDnsZoneResourceId,
                DnsZoneResourceTypeProvider.Operations.ReconcileNameMappings);

        Assert.False(operationResolution.HasErrors, FormatDiagnostics(operationResolution.Diagnostics));
        var operation = Assert.IsAssignableFrom<IResourceOperationExecutorProjection>(
            operationResolution.Operation);
        var execution = await operation.ExecuteAsync();

        Assert.False(execution.HasErrors, FormatDiagnostics(execution.Diagnostics));
        Assert.Contains(execution.Diagnostics, diagnostic =>
            diagnostic.Code == "loadBalancer.graphNameMappingsReconciled" &&
            diagnostic.Message.Contains("Published 1 local host name mapping", StringComparison.Ordinal));
        var hostsFile = await File.ReadAllTextAsync(hostsFilePath);
        Assert.Contains("127.0.0.1 api.cloudshell.local", hostsFile);
    }

    private static string FormatDiagnostics(
        IEnumerable<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(diagnostic =>
                $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}"));
}
