using System.Diagnostics;
using CloudShell.ResourceModel;

namespace CloudShell.AppHost.Launcher;

public sealed record CloudShellHostLauncherOptions
{
    public string? CliProjectPath { get; init; }

    public string? CloudShellCommand { get; init; }

    public string? TemplatePath { get; init; }

    public ResourceTemplateFormat TemplateFormat { get; init; } = ResourceTemplateFormat.Yaml;

    public string? EnvironmentId { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    public Uri? ControlPlaneUrl { get; init; }

    public string? StateDirectory { get; init; }

    public string? DataDirectory { get; init; }

    public bool StartHost { get; init; }

    public string? HostProjectPath { get; init; }

    public Uri? HostUrl { get; init; }

    public bool NoBuild { get; init; }

    public int? TimeoutSeconds { get; init; }

    public ResourceDefinitionApplyMode Mode { get; init; } =
        ResourceDefinitionApplyMode.CreateOrUpdate;

    public string? BearerToken { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool InheritStandardInput { get; init; }
}

public sealed record CloudShellHostLauncherResult(
    string Command,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    string TemplatePath);

public interface ICloudShellHostLauncherCommandRunner
{
    Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        CloudShellHostLauncherOptions options,
        CancellationToken cancellationToken);
}

public static class CloudShellHostLauncher
{
    public static Task<CloudShellHostLauncherResult> ApplyAsync(
        ResourceTemplate template,
        CloudShellHostLauncherOptions options,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            template,
            options,
            DefaultCloudShellHostLauncherCommandRunner.Instance,
            cancellationToken);

    public static async Task<CloudShellHostLauncherResult> ApplyAsync(
        ResourceTemplate template,
        CloudShellHostLauncherOptions options,
        ICloudShellHostLauncherCommandRunner commandRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(commandRunner);

        var templatePath = options.TemplatePath ??
            Path.Combine(
                Directory.CreateTempSubdirectory("cloudshell-template-").FullName,
                options.TemplateFormat == ResourceTemplateFormat.Json
                    ? "resources.json"
                    : "resources.yaml");
        await WriteTemplateAsync(
            template,
            templatePath,
            options.TemplateFormat,
            cancellationToken);

        var templateApplyArguments = BuildTemplateApplyArguments(templatePath, options);
        var command = string.IsNullOrWhiteSpace(options.CliProjectPath)
            ? options.CloudShellCommand ?? "cloudshell"
            : "dotnet";
        var arguments = string.IsNullOrWhiteSpace(options.CliProjectPath)
            ? templateApplyArguments
            :
            [
                "run",
                "--project",
                options.CliProjectPath!,
                "--",
                .. templateApplyArguments
            ];

        var exitCode = await commandRunner.RunAsync(
            command,
            arguments,
            options,
            cancellationToken);
        return new(command, arguments, exitCode, templatePath);
    }

    public static async Task<string> WriteTemplateAsync(
        ResourceTemplate template,
        string path,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var document = ResourceTemplateSerializer.SerializeTemplate(template, format);
        await File.WriteAllTextAsync(fullPath, document, cancellationToken);
        return fullPath;
    }

    public static IReadOnlyList<string> BuildTemplateApplyArguments(
        string templatePath,
        CloudShellHostLauncherOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templatePath);
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>
        {
            "template",
            "apply",
            templatePath
        };
        AddOption(arguments, "--control-plane", FormatUri(options.ControlPlaneUrl));
        AddOption(arguments, "--state-dir", options.StateDirectory);
        AddOption(arguments, "--host-project", options.HostProjectPath);
        AddOption(arguments, "--data-dir", options.DataDirectory);
        AddOption(arguments, "--url", FormatUri(options.HostUrl));
        AddOption(arguments, "--timeout-seconds", options.TimeoutSeconds?.ToString());
        AddOption(arguments, "--mode", ToCliMode(options.Mode));
        AddOption(arguments, "--bearer-token", options.BearerToken);
        if (options.StartHost)
        {
            arguments.Add("--start");
        }

        if (options.NoBuild)
        {
            arguments.Add("--no-build");
        }

        return arguments;
    }

    private static void AddOption(
        List<string> arguments,
        string name,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        arguments.Add(name);
        arguments.Add(value.Trim());
    }

    private static string ToCliMode(ResourceDefinitionApplyMode mode) =>
        mode switch
        {
            ResourceDefinitionApplyMode.CreateOrUpdate => "create-or-update",
            ResourceDefinitionApplyMode.CreateOnly => "create-only",
            ResourceDefinitionApplyMode.UpdateExisting => "update-existing",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported apply mode.")
        };

    private static string? FormatUri(Uri? uri) =>
        uri?.ToString().TrimEnd('/');
}

internal sealed class DefaultCloudShellHostLauncherCommandRunner :
    ICloudShellHostLauncherCommandRunner
{
    public static DefaultCloudShellHostLauncherCommandRunner Instance { get; } = new();

    public async Task<int> RunAsync(
        string command,
        IReadOnlyList<string> arguments,
        CloudShellHostLauncherOptions options,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            WorkingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
                ? Environment.CurrentDirectory
                : options.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = !options.InheritStandardInput
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell launcher command.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
