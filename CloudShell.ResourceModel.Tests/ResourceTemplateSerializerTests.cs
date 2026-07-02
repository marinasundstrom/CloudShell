using System.Text.Json;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceTemplateSerializerTests
{
    [Fact]
    public void DeserializeTemplate_YamlUsesResourceDefinitionTypeAliasAndDefaultName()
    {
        const string yaml = """
resources:
  - type: application.container-app
    name: api
    displayName: API
    container:
      image: api:latest
      replicas: 3
metadata:
  source: test
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        Assert.Equal("local", template.Name);
        Assert.Equal("test", template.Metadata?["source"]);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("api", resource.Name);
        Assert.Equal(ResourceTypeId.Create("application.container-app"), resource.TypeId);
        Assert.Equal("API", resource.DisplayName);
        Assert.True(resource.ResourceAttributeValues.TryGetValue(
            ResourceAttributeId.Create("container.image"),
            out var image));
        Assert.Equal("api:latest", image.StringValue);
        Assert.True(resource.ResourceAttributeValues.TryGetValue(
            ResourceAttributeId.Create("container.replicas"),
            out var replicas));
        Assert.Equal(3, replicas.IntegerValue);
    }

    [Fact]
    public void DeserializeTemplate_YamlFlattensNestedAttributeGroups()
    {
        const string yaml = """
resources:
  - type: application.container-app
    name: api
    logs:
      sources:
      - id: console
        name: Console logs
        kind: processOutput
        format: jsonConsole
        capabilities:
        - read
        - stream
        description: Provider-captured process console output.
        origin: providerDefault
        purpose: default
        availability: resourceRunning
    container:
      image: cloudshell-signalr-api:20260630.1
      replicas: 3
      routing:
        sessionAffinity:
          mode: Cookie
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("cloudshell-signalr-api:20260630.1", resource.ResourceAttributes["container.image"]);
        Assert.Equal("3", resource.ResourceAttributes["container.replicas"]);
        Assert.Equal("Cookie", resource.ResourceAttributes["container.routing.sessionAffinity.mode"]);

        var logSources = resource.GetCapability<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources);
        var logSource = Assert.Single(logSources!.Sources ?? []);
        Assert.Equal("console", logSource.Id);
        Assert.Equal(ResourceLogSourceDefinitionValues.JsonConsole, logSource.Format);
    }

    [Fact]
    public void DeserializeTemplate_YamlStillSupportsAttributesWrapper()
    {
        const string yaml = """
resources:
  - type: application.container-app
    name: api
    attributes:
      container:
        image: api:latest
      container.replicas: 3
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("api:latest", resource.ResourceAttributes["container.image"]);
        Assert.Equal("3", resource.ResourceAttributes["container.replicas"]);
    }

    [Fact]
    public void SerializeAndDeserializeDefinition_YamlUsesTypeAliasWithoutRenamingReferences()
    {
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("application.container-app"),
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    "application.database:db",
                    ResourceTypeId.Create("application.database"))
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("container.image")] = "api:latest"
            });

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(yaml);

        Assert.Contains("type: application.container-app", yaml);
        Assert.Contains("container:", yaml);
        Assert.Contains("image: api:latest", yaml);
        Assert.Contains("resourceId: application.database:db", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("container.image:", yaml);
        Assert.DoesNotContain("typeId: application.container-app", yaml);
        Assert.DoesNotContain("typeId: application.database", yaml);
        Assert.DoesNotContain("value: application.database:db", yaml);
        Assert.DoesNotContain("relationship: dependsOn", yaml);
        Assert.DoesNotContain("addressingMode: resourceId", yaml);
        Assert.DoesNotContain("effectiveResourceId:", yaml);
        Assert.DoesNotContain("startupDependencies:", yaml);
        Assert.DoesNotContain("resourceAttributes:", yaml);
        Assert.DoesNotContain("resourceAttributeValues:", yaml);
        Assert.DoesNotContain("capabilityPayloads:", yaml);
        Assert.Equal(definition.Name, roundTripped.Name);
        Assert.Equal(definition.TypeId, roundTripped.TypeId);

        var dependency = Assert.Single(roundTripped.StartupDependencies);
        Assert.Null(dependency.TypeId);
    }

    [Fact]
    public void DeserializeTemplate_YamlSupportsCompactDependsOnReferences()
    {
        const string yaml = """
resources:
  - type: application.container-app
    name: api
    dependsOn:
    - resourceId: cloudshell.container-host:default
    attributes:
      container:
        image: api:latest
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        var resource = Assert.Single(template.Resources);
        var dependency = Assert.Single(resource.StartupDependencies);

        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyResourceId));
        Assert.Equal("cloudshell.container-host:default", dependencyResourceId);
        Assert.Equal(ResourceReferenceRelationships.DependsOn, dependency.Relationship);
        Assert.Equal(ResourceReferenceAddressingModes.ResourceId, dependency.AddressingMode);
        Assert.Null(dependency.TypeId);
        Assert.Null(dependency.ProviderId);
    }

    [Fact]
    public void SerializeDefinition_YamlGroupsDottedAttributes()
    {
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("application.container-app"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("container.image")] = "cloudshell-signalr-api:20260630.1",
                [ResourceAttributeId.Create("container.replicas")] = 3,
                [ResourceAttributeId.Create("container.routing.sessionAffinity.mode")] = "Cookie",
                [ResourceLogSourceAttributeIds.LogSources] = ResourceAttributeValue.FromObject(
                    ResourceLogSourceDefinitionSet.DefaultConsole(
                        ResourceLogSourceDefinitionValues.JsonConsole))
            });

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(yaml);

        Assert.Contains("container:", yaml);
        Assert.Contains("image: cloudshell-signalr-api:20260630.1", yaml);
        Assert.Contains("replicas: 3", yaml);
        Assert.Contains("routing:", yaml);
        Assert.Contains("sessionAffinity:", yaml);
        Assert.Contains("mode: Cookie", yaml);
        Assert.Contains("logs:", yaml);
        Assert.Contains("sources:", yaml);
        Assert.Contains("format: jsonConsole", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("container.image:", yaml);
        Assert.DoesNotContain("logs.sources:", yaml);
        Assert.Equal(
            "cloudshell-signalr-api:20260630.1",
            roundTripped.ResourceAttributes["container.image"]);
        Assert.Equal("3", roundTripped.ResourceAttributes["container.replicas"]);
        Assert.NotNull(roundTripped.GetCapability<ResourceLogSourceDefinitionSet>(
            ResourceLogSourceCapabilityIds.LogSources));
    }

    [Fact]
    public void SerializeDefinition_YamlCompactsEmbeddedResourceIdReferences()
    {
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("application.container-app"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ResourceAttributeId.Create("container.endpointRequests")] = ResourceAttributeValue.FromObject(
                    new[]
                    {
                        new
                        {
                            name = "http",
                            protocol = "http",
                            network = ResourceReference.ReferenceResourceId(
                                "network:host",
                                ResourceTypeId.Create("cloudshell.network"),
                                "cloudshell.network")
                        }
                    })
            });

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(yaml);

        Assert.Contains("network:", yaml);
        Assert.Contains("resourceId: network:host", yaml);
        Assert.DoesNotContain("value: network:host", yaml);
        Assert.DoesNotContain("relationship: reference", yaml);
        Assert.DoesNotContain("addressingMode: resourceId", yaml);
        Assert.DoesNotContain("typeId: cloudshell.network", yaml);
        Assert.DoesNotContain("providerId: cloudshell.network", yaml);

        var endpoint = Assert.Single(roundTripped.ResourceAttributeValues[
            ResourceAttributeId.Create("container.endpointRequests")].ArrayValue ?? []);
        var network = endpoint.ObjectValue!["network"];

        Assert.True(network.TryGetResourceReference(out var reference));
        Assert.True(reference.TryGetResourceId(out var resourceId));
        Assert.Equal("network:host", resourceId);
        Assert.Equal(ResourceReferenceRelationships.Reference, reference.Relationship);
        Assert.Null(reference.TypeId);
        Assert.Null(reference.ProviderId);
    }

    [Fact]
    public void DeserializeTemplate_JsonAcceptsTypeIdAndSerializesDocumentShape()
    {
        const string json = """
{
  "name": "local-app",
  "resources": [
    {
      "name": "api",
      "typeId": "application.container-app",
      "attributes": {
        "container.image": "api:latest"
      }
    }
  ]
}
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(
            json,
            ResourceTemplateFormat.Json);
        var serialized = ResourceTemplateSerializer.SerializeTemplate(
            template,
            ResourceTemplateFormat.Json);

        var resource = Assert.Single(template.Resources);
        Assert.Equal("local-app", template.Name);
        Assert.Equal(ResourceTypeId.Create("application.container-app"), resource.TypeId);
        Assert.Equal("api:latest", resource.ResourceAttributes["container.image"]);
        Assert.Contains("\"type\": \"application.container-app\"", serialized);
        Assert.Contains("\"container\": {", serialized);
        Assert.Contains("\"image\": \"api:latest\"", serialized);
        Assert.DoesNotContain("\"typeId\": \"application.container-app\"", serialized);
        Assert.DoesNotContain("\"attributes\"", serialized);
        Assert.DoesNotContain("\"container.image\"", serialized);
        Assert.DoesNotContain("effectiveResourceId", serialized);
        Assert.DoesNotContain("startupDependencies", serialized);
        Assert.DoesNotContain("resourceAttributes", serialized);
        Assert.DoesNotContain("resourceAttributeValues", serialized);
        Assert.DoesNotContain("capabilityPayloads", serialized);
    }

    [Fact]
    public void DeserializeTemplate_JsonAcceptsTypeScriptHostingPocShape()
    {
        const string json = """
{
  "name": "typescript-hosting-poc",
  "resources": [
    {
      "name": "typescript-app-settings",
      "type": "configuration.store",
      "resourceId": "configuration.store:typescript-app-settings",
      "providerId": "configuration",
      "displayName": "TypeScript App Settings",
      "attributes": {
        "configuration": {
          "endpoint": "http://localhost:5101/api/configuration/stores/typescript-app-settings/entries"
        }
      }
    },
    {
      "name": "typescript-frontend",
      "type": "application.javascript-app",
      "resourceId": "application.javascript-app:typescript-frontend",
      "providerId": "applications.javascript-app",
      "displayName": "TypeScript-declared Frontend",
      "project": {
        "path": "samples/JavaScriptApp/App",
        "serviceDiscoveryName": "typescript-frontend",
        "references": [
          {
            "resourceId": "configuration.store:typescript-app-settings",
            "relationship": "reference",
            "addressingMode": "resourceId",
            "typeId": "configuration.store",
            "providerId": "configuration"
          }
        ],
        "environmentVariables": {
          "CLOUDSHELL_SETTINGS_ENDPOINT": {
            "value": "http://localhost:5101/api/configuration/stores/typescript-app-settings/entries"
          },
          "Sample__Message": {
            "configurationEntryRef": {
              "storeResourceId": "configuration.store:typescript-app-settings",
              "name": "Sample--Message"
            }
          }
        },
        "endpointRequests": [
          {
            "name": "http",
            "protocol": "http",
            "targetPort": 5173,
            "host": "localhost",
            "port": 5173,
            "exposure": "Local",
            "network": {
              "resourceId": "network:host",
              "relationship": "reference",
              "addressingMode": "resourceId",
              "typeId": "cloudshell.network",
              "providerId": "cloudshell.network"
            }
          }
        ]
      },
      "javascript": {
        "engine": "node",
        "packageManager": "npm",
        "script": "dev"
      },
      "health": {
        "checks": [
          {
            "name": "health",
            "type": "health",
            "source": {
              "kind": "http",
              "http": {
                "path": "/healthz",
                "endpointName": "http"
              }
            }
          }
        ]
      }
    },
    {
      "name": "host",
      "type": "cloudshell.network",
      "resourceId": "network:host",
      "providerId": "cloudshell.network",
      "displayName": "Host network",
      "network": {
        "kind": "Host",
        "hostReadiness": "hostReady"
      }
    }
  ],
  "metadata": {
    "cloudshell.source": "typescript",
    "cloudshell.sample": "TypeScriptAppHost"
  }
}
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(
            json,
            ResourceTemplateFormat.Json);

        Assert.Equal("typescript-hosting-poc", template.Name);
        Assert.Equal("typescript", template.Metadata?["cloudshell.source"]);
        Assert.Equal(3, template.Resources.Count);

        var settings = Assert.Single(template.Resources, resource =>
            resource.Name == "typescript-app-settings");
        Assert.Equal(
            "http://localhost:5101/api/configuration/stores/typescript-app-settings/entries",
            settings.ResourceAttributes["configuration.endpoint"]);

        var frontend = Assert.Single(template.Resources, resource =>
            resource.Name == "typescript-frontend");
        Assert.Equal(ResourceTypeId.Create("application.javascript-app"), frontend.TypeId);
        Assert.Equal("samples/JavaScriptApp/App", frontend.ResourceAttributes["project.path"]);
        Assert.Equal("node", frontend.ResourceAttributes["javascript.engine"]);

        var references = frontend.ResourceAttributeValues[
            ResourceAttributeId.Create("project.references")].ArrayValue ?? [];
        var reference = Assert.Single(references);
        Assert.True(reference.TryGetResourceReference(out var resourceReference));
        Assert.True(resourceReference.TryGetResourceId(out var referencedResourceId));
        Assert.Equal("configuration.store:typescript-app-settings", referencedResourceId);

        var endpoint = Assert.Single(frontend.ResourceAttributeValues[
            ResourceAttributeId.Create("project.endpointRequests")].ArrayValue ?? []);
        Assert.Equal("http", endpoint.ObjectValue!["name"].StringValue);
        Assert.True(endpoint.ObjectValue["network"].TryGetResourceReference(out var networkReference));
        Assert.True(networkReference.TryGetResourceId(out var networkResourceId));
        Assert.Equal("network:host", networkResourceId);

        var healthChecks = frontend.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        Assert.Equal("health", Assert.Single(healthChecks!.Checks ?? []).Name);
    }

    [Fact]
    public void SerializeDefinition_OmitsEmptyStateSections()
    {
        var definition = new ResourceState(
            "api",
            ResourceTypeId.Create("application.container-app"),
            DependsOn: [],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>(),
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase),
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>(),
            Operations: new Dictionary<ResourceOperationId, JsonElement>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            .ToDefinition();

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var json = ResourceTemplateSerializer.SerializeDefinition(
            definition,
            ResourceTemplateFormat.Json);

        Assert.DoesNotContain("dependsOn:", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("configuration:", yaml);
        Assert.DoesNotContain("capabilities:", yaml);
        Assert.DoesNotContain("operations:", yaml);
        Assert.DoesNotContain("metadata:", yaml);
        Assert.DoesNotContain("\"dependsOn\"", json);
        Assert.DoesNotContain("\"attributes\"", json);
        Assert.DoesNotContain("\"configuration\"", json);
        Assert.DoesNotContain("\"capabilities\"", json);
        Assert.DoesNotContain("\"operations\"", json);
        Assert.DoesNotContain("\"metadata\"", json);
    }
}
