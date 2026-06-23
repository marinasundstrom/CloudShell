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
        var replicaGroup = Assert.Single(serviceDefinition.ServiceResources);
        Assert.Equal("cloudshell-application-api-rev-2-replicas", replicaGroup.Name);
        Assert.Equal(
            ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
            replicaGroup.Type);
        Assert.Equal("rev-2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentWorkloadVersion]);
        Assert.Equal("3", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal("3", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicas]);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.ReplaceOccupant.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaRestartMode]);
        Assert.Equal("1", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold]);
        Assert.Equal("10", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts]);
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

        var replicaGroup = Assert.Single(
            Assert.Single(spec.DeploymentDefinition.DeploymentServices).ServiceResources);

        Assert.Equal("2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentRequestedReplicaSlots]);
        Assert.Equal(
            ResourceOrchestratorReplicaRestartMode.RestartOccupant.ToString(),
            replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaRestartMode]);
        Assert.Equal("2", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaFailureThreshold]);
        Assert.Equal("4", replicaGroup.ResourceAttributes[ResourceAttributeNames.DeploymentReplicaMaxAttempts]);
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
        var replicaGroup = Assert.Single(serviceDefinition.ServiceResources);
        Assert.Equal("cloudshell-application-api-rev-3-replicas", replicaGroup.Name);
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
