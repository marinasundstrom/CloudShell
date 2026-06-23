namespace CloudShell.Providers.Applications;

public sealed class AspNetCoreProjectDefinitionNormalizationRule :
    IApplicationResourceDefinitionNormalizationRule
{
    public bool AppliesTo(ApplicationResourceDefinition definition) =>
        ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType);

    public ApplicationResourceDefinition Normalize(
        ApplicationResourceDefinition definition,
        ApplicationResourceDefinitionNormalizationContext context)
    {
        var legacyProjectPath = AspNetCoreProjectProcessDefinitions
            .TryExtractProjectPathFromDotNetArguments(definition.Arguments);
        var projectPath = NormalizeNullable(definition.ProjectPath) ?? legacyProjectPath;

        return definition with
        {
            ExecutablePath = string.Empty,
            Arguments = null,
            ProjectPath = projectPath,
            ProjectArguments = NormalizeNullable(definition.ProjectArguments) ??
                AspNetCoreProjectProcessDefinitions.TryExtractApplicationArgumentsFromDotNetArguments(definition.Arguments),
            AspNetCoreHotReload = AspNetCoreProjectProcessDefinitions.ResolveHotReload(definition),
            UseLaunchSettingsEndpoints = definition.UseLaunchSettingsEndpoints,
            ProjectContainerBuild = string.IsNullOrWhiteSpace(definition.ContainerImage) &&
                definition.ProjectContainerBuild
        };
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
