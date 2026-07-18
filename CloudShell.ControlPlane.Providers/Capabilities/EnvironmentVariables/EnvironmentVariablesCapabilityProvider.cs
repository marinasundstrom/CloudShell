namespace CloudShell.ControlPlane.Providers;

public sealed class EnvironmentVariablesCapabilityProvider :
    IResourceCapabilityAttributeProvider
{
    public static readonly ResourceCapabilityId CapabilityIdValue =
        ResourceCommonCapabilityIds.EnvironmentVariables;

    public static readonly ResourceAttributeId AttributeId =
        ResourceAttributeId.Create(CapabilityIdValue.ToString());

    public ResourceCapabilityId CapabilityId => CapabilityIdValue;

    public IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition> AttributeDefinitions { get; } =
        new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [AttributeId] = new(
                Description: "Environment variables keyed by variable name. Values are resolved when the resource starts.",
                ValueType: ResourceAttributeValueType.ComplexType,
                Path: "environmentVariables")
        };

    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> AttributeValueShapes { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>();
}

public sealed record ResourceEnvironmentVariableValue(
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(ApplicationEnvironmentVariableValueJsonConverter))]
    string? Value = null,
    ResourceConfigurationSettingReference? ConfigurationSettingRef = null,
    ResourceSecretReference? SecretRef = null);
