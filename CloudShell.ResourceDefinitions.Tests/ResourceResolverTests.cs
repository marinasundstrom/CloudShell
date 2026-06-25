using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceResolverTests
{
    [Fact]
    public void Resolve_MergesClassTypeAndResourceDefinitionValues()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
                    },
                    Capabilities:
                    [
                        new("logs.sources")
                    ],
                    Operations:
                    [
                        new("start", ResourceDefinitionJson.FromValue(new { policy = "class-default" }))
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    DefaultProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] =
                            new(DefaultValue: "dotnet")
                    },
                    Capabilities:
                    [
                        new("monitoring")
                    ],
                    Operations:
                    [
                        new("restart")
                    ])
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityId] = ResourceDefinitionJson.FromValue(new
                {
                    mounts = new[]
                    {
                        new { volume = "volume:data", targetPath = "App_Data" }
                    }
                })
            });

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, resolved.Class.ClassId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, resolved.Type.TypeId);
        Assert.Equal("executable", resolved.Attributes.GetString("workload.kind"));
        Assert.Equal(
            "./api",
            resolved.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal(
            ResourceDefinitionValueSource.ResourceState,
            resolved.Attributes.Resolve(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath)?.Source);
        Assert.True(resolved.Capabilities.Has("logs.sources"));
        Assert.True(resolved.Capabilities.Has("monitoring"));
        Assert.True(resolved.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityId));
        Assert.True(resolved.Operations.Has("start"));
        Assert.True(resolved.Operations.Has("restart"));
    }

    [Fact]
    public void Resolve_UsesAttributeDefinitionMapForDefaultsAndRequirements()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
                    })
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = new(
                            DefaultValue: "dotnet",
                            Required: true,
                            RequiredMessage: "Executable path is required.")
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        Assert.Equal("executable", resolved.Attributes.GetString("workload.kind"));
        Assert.Equal(
            "dotnet",
            resolved.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal(
            ResourceDefinitionValueSource.TypeDefinition,
            resolved.Attributes.Resolve(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath)?.Source);
    }

    [Fact]
    public void Resolve_ReportsRequiredAttributeDefinitionDiagnostics()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = new(
                            Required: true,
                            RequiredMessage: "Executable path is required.")
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing, diagnostic.Code);
        Assert.Equal("Executable path is required.", diagnostic.Message);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public void Resolve_KeepsDefinedAttributeUnsetWhenNoValueOrDefaultExists()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["runtime.status"] = new(
                            ValueType: ResourceAttributeValueType.String)
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);
        var attribute = resolved.Attributes.Resolve("runtime.status");

        Assert.NotNull(attribute);
        Assert.True(attribute.IsDefined);
        Assert.False(attribute.IsSet);
        Assert.Null(attribute.Value);
        Assert.Null(resolved.Attributes.GetString("runtime.status"));
        Assert.Empty(resolved.Diagnostics);
    }

    [Fact]
    public void Resolve_MarksCustomResourceStateAttributeAsUndefined()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId)
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["annotations.owner"] = "platform"
            });

        var resolved = resolver.Resolve(definition);
        var attribute = resolved.Attributes.Resolve("annotations.owner");

        Assert.NotNull(attribute);
        Assert.False(attribute.IsDefined);
        Assert.True(attribute.IsSet);
        Assert.Equal("platform", attribute.Value);
        Assert.Equal("platform", resolved.Attributes.GetString("annotations.owner"));
        Assert.Empty(resolved.Diagnostics);
    }

    [Fact]
    public void Resolve_ValidatesComplexResourceAttributeValueAgainstShape()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["runtime.healthChecks"] = new(
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: "runtime.healthCheck",
                            IsCollection: true)
                    },
                    AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
                    {
                        ["runtime.healthCheck"] = new(
                            new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["name"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true),
                                    ["protocol"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true),
                                    ["targetPort"] = new(
                                        ValueType: ResourceAttributeValueType.Integer)
                                }))
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["runtime.healthChecks"] = ResourceAttributeValue.Array(
                    [
                        ResourceAttributeValue.Object(
                            new Dictionary<string, ResourceAttributeValue>
                            {
                                ["name"] = "http",
                                ["protocol"] = "http",
                                ["targetPort"] = 8080
                            })
                    ])
            });

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        var healthChecks = resolved.Attributes.GetValue("runtime.healthChecks");
        Assert.NotNull(healthChecks);
        Assert.Equal(ResourceAttributeValueKind.Array, healthChecks.Kind);
    }

    [Fact]
    public void Resolve_ReportsComplexResourceAttributeValueShapeDiagnostics()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["endpoints"] = new(
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShape: new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["name"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true),
                                    ["targetPort"] = new(
                                        ValueType: ResourceAttributeValueType.Integer,
                                        Required: true)
                                }),
                            IsCollection: true)
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["endpoints"] = ResourceAttributeValue.Array(
                    [
                        ResourceAttributeValue.Object(
                            new Dictionary<string, ResourceAttributeValue>
                            {
                                ["name"] = "http",
                                ["targetPort"] = ResourceAttributeValue.Object(
                                    new Dictionary<string, ResourceAttributeValue>())
                            })
                    ])
            });

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeValueInvalid, diagnostic.Code);
        Assert.Equal("endpoints", diagnostic.Target);
        Assert.Contains("endpoints[0].targetPort", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ValidatesEndpointMappingAttributeValuesAgainstRuntimeProvidedShapes()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["endpoints"] = new(
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: NetworkingEndpointShapeIds.Endpoint,
                            IsCollection: true),
                        ["endpointMappings"] = new(
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: NetworkingEndpointShapeIds.EndpointMapping,
                            IsCollection: true)
                    })
            ],
            attributeValueShapeProviders: [new NetworkingEndpointShapeProvider()]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["endpoints"] = ResourceAttributeValue.FromObject(
                    new[]
                    {
                        new NetworkingEndpointValue(
                            "http",
                            "http",
                            TargetPort: 8080,
                            Exposure: "Local")
                    }),
                ["endpointMappings"] = ResourceAttributeValue.FromObject(
                    new[]
                    {
                        new NetworkingEndpointMappingValue(
                            new NetworkingEndpointReferenceValue(
                                ResourceReference.DependsOnResourceId(
                                    "cloudshell.network:default",
                                    typeId: "cloudshell.network"),
                                "public-http"),
                            new NetworkingEndpointReferenceValue(
                                ResourceReference.DependsOnResourceId(
                                    "application.executable:api",
                                    typeId: ExecutableApplicationResourceTypeProvider.ResourceTypeId),
                                "http"),
                            Id: "api-http",
                            Name: "api-http",
                            Network: ResourceReference.DependsOnResourceId(
                                "cloudshell.network:default",
                                typeId: "cloudshell.network"))
                    })
            });

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        var projected = Assert.Single(resolved.Attributes.GetObject<NetworkingEndpointMappingValue[]>("endpointMappings")!);
        Assert.Equal("public-http", projected.Source.EndpointName);
        Assert.True(projected.Target.Resource.TryGetDependsOnResourceId(out var targetResourceId));
        Assert.Equal("application.executable:api", targetResourceId);
    }

    [Fact]
    public void Resolve_ReportsEndpointMappingRuntimeProvidedShapeDiagnostics()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["endpointMappings"] = new(
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: NetworkingEndpointShapeIds.EndpointMapping,
                            IsCollection: true)
                    })
            ],
            attributeValueShapeProviders: [new NetworkingEndpointShapeProvider()]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                ["endpointMappings"] = ResourceAttributeValue.Array(
                    [
                        ResourceAttributeValue.Object(
                            new Dictionary<string, ResourceAttributeValue>
                            {
                                ["source"] = ResourceAttributeValue.FromObject(
                                    new NetworkingEndpointReferenceValue(
                                        ResourceReference.DependsOnResourceId(
                                            "cloudshell.network:default",
                                            typeId: "cloudshell.network"),
                                        "public-http"))
                            })
                    ])
            });

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeValueInvalid, diagnostic.Code);
        Assert.Equal("endpointMappings.target", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsReadOnlyAttributeDeclaredInResourceDefinition()
    {
        var resolver = new ResourceResolver(
            [
                new("container")
            ],
            [
                new(
                    "docker.container",
                    "container",
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["endpoints.count"] = new(
                            ValueType: ResourceAttributeValueType.Integer,
                            ReadOnly: true,
                            Mutability: ResourceAttributeMutability.ProviderManaged)
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            "docker.container",
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["endpoints.count"] = "2"
            });

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange, diagnostic.Code);
        Assert.Equal("endpoints.count", diagnostic.Target);
    }

    [Fact]
    public void Resolve_AllowsReadOnlyAttributeInResourceState()
    {
        var resolver = new ResourceResolver(
            [
                new("container")
            ],
            [
                new(
                    "docker.container",
                    "container",
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["endpoints.count"] = new(
                            ValueType: ResourceAttributeValueType.Integer,
                            ReadOnly: true,
                            Mutability: ResourceAttributeMutability.ProviderManaged)
                    })
            ]);
        var state = new ResourceState(
            "api",
            "docker.container",
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                ["endpoints.count"] = "2"
            });

        var resolved = resolver.Resolve(state);

        Assert.Empty(resolved.Diagnostics);
        Assert.Equal("2", resolved.Attributes.GetString("endpoints.count"));
        Assert.True(resolved.Attributes.Resolve("endpoints.count")?.ReadOnly);
        Assert.Equal(
            ResourceAttributeMutability.ProviderManaged,
            resolved.Attributes.Resolve("endpoints.count")?.Mutability);
    }

    [Fact]
    public void Resolve_ReportsInvalidAttributeDefinitionDefaultKind()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["container:replicas"] = new(
                            DefaultValue: "one",
                            ValueType: ResourceAttributeValueType.Integer)
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("container:replicas", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsInvalidAttributeDefinitionDefaultMissingRequiredField()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["identity:principal"] = new(
                            DefaultValue: ResourceAttributeValue.Object(
                                new Dictionary<string, ResourceAttributeValue>()),
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShape: new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["subject"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true)
                                }))
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("identity:principal.subject", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsInvalidAttributeDefinitionDefaultMissingRequiredFieldFromLocalShape()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["runtime:healthCheck"] = new(
                            DefaultValue: ResourceAttributeValue.Object(
                                new Dictionary<string, ResourceAttributeValue>()),
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: "runtime:healthCheck")
                    },
                    AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
                    {
                        ["runtime:healthCheck"] = new(
                            new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["name"] = new(
                                        ValueType: ResourceAttributeValueType.String,
                                        Required: true)
                                }))
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("runtime:healthCheck.name", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsInvalidResourceReferenceAttributeDefinitionDefault()
    {
        var referenceValue = ResourceAttributeValue.FromObject(
            ResourceReference.DependsOnResourceId(
                "application.sql-server:server",
                typeId: "application.sql-server"));
        var invalidReferenceValue = ResourceAttributeValue.Object(
            referenceValue.ObjectValue!
                .Where(pair => pair.Key != "addressingMode")
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value));
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["database.server"] = new(
                            DefaultValue: invalidReferenceValue,
                            ValueType: ResourceAttributeValueType.ResourceReference)
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("database.server.addressingMode", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsInvalidAttributeDefinitionDefaultCollectionSize()
    {
        var resolver = new ResourceResolver(
            [
                new(ExecutableApplicationResourceTypeProvider.ClassId)
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["runtime:healthChecks"] = new(
                            DefaultValue: ResourceAttributeValue.Array([]),
                            ValueType: ResourceAttributeValueType.ComplexType,
                            ValueShapeId: "runtime:healthCheck",
                            IsCollection: true,
                            Collection: new(MinSize: 1))
                    },
                    AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
                    {
                        ["runtime:healthCheck"] = new(
                            new(
                                new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                                {
                                    ["name"] = new(ValueType: ResourceAttributeValueType.String)
                                }))
                    })
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid, diagnostic.Code);
        Assert.Equal("runtime:healthChecks", diagnostic.Target);
    }

    [Fact]
    public void Resolve_ReportsRequiredAttributeDiagnostics()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    RequiredAttributes:
                    [
                        new(
                            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath,
                            "Executable path is required.")
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId)
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing, diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public void Resolve_BlocksOperationOverrideWhenInheritedOperationDisallowsOverride()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Operations:
                    [
                        new("start", AllowOverride: false)
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Operations:
                    [
                        new("start", ResourceDefinitionJson.FromValue(new { policy = "type" }))
                    ])
            ]);

        var resolved = resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId));

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed, diagnostic.Code);
        Assert.Equal(ResourceDefinitionValueSource.ClassDefinition, resolved.Operations.Resolve("start").Source);
    }

    [Fact]
    public void ResourceDefinition_CanRoundTripPlainJsonPayloads()
    {
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: "API",
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration(
                    "dotnet",
                    "run",
                    "./src/Api"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityId] = ResourceDefinitionJson.FromValue(new VolumeConsumerCapability(
                    [new("volume:data", "App_Data", ReadOnly: false)]))
            });

        var json = JsonSerializer.Serialize(definition);
        var roundTrip = JsonSerializer.Deserialize<ResourceDefinition>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("api", roundTrip.Name);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, roundTrip.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, roundTrip.ProviderId);

        var executable = roundTrip.GetConfiguration<ExecutableApplicationConfiguration>(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection);
        Assert.NotNull(executable);
        Assert.Equal("dotnet", executable.Path);
        Assert.Equal("./src/Api", executable.WorkingDirectory);

        var volumeConsumer = roundTrip.GetCapability<VolumeConsumerCapability>("storage.volumeConsumer");
        Assert.NotNull(volumeConsumer);
        var mount = Assert.Single(volumeConsumer.Mounts);
        Assert.Equal("volume:data", mount.Volume);
        Assert.Equal("App_Data", mount.TargetPath);
        Assert.False(mount.ReadOnly);
    }

    private sealed record VolumeConsumerCapability(
        IReadOnlyList<VolumeMountDefinition> Mounts);

    private sealed record VolumeMountDefinition(
        string Volume,
        string TargetPath,
        bool ReadOnly);

    private static class VolumeConsumerCapabilityProvider
    {
        public static readonly ResourceCapabilityId CapabilityId = "storage.volumeConsumer";
    }
}
