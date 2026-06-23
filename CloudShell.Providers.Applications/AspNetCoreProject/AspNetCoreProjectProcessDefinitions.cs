using System.Text;

namespace CloudShell.Providers.Applications;

internal static class AspNetCoreProjectProcessDefinitions
{
    public static bool ResolveHotReload(ApplicationResourceDefinition definition)
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

    public static string BuildDotNetRunArguments(
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

    internal static IReadOnlyList<string> SplitCommandLine(string? value)
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

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
}
