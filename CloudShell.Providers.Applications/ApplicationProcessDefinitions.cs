using CloudShell.Abstractions.ResourceManager;
namespace CloudShell.Providers.Applications;

internal static class ApplicationProcessDefinitions
{
    public static LocalProcessDefinition Create(
        ApplicationResourceDefinition definition,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null,
        IReadOnlyList<ResourceVolumeMountMaterialization>? volumeMounts = null)
    {
        var command = CreateCommand(definition);
        return new LocalProcessDefinition(
            definition.Id,
            command.ExecutablePath,
            command.Arguments,
            definition.WorkingDirectory,
            environmentVariables ?? definition.EnvironmentVariables,
            ToLocalProcessLifetime(definition.Lifetime),
            volumeMounts);
    }

    public static bool ResolveAspNetCoreHotReload(ApplicationResourceDefinition definition)
        => AspNetCoreProjectProcessDefinitions.ResolveHotReload(definition);

    public static string? TryExtractProjectPathFromDotNetArguments(string? arguments)
        => AspNetCoreProjectProcessDefinitions.TryExtractProjectPathFromDotNetArguments(arguments);

    public static string? TryExtractApplicationArgumentsFromDotNetArguments(string? arguments)
        => AspNetCoreProjectProcessDefinitions.TryExtractApplicationArgumentsFromDotNetArguments(arguments);

    public static string BuildDotNetAspNetCoreProjectArguments(
        string projectPath,
        bool hotReload,
        string? applicationArguments)
        => AspNetCoreProjectProcessDefinitions.BuildDotNetRunArguments(
            projectPath,
            hotReload,
            applicationArguments);

    private static ApplicationProcessCommand CreateCommand(ApplicationResourceDefinition definition)
    {
        if (!ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType))
        {
            return new ApplicationProcessCommand(
                definition.ExecutablePath,
                definition.Arguments);
        }

        if (string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return new ApplicationProcessCommand(
                string.IsNullOrWhiteSpace(definition.ExecutablePath) ? "dotnet" : definition.ExecutablePath,
                definition.Arguments);
        }

        return new ApplicationProcessCommand(
            "dotnet",
            BuildDotNetAspNetCoreProjectArguments(
                definition.ProjectPath,
                definition.AspNetCoreHotReload,
                definition.ProjectArguments));
    }

    private static LocalProcessLifetime ToLocalProcessLifetime(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => LocalProcessLifetime.ControlPlaneScoped,
            _ => LocalProcessLifetime.Detached
        };

    private sealed record ApplicationProcessCommand(
        string ExecutablePath,
        string? Arguments);
}
