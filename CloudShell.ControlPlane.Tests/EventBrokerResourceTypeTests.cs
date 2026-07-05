using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Tests;

public sealed class EventBrokerResourceTypeTests
{
    [Fact]
    public void ResourceGraphBuilder_BuildsEventBrokerProtocolsAsAttributes()
    {
        var graph = new ResourceGraphBuilder();
        var broker = graph
            .AddEventBroker("events")
            .WithMqttEndpoint(
                "mqtt://localhost:1883",
                capabilities:
                [
                    EventBrokerProtocolCapabilities.PublishEvents,
                    EventBrokerProtocolCapabilities.SubscribeEvents,
                    EventBrokerProtocolCapabilities.TelemetryIngestion
                ])
            .WithHttpEndpoint("http://localhost:7180/events");

        var definition = graph
            .BuildTemplate("events")
            .Resources
            .Single(resource => resource.Name == broker.Name);
        var protocols = definition.ResourceAttributeValues.GetObject<EventBrokerProtocolEndpoint[]>(
            EventBrokerResourceTypeProvider.Attributes.Protocols);

        Assert.Equal(EventBrokerResourceTypeProvider.ResourceTypeId, definition.TypeId);
        Assert.NotNull(protocols);
        Assert.Collection(
            protocols!,
            protocol =>
            {
                Assert.Equal("mqtt", protocol.Name);
                Assert.Equal(EventBrokerProtocols.Mqtt, protocol.Protocol);
                Assert.Equal("mqtt://localhost:1883", protocol.Endpoint);
                Assert.Equal("json", protocol.EventFormat);
                Assert.Contains(EventBrokerProtocolCapabilities.TelemetryIngestion, protocol.Capabilities ?? []);
            },
            protocol =>
            {
                Assert.Equal("http", protocol.Name);
                Assert.Equal(EventBrokerProtocols.Http, protocol.Protocol);
                Assert.Equal("http://localhost:7180/events", protocol.Endpoint);
            });
    }

    [Fact]
    public void BuiltInProjections_ProjectEventBrokerProtocolEndpoints()
    {
        var services = new ServiceCollection();
        services.AddEventBrokerResourceType();
        services.AddBuiltInProviderResourceManagerProjections();

        using var serviceProvider = services.BuildServiceProvider();
        var resource = ResolveEventBroker();
        var endpointProjection = serviceProvider
            .GetServices<IResourceModelResourceManagerEndpointProjectionProvider>()
            .Select(provider => provider.GetEndpointProjection(resource))
            .SingleOrDefault(projection => projection is { Endpoints.Count: > 0 });

        Assert.NotNull(endpointProjection);
        Assert.Collection(
            endpointProjection.ResourceEndpoints,
            endpoint =>
            {
                Assert.Equal("mqtt", endpoint.Name);
                Assert.Equal(EventBrokerProtocols.Mqtt, endpoint.Protocol);
                Assert.Equal(1883, endpoint.TargetPort);
            },
            endpoint =>
            {
                Assert.Equal("http", endpoint.Name);
                Assert.Equal(EventBrokerProtocols.Http, endpoint.Protocol);
                Assert.Equal(7180, endpoint.TargetPort);
            });
        Assert.Collection(
            endpointProjection.ResourceEndpointNetworkMappings,
            mapping => Assert.Equal("mqtt://localhost:1883", mapping.Address),
            mapping => Assert.Equal("http://localhost:7180/events", mapping.Address));
    }

    private static ResourceModelResource ResolveEventBroker()
    {
        var state = new ResourceState(
            "events",
            EventBrokerResourceTypeProvider.ResourceTypeId,
            ResourceId: "event.broker:events",
            ProviderId: EventBrokerResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [EventBrokerResourceTypeProvider.Attributes.Protocols] =
                    ResourceAttributeValue.FromObject<EventBrokerProtocolEndpoint[]>(
                    [
                        new(
                            "mqtt",
                            EventBrokerProtocols.Mqtt,
                            "mqtt://localhost:1883",
                            "json",
                            [EventBrokerProtocolCapabilities.PublishEvents]),
                        new(
                            "http",
                            EventBrokerProtocols.Http,
                            "http://localhost:7180/events")
                    ])
            });
        var typeProvider = new EventBrokerResourceTypeProvider();
        var resolver = new ResourceResolver(
            [EventBrokerResourceTypeProvider.ClassDefinition],
            [typeProvider.TypeDefinition]);

        return resolver.Resolve(state);
    }
}
