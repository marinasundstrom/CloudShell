namespace CloudShell.ControlPlane.Providers;

public static class AspNetCoreProjectShapeIds
{
    public static readonly ResourceAttributeValueShapeId EnvironmentVariableValue =
        "application.aspNetCoreProject.environmentVariableValue";

    public static readonly ResourceAttributeValueShapeId ConfigurationSettingReference =
        "application.configurationSettingReference";

    public static readonly ResourceAttributeValueShapeId SecretReference =
        "application.secretReference";
}

public sealed class AspNetCoreProjectShapeProvider : IResourceAttributeValueShapeProvider
{
    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes() =>
        AspNetCoreProjectShapes.All;
}

public static class AspNetCoreProjectShapes
{
    public static ResourceAttributeValueShapeDefinition EnvironmentVariableValue { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["value"] = new(ValueType: ResourceAttributeValueType.String),
                ["configurationSettingRef"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: AspNetCoreProjectShapeIds.ConfigurationSettingReference),
                ["secretRef"] = new(
                    ValueType: ResourceAttributeValueType.ComplexType,
                    ValueShapeId: AspNetCoreProjectShapeIds.SecretReference)
            }),
        ".NET app process environment variable value shape.");

    public static ResourceAttributeValueShapeDefinition ConfigurationSettingReference { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["storeResourceId"] = new(
                    Required: true,
                    RequiredMessage: "Configuration store resource id is required.",
                    ValueType: ResourceAttributeValueType.String),
                ["name"] = new(
                    Required: true,
                    RequiredMessage: "Configuration setting name is required.",
                    ValueType: ResourceAttributeValueType.String),
                ["version"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Configuration setting reference shape.");

    public static ResourceAttributeValueShapeDefinition SecretReference { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["vaultResourceId"] = new(
                    Required: true,
                    RequiredMessage: "Secrets vault resource id is required.",
                    ValueType: ResourceAttributeValueType.String),
                ["name"] = new(
                    Required: true,
                    RequiredMessage: "Secret name is required.",
                    ValueType: ResourceAttributeValueType.String),
                ["version"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "Secret reference shape.");

    public static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> All { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        {
            [AspNetCoreProjectShapeIds.EnvironmentVariableValue] = EnvironmentVariableValue,
            [AspNetCoreProjectShapeIds.ConfigurationSettingReference] = ConfigurationSettingReference,
            [AspNetCoreProjectShapeIds.SecretReference] = SecretReference
        };
}

public sealed record AspNetCoreProjectEnvironmentVariableValue(
    [property: System.Text.Json.Serialization.JsonConverter(
        typeof(ApplicationEnvironmentVariableValueJsonConverter))]
    string? Value = null,
    ResourceConfigurationSettingReference? ConfigurationSettingRef = null,
    ResourceSecretReference? SecretRef = null);

public sealed record ResourceConfigurationSettingReference(
    string StoreResourceId,
    string Name,
    string? Version = null);

public sealed record ResourceSecretReference(
    string VaultResourceId,
    string Name,
    string? Version = null);
