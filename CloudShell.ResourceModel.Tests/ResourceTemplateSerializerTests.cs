using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;

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
    image: api:latest
    replicas: 3
metadata:
  source: test
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(
            yaml,
            ResourceTemplateFormat.Yaml,
            new ResourceTemplateSerializerOptions(
                [new ContainerApplicationResourceTypeProvider().TypeDefinition]));

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
    public void DeserializeTemplate_YamlResolvesContainerAppAuthoredPaths()
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
    image: cloudshell-signalr-api:20260630.1
    replicas: 3
    routing:
      sessionAffinity:
        mode: Cookie
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(
            yaml,
            ResourceTemplateFormat.Yaml,
            new ResourceTemplateSerializerOptions(
                [new ContainerApplicationResourceTypeProvider().TypeDefinition]));

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
    public void DeserializeTemplate_YamlProjectsIdentityAndAccessGrantsAsResourceAttributes()
    {
        const string yaml = """
resources:
  - type: application.executable
    name: api
    identity:
      kind: provider
      providerId: identity:development
      name: api-service
      subject: application.executable:api
      provisionOnStartup: true
      scopes:
      - queue.publish
      claims:
        resource: application.executable:api
  - type: application.rabbitmq
    name: rabbitmq
    user:
      username: developer
      password: local-password
    vhost: my_vhost
    access:
      grants:
      - principal:
          kind: resourceIdentity
          id: application.executable:api/identities/api-service
          providerId: identity:development
          sourceResourceId: application.executable:api
          sourceIdentityName: api-service
        permission: CloudShell.Messaging/rabbitMQ/publish/action
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);

        var api = Assert.Single(template.Resources, resource => resource.Name == "api");
        var identity = api.GetIdentityAttribute();
        Assert.NotNull(identity);
        Assert.Equal("provider", identity.Kind);
        Assert.Equal("identity:development", identity.ProviderId);
        Assert.Equal("api-service", identity.Name);
        Assert.Equal("application.executable:api", identity.Subject);
        Assert.Equal(["queue.publish"], identity.Scopes);
        Assert.Equal("application.executable:api", identity.Claims?["resource"]);
        Assert.True(api.GetProvisionIdentityOnStartupAttribute());

        var rabbitmq = Assert.Single(template.Resources, resource => resource.Name == "rabbitmq");
        var grant = Assert.Single(rabbitmq.GetAccessGrantAttributes());
        Assert.Equal("resourceIdentity", grant.Principal.Kind);
        Assert.Equal("application.executable:api/identities/api-service", grant.Principal.Id);
        Assert.Equal("identity:development", grant.Principal.ProviderId);
        Assert.Equal("application.executable:api", grant.Principal.SourceResourceId);
        Assert.Equal("api-service", grant.Principal.SourceIdentityName);
        Assert.Equal("CloudShell.Messaging/rabbitMQ/publish/action", grant.Permission);
        Assert.Equal("developer", rabbitmq.ResourceAttributes["user.username"]);
        Assert.Equal("local-password", rabbitmq.ResourceAttributes["user.password"]);
        Assert.Equal("my_vhost", rabbitmq.ResourceAttributes["vhost"]);
    }

    [Fact]
    public void DeserializeTemplate_YamlPreservesVolumeConsumerCapabilityPayload()
    {
        const string yaml = """
resources:
  - type: application.rabbitmq
    name: rabbitmq
    storage:
      volumeConsumer:
        mounts:
        - volume: cloudshell.volume:rabbitmq-data
          targetPath: /var/lib/rabbitmq
          readOnly: false
""";

        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);
        var definition = Assert.Single(template.Resources);
        var directCapability = definition.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var resolver = new ResourceResolver(
            [RabbitMQResourceTypeProvider.ClassDefinition],
            [new RabbitMQResourceTypeProvider().TypeDefinition]);
        var resource = resolver.Resolve(definition);
        var resolvedCapability = resource.Capabilities.Get<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        var directMount = Assert.Single(directCapability!.Mounts);
        Assert.Equal("cloudshell.volume:rabbitmq-data", directMount.Volume);
        Assert.Equal("/var/lib/rabbitmq", directMount.TargetPath);
        var resolvedMount = Assert.Single(resolvedCapability!.Mounts);
        Assert.Equal("cloudshell.volume:rabbitmq-data", resolvedMount.Volume);
        Assert.Equal("/var/lib/rabbitmq", resolvedMount.TargetPath);
    }

    [Fact]
    public void ResourceTemplateApplyRequest_JsonRoundTripPreservesVolumeConsumerCapabilityPayload()
    {
        const string yaml = """
resources:
  - type: application.rabbitmq
    name: rabbitmq
    storage:
      volumeConsumer:
        mounts:
        - volume: cloudshell.volume:rabbitmq-data
          targetPath: /var/lib/rabbitmq
          readOnly: false
""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var template = ResourceTemplateSerializer.DeserializeTemplate(yaml);
        var request = new ResourceTemplateApplyRequest(template);

        var json = JsonSerializer.Serialize(request, options);
        var roundTripped = JsonSerializer.Deserialize<ResourceTemplateApplyRequest>(json, options);

        var definition = Assert.Single(roundTripped!.Template.Resources);
        var volumeConsumer = definition.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var mount = Assert.Single(volumeConsumer!.Mounts);
        Assert.Equal("cloudshell.volume:rabbitmq-data", mount.Volume);
        Assert.Equal("/var/lib/rabbitmq", mount.TargetPath);
    }

    [Fact]
    public void ResourceTemplateApplyRequest_JsonRoundTripPreservesApplicationArtifactSource()
    {
        var artifact = new ApplicationArtifactReference(
            "application.dotnet-app_api",
            "rev-1",
            "zip",
            new string('a', 64),
            1024,
            ".",
            "dotnetPublishedOutput");
        var definition = new ResourceDefinition(
            "api",
            ResourceTypeId.Create("application.dotnet-app"),
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ApplicationArtifactAttributeIds.Enabled] = true,
                [ApplicationArtifactAttributeIds.SourceKind] =
                    DeploymentArtifactSourceKinds.UploadedArtifact,
                [ApplicationArtifactAttributeIds.Source] =
                    ResourceAttributeValue.FromObject(artifact)
            });
        var request = new ResourceTemplateApplyRequest(
            new ResourceTemplate("local", [definition]));
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var json = JsonSerializer.Serialize(request, options);
        var roundTripped = JsonSerializer.Deserialize<ResourceTemplateApplyRequest>(json, options);

        var resource = Assert.Single(roundTripped!.Template.Resources);
        Assert.True(resource.ResourceAttributeValues.ContainsKey(ApplicationArtifactAttributeIds.Source));
        Assert.False(resource.ResourceAttributeValues.ContainsKey(
            ResourceAttributeId.Create("artifacts.source.artifactId")));

        var source = resource.ResourceAttributeValues[ApplicationArtifactAttributeIds.Source]
            .ToObject<ApplicationArtifactReference>()!;
        Assert.Equal(artifact.ArtifactId, source.ArtifactId);
        Assert.Equal(artifact.RevisionId, source.RevisionId);
        Assert.Equal(artifact.ArtifactLayoutKind, source.ArtifactLayoutKind);
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

        var options = new ResourceTemplateSerializerOptions(
            [new ContainerApplicationResourceTypeProvider().TypeDefinition]);
        var yaml = ResourceTemplateSerializer.SerializeDefinition(
            definition,
            ResourceTemplateFormat.Yaml,
            options);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(
            yaml,
            ResourceTemplateFormat.Yaml,
            options);

        Assert.Contains("type: application.container-app", yaml);
        Assert.Contains("image: api:latest", yaml);
        Assert.Contains("resourceId: application.database:db", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("container:", yaml);
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
    public void SerializeDefinition_YamlUsesContainerAppAuthoredPaths()
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

        var options = new ResourceTemplateSerializerOptions(
            [new ContainerApplicationResourceTypeProvider().TypeDefinition]);
        var yaml = ResourceTemplateSerializer.SerializeDefinition(
            definition,
            ResourceTemplateFormat.Yaml,
            options);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(
            yaml,
            ResourceTemplateFormat.Yaml,
            options);

        Assert.Contains("image: cloudshell-signalr-api:20260630.1", yaml);
        Assert.Contains("replicas: 3", yaml);
        Assert.Contains("routing:", yaml);
        Assert.Contains("sessionAffinity:", yaml);
        Assert.Contains("mode: Cookie", yaml);
        Assert.Contains("logs:", yaml);
        Assert.Contains("sources:", yaml);
        Assert.Contains("format: jsonConsole", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("container:", yaml);
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
    public void SerializeDefinition_YamlKeepsDotnetExecutablePathFlat()
    {
        var definition = new ResourceDefinition(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ExecutablePath] =
                    "artifacts/api/CloudShell.Api.dll",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    "--urls http://localhost:5080"
            });

        var yaml = ResourceTemplateSerializer.SerializeDefinition(definition);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(yaml);

        Assert.Contains("type: application.dotnet-app", yaml);
        Assert.Contains("executablePath: artifacts/api/CloudShell.Api.dll", yaml);
        Assert.Contains("project:", yaml);
        Assert.Contains("arguments: --urls http://localhost:5080", yaml);
        Assert.DoesNotContain("dotnet:", yaml);
        Assert.Equal(
            "artifacts/api/CloudShell.Api.dll",
            roundTripped.ResourceAttributes["executablePath"]);
        Assert.Equal(
            "--urls http://localhost:5080",
            roundTripped.ResourceAttributes["project.arguments"]);
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

        var options = new ResourceTemplateSerializerOptions(
            [new ContainerApplicationResourceTypeProvider().TypeDefinition]);
        var yaml = ResourceTemplateSerializer.SerializeDefinition(
            definition,
            ResourceTemplateFormat.Yaml,
            options);
        var roundTripped = ResourceTemplateSerializer.DeserializeDefinition(
            yaml,
            ResourceTemplateFormat.Yaml,
            options);

        Assert.Contains("endpoints:", yaml);
        Assert.Contains("network:", yaml);
        Assert.Contains("resourceId: network:host", yaml);
        Assert.DoesNotContain("endpointRequests:", yaml);
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
      "endpoint": "http://localhost:5101/api/configuration/stores/typescript-app-settings/settings"
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
            "value": "http://localhost:5101/api/configuration/stores/typescript-app-settings/settings"
          },
          "Sample__Message": {
            "configurationSettingRef": {
              "storeResourceId": "configuration.store:typescript-app-settings",
              "name": "Sample--Message"
            }
          }
        }
      },
      "endpoints": [
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
      ],
      "runtime": "node",
      "packageManager": "npm",
      "script": "dev",
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
            ResourceTemplateFormat.Json,
            new ResourceTemplateSerializerOptions(
                [new JavaScriptAppResourceTypeProvider().TypeDefinition]));

        Assert.Equal("typescript-hosting-poc", template.Name);
        Assert.Equal("typescript", template.Metadata?["cloudshell.source"]);
        Assert.Equal(3, template.Resources.Count);

        var settings = Assert.Single(template.Resources, resource =>
            resource.Name == "typescript-app-settings");
        Assert.Equal(
            "http://localhost:5101/api/configuration/stores/typescript-app-settings/settings",
            settings.ResourceAttributes["endpoint"]);

        var frontend = Assert.Single(template.Resources, resource =>
            resource.Name == "typescript-frontend");
        Assert.Equal(ResourceTypeId.Create("application.javascript-app"), frontend.TypeId);
        Assert.Equal("samples/JavaScriptApp/App", frontend.ResourceAttributes["javascript-app:project.path"]);
        Assert.Equal("node", frontend.ResourceAttributes["javascript-app:runtime"]);

        var references = frontend.ResourceAttributeValues[
            ResourceAttributeId.Create("javascript-app:project.references")].ArrayValue ?? [];
        var reference = Assert.Single(references);
        Assert.True(reference.TryGetResourceReference(out var resourceReference));
        Assert.True(resourceReference.TryGetResourceId(out var referencedResourceId));
        Assert.Equal("configuration.store:typescript-app-settings", referencedResourceId);

        var endpoint = Assert.Single(frontend.ResourceAttributeValues[
            ResourceAttributeId.Create("javascript-app:project.endpointRequests")].ArrayValue ?? []);
        Assert.Equal("http", endpoint.ObjectValue!["name"].StringValue);
        Assert.True(endpoint.ObjectValue["network"].TryGetResourceReference(out var networkReference));
        Assert.True(networkReference.TryGetResourceId(out var networkResourceId));
        Assert.Equal("network:host", networkResourceId);

        var healthChecks = frontend.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        Assert.Equal("health", Assert.Single(healthChecks!.Checks ?? []).Name);
    }

    [Fact]
    public void SerializeTemplate_UsesResourceAttributePathsWhenProvided()
    {
        var template = new ResourceTemplate(
            "javascript-app",
            [
                new(
                    "frontend",
                    ResourceTypeId.Create("application.javascript-app"),
                    Attributes: new ResourceAttributeValueMap(new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        ["javascript-app:project.path"] = "samples/JavaScriptApp/App",
                        ["javascript-app:runtime"] = "node",
                        ["javascript-app:packageManager"] = "npm",
                        ["javascript-app:script"] = "dev"
                    }))
            ]);
        var options = new ResourceTemplateSerializerOptions(
            [
                new(
                    ResourceTypeId.Create("application.javascript-app"),
                    ResourceClassId.Create("project"),
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["javascript-app:project.path"] = new(Path: "project.path"),
                        ["javascript-app:runtime"] = new(Path: "runtime"),
                        ["javascript-app:packageManager"] = new(Path: "packageManager"),
                        ["javascript-app:script"] = new(Path: "script")
                    })
            ]);

        var yaml = ResourceTemplateSerializer.SerializeTemplate(
            template,
            ResourceTemplateFormat.Yaml,
            options);
        var roundTripped = ResourceTemplateSerializer.DeserializeTemplate(
            yaml,
            ResourceTemplateFormat.Yaml,
            options);
        var resource = Assert.Single(roundTripped.Resources);

        Assert.Contains("project:", yaml);
        Assert.Contains("path: samples/JavaScriptApp/App", yaml);
        Assert.Contains("runtime: node", yaml);
        Assert.DoesNotContain("javascript-app:runtime", yaml);
        Assert.Equal("samples/JavaScriptApp/App", resource.ResourceAttributes["javascript-app:project.path"]);
        Assert.Equal("node", resource.ResourceAttributes["javascript-app:runtime"]);
        Assert.Equal("npm", resource.ResourceAttributes["javascript-app:packageManager"]);
        Assert.Equal("dev", resource.ResourceAttributes["javascript-app:script"]);
    }

    [Fact]
    public void SerializeDefinition_OmitsEmptyStateSections()
    {
        var definition = new ResourceState(
            "api",
            ResourceTypeId.Create("application.container-app"),
            DependsOn: [],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>(),
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
        Assert.DoesNotContain("capabilities:", yaml);
        Assert.DoesNotContain("operations:", yaml);
        Assert.DoesNotContain("metadata:", yaml);
        Assert.DoesNotContain("\"dependsOn\"", json);
        Assert.DoesNotContain("\"attributes\"", json);
        Assert.DoesNotContain("\"capabilities\"", json);
        Assert.DoesNotContain("\"operations\"", json);
        Assert.DoesNotContain("\"metadata\"", json);
    }
}
