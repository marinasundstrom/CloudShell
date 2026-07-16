using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;

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

    public string? HostSettingsPath { get; init; }

    public bool NoBuild { get; init; }

    public int? TimeoutSeconds { get; init; }

    public ResourceDefinitionApplyMode Mode { get; init; } =
        ResourceDefinitionApplyMode.CreateOrUpdate;

    public string? BearerToken { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool InheritStandardInput { get; init; }

    public static CloudShellHostLauncherOptions FromArguments(
        string[]? args = null,
        string? applicationDirectory = null,
        IConfiguration? configuration = null)
    {
        args ??= [];

        var repositoryRoot = TryFindRepositoryRoot(applicationDirectory);
        var appHostSettingsPath = ReadArgumentValue(args, "--host-settings")
            ?? Environment.GetEnvironmentVariable("CLOUDSHELL_HOST_SETTINGS")
            ?? configuration?["CloudShell:Launcher:HostSettingsPath"]
            ?? TryResolveAppHostSettingsPath(applicationDirectory);
        var explicitDataDirectory = ReadArgumentValue(args, "--data-dir")
            ?? Environment.GetEnvironmentVariable("CLOUDSHELL_DATA_DIR")
            ?? configuration?["CloudShell:Launcher:DataDirectory"];
        var stateDirectory = ResolvePath(
            ReadArgumentValue(args, "--state-dir")
                ?? Environment.GetEnvironmentVariable("CLOUDSHELL_STATE_DIR")
                ?? configuration?["CloudShell:Launcher:StateDirectory"]
                ?? Path.Combine(
                    applicationDirectory is null
                        ? Environment.CurrentDirectory
                        : Path.GetFullPath(Path.Combine(applicationDirectory, "..")),
                    ".cloudshell"),
            applicationDirectory);
        var controlPlaneUrl = new Uri(
            ReadArgumentValue(args, "--control-plane")
                ?? Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_URL")
                ?? configuration?["CloudShell:Launcher:ControlPlaneUrl"]
                ?? "http://127.0.0.1:5104");
        var hostUrl = new Uri(
            ReadArgumentValue(args, "--url")
                ?? Environment.GetEnvironmentVariable("CLOUDSHELL_HOST_URL")
                ?? configuration?["CloudShell:Launcher:HostUrl"]
                ?? controlPlaneUrl.ToString());

        var dataDirectory = explicitDataDirectory is not null
            ? ResolvePath(explicitDataDirectory, applicationDirectory)
            : string.IsNullOrWhiteSpace(configuration?["CloudShell:DataDirectory"])
                ? stateDirectory
                : null;

        return new()
        {
            CliProjectPath = ReadArgumentValue(args, "--cli-project")
                ?? Environment.GetEnvironmentVariable("CLOUDSHELL_CLI_PROJECT")
                ?? configuration?["CloudShell:Launcher:CliProject"]
                ?? TryResolveRepositoryPath(repositoryRoot, "CloudShell.Cli", "CloudShell.Cli.csproj"),
            TemplatePath = ResolveOptionalPath(ReadArgumentValue(args, "--template-path"), applicationDirectory),
            EnvironmentId = configuration?["CloudShell:Launcher:EnvironmentId"] ?? "local",
            ControlPlaneUrl = controlPlaneUrl,
            StateDirectory = stateDirectory,
            DataDirectory = dataDirectory,
            StartHost = HasArgument(args, "--start"),
            HostProjectPath = ReadArgumentValue(args, "--host-project")
                ?? Environment.GetEnvironmentVariable("CLOUDSHELL_HOST_PROJECT")
                ?? configuration?["CloudShell:Launcher:HostProject"]
                ?? TryResolveRepositoryPath(
                    repositoryRoot,
                    "CloudShell.LocalDevelopmentHost",
                    "CloudShell.LocalDevelopmentHost.csproj"),
            HostUrl = hostUrl,
            HostSettingsPath = ResolveOptionalPath(appHostSettingsPath, applicationDirectory),
            NoBuild = HasArgument(args, "--no-build"),
            BearerToken = Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN"),
            WorkingDirectory = repositoryRoot
                ?? applicationDirectory
                ?? Environment.CurrentDirectory
        };
    }

    internal static bool HasArgument(
        string[] args,
        string name) =>
        args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

    internal static string? ReadArgumentValue(
        string[] args,
        string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static string? TryResolveRepositoryPath(
        string? repositoryRoot,
        params string[] pathParts) =>
        repositoryRoot is null
            ? null
            : Path.Combine([repositoryRoot, .. pathParts]);

    private static string? ResolveOptionalPath(
        string? path,
        string? applicationDirectory) =>
        string.IsNullOrWhiteSpace(path)
            ? null
            : ResolvePath(path, applicationDirectory);

    private static string ResolvePath(
        string path,
        string? applicationDirectory) =>
        Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(
                path,
                applicationDirectory ?? Environment.CurrentDirectory);

    private static string? TryResolveAppHostSettingsPath(string? applicationDirectory)
    {
        if (string.IsNullOrWhiteSpace(applicationDirectory))
        {
            return null;
        }

        var candidate = Path.Combine(applicationDirectory, "appsettings.json");
        return File.Exists(candidate)
            ? candidate
            : null;
    }

    private static string? TryFindRepositoryRoot(string? applicationDirectory)
    {
        var directory = new DirectoryInfo(applicationDirectory ?? Environment.CurrentDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
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
        ResourceTemplateSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(
            template,
            options,
            DefaultCloudShellHostLauncherCommandRunner.Instance,
            serializerOptions,
            cancellationToken);

    public static async Task<CloudShellHostLauncherResult> ApplyAsync(
        ResourceTemplate template,
        CloudShellHostLauncherOptions options,
        ICloudShellHostLauncherCommandRunner commandRunner,
        ResourceTemplateSerializerOptions? serializerOptions = null,
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
            serializerOptions,
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

    public static async Task<CloudShellHostLauncherResult> RunAsync(
        ResourceTemplate template,
        CloudShellHostLauncherOptions options,
        ResourceTemplateSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(options);

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
            serializerOptions,
            cancellationToken);

        var hostUrl = options.HostUrl ?? options.ControlPlaneUrl ??
            throw new InvalidOperationException("A host URL or Control Plane URL is required for foreground run.");
        if (string.IsNullOrWhiteSpace(options.HostProjectPath))
        {
            throw new InvalidOperationException("A host project path is required for foreground run.");
        }

        var hostArguments = BuildHostRunArguments(options, hostUrl);
        using var hostProcess = StartProcess("dotnet", hostArguments, options);
        try
        {
            await WaitForReadyAsync(
                hostUrl,
                options.BearerToken,
                hostProcess,
                TimeSpan.FromSeconds(options.TimeoutSeconds ?? 60),
                cancellationToken);

            var applyOptions = options with
            {
                ControlPlaneUrl = hostUrl,
                HostUrl = null,
                HostProjectPath = null,
                StateDirectory = null,
                DataDirectory = null,
                StartHost = false,
                NoBuild = false,
                TemplatePath = templatePath
            };
            var applyResult = await ApplyAsync(
                template,
                applyOptions,
                serializerOptions,
                cancellationToken);
            if (applyResult.ExitCode != 0)
            {
                await TryStopAsync(hostProcess);
                return applyResult;
            }

            Console.WriteLine(FormatHostUrlMessage(hostUrl));

            await hostProcess.WaitForExitAsync(cancellationToken);
            return new("dotnet", hostArguments, hostProcess.ExitCode, templatePath);
        }
        catch
        {
            await TryStopAsync(hostProcess);
            throw;
        }
    }

    public static async Task<string> WriteTemplateAsync(
        ResourceTemplate template,
        string path,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml,
        ResourceTemplateSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var document = ResourceTemplateSerializer.SerializeTemplate(template, format, serializerOptions);
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
        AddOption(arguments, "--host-settings", options.HostSettingsPath);
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

    public static IReadOnlyList<string> BuildHostRunArguments(
        CloudShellHostLauncherOptions options,
        Uri hostUrl)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(hostUrl);
        if (string.IsNullOrWhiteSpace(options.HostProjectPath))
        {
            throw new InvalidOperationException("A host project path is required for foreground run.");
        }

        var arguments = new List<string>
        {
            "run",
            "--project",
            options.HostProjectPath
        };
        if (options.NoBuild)
        {
            arguments.Add("--no-build");
        }

        arguments.Add("--");
        arguments.Add("--urls");
        arguments.Add(FormatUri(hostUrl)!);
        AddOption(arguments, "--CloudShell:DataDirectory", options.DataDirectory);
        AddOption(arguments, "--CloudShell:HostSettingsPath", options.HostSettingsPath);
        return arguments;
    }

    public static string FormatHostUrlMessage(Uri hostUrl)
    {
        ArgumentNullException.ThrowIfNull(hostUrl);
        return $"CloudShell UI: {FormatUri(hostUrl)}";
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

    internal static Process StartProcess(
        string command,
        IReadOnlyList<string> arguments,
        CloudShellHostLauncherOptions options)
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

        return Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell launcher command.");
    }

    private static async Task WaitForReadyAsync(
        Uri baseUrl,
        string? bearerToken,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        while (!linked.Token.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException("CloudShell host exited before it was ready.");
            }

            if (await IsReadyAsync(baseUrl, bearerToken, linked.Token))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token);
        }

        throw new TimeoutException($"CloudShell host did not become ready within {timeout.TotalSeconds:N0} seconds.");
    }

    private static async Task<bool> IsReadyAsync(
        Uri baseUrl,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { BaseAddress = NormalizeBaseAddress(baseUrl) };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", bearerToken);
            }

            using var response = await client.GetAsync("api/control-plane/v1/resources", cancellationToken);
            return response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent;
        }
        catch
        {
            return false;
        }
    }

    private static Uri NormalizeBaseAddress(Uri baseUrl)
    {
        var value = baseUrl.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? baseUrl
            : new Uri(value + "/", UriKind.Absolute);
    }

    private static async Task TryStopAsync(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }
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
        using var process = CloudShellHostLauncher.StartProcess(command, arguments, options);
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }
}
