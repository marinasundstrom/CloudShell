using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceOrchestratorDeploymentDefinitionTests
{
    [Fact]
    public void DeploymentDefinition_ProjectsServiceSpecAsGroupedReplicaResource()
    {
        var service = CreateService(replicas: 3);
        var spec = new ResourceOrchestratorDeploymentSpec(
            service,
            "rev-2",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deployment.reason"] = "image-update"
            });

        var definition = spec.DeploymentDefinition;

        Assert.Equal(
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            definition.DefinitionVersion);
        Assert.Empty(definition.DeploymentResources);
        var serviceDefinition = Assert.Single(definition.DeploymentServices);
        Assert.Equal(service.Name, serviceDefinition.Name);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.Service,
            serviceDefinition.Type);
        Assert.Equal("image-update", serviceDefinition.ServiceAttributes["deployment.reason"]);
        Assert.Equal(2, serviceDefinition.ServiceResources.Count);
        var replicaGroup = Assert.Single(serviceDefinition.ServiceResources, resource =>
            resource.Type == ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", replicaGroup.Name);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
            replicaGroup.Type);
        Assert.Equal("rev-2", replicaGroup.ResourceAttributes[ResourceAttributeNames.RuntimeRevision]);
        Assert.Equal("rev-2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentWorkloadVersion]);
        Assert.Equal("3", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal("3", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicas]);
        Assert.Equal(
            ResourceOrchestratorScaleOutRoutingMode.AfterAddedReplicas.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRoutingScaleOutMode]);
        Assert.Equal(
            ResourceOrchestratorScaleInRoutingMode.BeforeRemovedReplicas.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRoutingScaleInMode]);
        Assert.Equal(
            ResourceOrchestratorReplacementRoutingMode.AfterNewReplicaGroupMaterialized.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRoutingReplacementMode]);
        Assert.Equal(
            "0",
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplacementRetainPreviousReplicaSlots]);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.ReplaceOccupant.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaRestartMode]);
        Assert.Equal("1", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold]);
        Assert.Equal("10", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts]);
        Assert.True(ResourceOrchestratorReplicaGroupDefinition.TryFromResourceDefinition(
            serviceDefinition,
            replicaGroup,
            out var replicaGroupDefinition));
        Assert.NotNull(replicaGroupDefinition.Template);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.Replica,
            replicaGroupDefinition.Template.Type);
        Assert.Equal(
            "cloudshell-application-api-rev-2-replicas-replica-template",
            replicaGroupDefinition.Template.Name);
        Assert.Equal(
            replicaGroup.Name,
            replicaGroupDefinition.Template.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaGroupId]);
        Assert.Equal(
            "rev-2",
            replicaGroupDefinition.Template.ResourceAttributes[ResourceAttributeNames.RuntimeRevision]);
        var routingBinding = Assert.Single(serviceDefinition.RoutingBindingDefinitions);
        Assert.Equal("cloudshell-application-api-rev-2-replicas-http-routing", routingBinding.Name);
        Assert.Equal(service.Name, routingBinding.ServiceName);
        Assert.Equal(replicaGroup.Name, routingBinding.ReplicaGroupName);
        Assert.Equal(service.ResourceId, routingBinding.SourceEndpoint.ResourceId);
        Assert.Equal("http", routingBinding.SourceEndpoint.EndpointName);
    }

    [Fact]
    public void DeploymentDefinition_ProjectsReplicaManagementPolicy()
    {
        var service = CreateService(replicas: 2) with
        {
            ReplicaManagementPolicy = new ResourceOrchestratorReplicaManagementPolicy(
                ResourceOrchestratorReplicaRestartMode.RestartOccupant,
                FailureThreshold: 2,
                MaxAttempts: 4)
        };
        var spec = new ResourceOrchestratorDeploymentSpec(service, "rev-2");

        var serviceDefinition = Assert.Single(spec.DeploymentDefinition.DeploymentServices);
        var replicaGroup = Assert.Single(serviceDefinition.ServiceResources, resource =>
            resource.Type == ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup);

        Assert.Equal("2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.RestartOccupant.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaRestartMode]);
        Assert.Equal("2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold]);
        Assert.Equal("4", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts]);
    }

    [Fact]
    public void ServiceDefinition_ProjectsRoutingBindingDefinitions()
    {
        var service = CreateService(replicas: 3);
        var binding = new ResourceOrchestratorServiceRoutingBindingDefinition(
            "cloudshell-application-api-http-binding",
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            service.Name,
            "cloudshell-application-api-rev-2-replicas",
            ResourceEndpointReference.ForEndpoint(service.ResourceId, "http"),
            LoadBalancerResourceId: "cloudshell.loadBalancer:public",
            RouteId: "cloudshell.loadBalancer:public/routes/api",
            EndpointMappingId: "cloudshell.virtualNetwork:default/endpointMappings/api-http",
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["deployment.routing.strategy"] = "gradual"
            });
        var serviceDefinition = new ResourceOrchestratorServiceDefinition(
            service.Name,
            ResourceOrchestratorDeploymentDefinitionTypes.Service,
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            Resources:
            [
                binding.ToResourceDefinition()
            ]);

        var projected = Assert.Single(serviceDefinition.RoutingBindingDefinitions);

        Assert.Equal(binding.Name, projected.Name);
        Assert.Equal(service.Name, projected.ServiceName);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", projected.ReplicaGroupName);
        Assert.Equal(service.ResourceId, projected.SourceEndpoint.ResourceId);
        Assert.Equal("http", projected.SourceEndpoint.EndpointName);
        Assert.Equal("cloudshell.loadBalancer:public", projected.LoadBalancerResourceId);
        Assert.Equal("cloudshell.loadBalancer:public/routes/api", projected.RouteId);
        Assert.Equal("cloudshell.virtualNetwork:default/endpointMappings/api-http", projected.EndpointMappingId);
        Assert.Equal("gradual", projected.RoutingBindingAttributes["deployment.routing.strategy"]);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.ServiceRoutingBinding,
            Assert.Single(serviceDefinition.ServiceResources).Type);
        Assert.Equal(
            "cloudshell-application-api-rev-2-replicas",
            Assert.Single(serviceDefinition.ServiceResources)
                .ResourceAttributes[ResourceAttributeNames.DeploymentReplicaGroupId]);
    }

    [Fact]
    public void ServiceDefinition_IgnoresIncompleteRoutingBindingDefinitions()
    {
        var service = CreateService(replicas: 1);
        var serviceDefinition = new ResourceOrchestratorServiceDefinition(
            service.Name,
            ResourceOrchestratorDeploymentDefinitionTypes.Service,
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            Resources:
            [
                new ResourceOrchestratorResourceDefinition(
                    "cloudshell-application-api-http-binding",
                    ResourceOrchestratorDeploymentDefinitionTypes.ServiceRoutingBinding,
                    ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                    Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ResourceAttributeNames.DeploymentReplicaGroupId] =
                            "cloudshell-application-api-rev-2-replicas",
                        [ResourceAttributeNames.DeploymentRoutingSourceResourceId] =
                            service.ResourceId
                    })
            ]);

        Assert.Empty(serviceDefinition.RoutingBindingDefinitions);
    }

    [Fact]
    public void DeploymentDefinition_PreservesExplicitDefinitionStructure()
    {
        var service = CreateService(replicas: 1);
        var explicitDefinition = new ResourceOrchestratorDeploymentDefinition(
            "2026-06-23",
            Services:
            [
                new ResourceOrchestratorServiceDefinition(
                    "acme-api",
                    "cloudshell.service.http",
                    "1",
                    Resources:
                    [
                        new ResourceOrchestratorResourceDefinition(
                            "acme-api-replicas",
                            "cloudshell.replica-group",
                            "1")
                    ])
            ],
            Resources:
            [
                new ResourceOrchestratorResourceDefinition(
                    "shared-network",
                    "cloudshell.network",
                    "1")
            ]);
        var spec = new ResourceOrchestratorDeploymentSpec(
            service,
            "rev-2",
            Definition: explicitDefinition);

        Assert.Equal(explicitDefinition, spec.DeploymentDefinition);
        Assert.Equal("shared-network", Assert.Single(spec.DeploymentDefinition.DeploymentResources).Name);
        Assert.Equal(
            "acme-api-replicas",
            Assert.Single(Assert.Single(spec.DeploymentDefinition.DeploymentServices).ServiceResources).Name);
    }

    [Fact]
    public void CreateDeploymentDefinition_UsesRuntimeRevisionForGeneratedReplicaGroup()
    {
        var service = CreateService(replicas: 2) with
        {
            RuntimeRevisionId = null
        };
        var spec = new ResourceOrchestratorDeploymentSpec(service, "rev-2");

        var definition = spec.CreateDeploymentDefinition("rev-3");

        var serviceDefinition = Assert.Single(definition.DeploymentServices);
        var replicaGroup = Assert.Single(serviceDefinition.ServiceResources, resource =>
            resource.Type == ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup);
        Assert.Equal("cloudshell-application-api-rev-3-replicas", replicaGroup.Name);
        Assert.Equal("rev-3", replicaGroup.ResourceAttributes[ResourceAttributeNames.RuntimeRevision]);
        Assert.Equal("rev-2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentWorkloadVersion]);
    }

    private static ResourceOrchestratorService CreateService(int replicas) =>
        new(
            "application:api",
            "cloudshell-application-api",
            new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                "api",
                Image: "ghcr.io/example/api:2",
                Replicas: replicas,
                ReplicasEnabled: replicas > 1),
            Ports: [new ServicePort("http", 8080, Protocol: "http")],
            Networks: ["cloudshell"],
            RuntimeRevisionId: "rev-2");
}
