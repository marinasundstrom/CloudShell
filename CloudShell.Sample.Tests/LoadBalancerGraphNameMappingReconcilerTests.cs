using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.LoadBalancer;
using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using CloudShell.ResourceModel.ReferenceProviders.ResourceManager;
using CloudShell.ResourceModel.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;

namespace CloudShell.Sample.Tests;

public sealed class LoadBalancerGraphNameMappingReconcilerTests
{
    [Fact]
    public async Task ReconcileNameMappings_PublishesGraphMappingsThroughLocalHostNamesProvider()
    {
        const string loadBalancerResourceId = "cloudshell.loadBalancer:public";
        const string dnsZoneResourceId = "cloudshell.dnsZone:cloudshell-local";
        const string nameMappingResourceId = "cloudshell.nameMapping:api-cloudshell-local";
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
                "public",
                LoadBalancerResourceTypeProvider.ResourceTypeId,
                ResourceId: loadBalancerResourceId,
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
                "cloudshell-local",
                DnsZoneResourceTypeProvider.ResourceTypeId,
                ResourceId: dnsZoneResourceId,
                ProviderId: DnsZoneResourceTypeProvider.ProviderId,
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [DnsZoneResourceTypeProvider.Attributes.ZoneName] = "cloudshell.local",
                    [DnsZoneResourceTypeProvider.Attributes.Provider] =
                        LocalHostNamePublishingProvider.DefaultProviderName
                }),
            new ResourceGraphState(
                "api-cloudshell-local",
                NameMappingResourceTypeProvider.ResourceTypeId,
                ResourceId: nameMappingResourceId,
                ProviderId: NameMappingResourceTypeProvider.ProviderId,
                DependsOn:
                [
                    ResourceReference.BelongsToResourceId(
                        dnsZoneResourceId,
                        DnsZoneResourceTypeProvider.ResourceTypeId),
                    ResourceReference.ReferenceResourceId(
                        loadBalancerResourceId,
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
                dnsZoneResourceId,
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
