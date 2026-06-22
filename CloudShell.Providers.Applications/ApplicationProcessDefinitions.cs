using CloudShell.Abstractions.ResourceManager;
using System.Text;

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
    {
        if (!string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return definition.AspNetCoreHotReload;
        }

        return definition.Arguments?.TrimStart().StartsWith("watch ", StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public static string? TryExtractProjectPathFromDotNetArguments(string? arguments)
    {
        var tokens = SplitCommandLine(arguments);
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (string.Equals(tokens[index], "--project", StringComparison.OrdinalIgnoreCase))
            {
                return tokens[index + 1];
            }
        }

        return null;
    }

    public static string? TryExtractApplicationArgumentsFromDotNetArguments(string? arguments)
    {
        var separatorIndex = arguments?.IndexOf(" -- ", StringComparison.Ordinal);
        if (separatorIndex is null or < 0)
        {
            return null;
        }

        var value = arguments![(separatorIndex.Value + 4)..];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string BuildDotNetAspNetCoreProjectArguments(
        string projectPath,
        bool hotReload,
        string? applicationArguments)
    {
        var runnerArguments = hotReload
            ? $"watch --non-interactive --project {QuoteCommandArgument(projectPath)} run --no-launch-profile"
            : $"run --project {QuoteCommandArgument(projectPath)} --no-build --no-launch-profile";

        return string.IsNullOrWhiteSpace(applicationArguments)
            ? runnerArguments
            : $"{runnerArguments} -- {applicationArguments.Trim()}";
    }

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

    private static IReadOnlyList<string> SplitCommandLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        foreach (var character in value)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\')
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private static LocalProcessLifetime ToLocalProcessLifetime(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => LocalProcessLifetime.ControlPlaneScoped,
            _ => LocalProcessLifetime.Detached
        };

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;

    private sealed record ApplicationProcessCommand(
        string ExecutablePath,
        string? Arguments);
}
