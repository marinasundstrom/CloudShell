namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class AspNetCoreProjectShapeIds
{
    public static readonly ResourceAttributeValueShapeId EnvironmentVariable =
        "application.aspNetCoreProject.environmentVariable";
}

public sealed class AspNetCoreProjectShapeProvider : IResourceAttributeValueShapeProvider
{
    public IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> GetAttributeValueShapes() =>
        AspNetCoreProjectShapes.All;
}

public static class AspNetCoreProjectShapes
{
    public static ResourceAttributeValueShapeDefinition EnvironmentVariable { get; } = new(
        new(
            new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
            {
                ["name"] = new(
                    Required: true,
                    RequiredMessage: "Environment variable name is required.",
                    ValueType: ResourceAttributeValueType.String),
                ["value"] = new(ValueType: ResourceAttributeValueType.String)
            }),
        "ASP.NET Core project process environment variable shape.");

    public static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition> All { get; } =
        new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        {
            [AspNetCoreProjectShapeIds.EnvironmentVariable] = EnvironmentVariable
        };
}

public sealed record AspNetCoreProjectEnvironmentVariableValue(
    string Name,
    string? Value = null);
