using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace CloudShell.Cli;

internal sealed class ControlPlaneDaemon
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<ControlPlaneDaemonState> StartAsync(
        ControlPlaneStartCommand command,
        CancellationToken cancellationToken)
    {
        var stateFile = GetStateFile(command.StateDirectory);
        var existing = await ReadStateAsync(command.StateDirectory);
        if (existing is not null && IsProcessRunning(existing.ProcessId))
        {
            return existing;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        var hostProject = ResolveHostProject(command.HostProject);
        var processId = await StartHostProcessAsync(
            hostProject,
            command.DataDirectory,
            command.HostSettingsPath,
            command.Url,
            command.NoBuild,
            command.StateDirectory,
            cancellationToken);
        var state = new ControlPlaneDaemonState(
            processId,
            command.Url,
            hostProject,
            ResolveDataDirectory(command.DataDirectory),
            DateTimeOffset.UtcNow);

        await WriteStateAsync(stateFile, state, cancellationToken);

        try
        {
            await WaitForReadyAsync(
                command.Url,
                command.BearerToken,
                processId,
                TimeSpan.FromSeconds(command.TimeoutSeconds),
                cancellationToken);
        }
        catch
        {
            await TryStopAsync(processId);
            TryDelete(stateFile);
            throw;
        }

        return state;
    }

    public async Task<ControlPlaneDaemonStopResult> StopAsync(ControlPlaneStopCommand command)
    {
        var stateFile = GetStateFile(command.StateDirectory);
        var state = await ReadStateAsync(command.StateDirectory);
        if (state is null)
        {
            return new ControlPlaneDaemonStopResult(false, false, null);
        }

        var stopped = false;
        if (await TryStopAsync(state.ProcessId))
        {
            stopped = true;
        }

        TryDelete(stateFile);
        return new ControlPlaneDaemonStopResult(true, stopped, state.ProcessId);
    }

    public async Task<ControlPlaneDaemonStatus> StatusAsync(
        ControlPlaneStatusCommand command,
        CancellationToken cancellationToken)
    {
        var state = await ReadStateAsync(command.StateDirectory);
        if (state is null)
        {
            return new ControlPlaneDaemonStatus(null, false, false);
        }

        var running = IsProcessRunning(state.ProcessId);
        var apiReady = running && await IsReadyAsync(
            state.BaseUrl,
            command.BearerToken,
            cancellationToken);
        return new ControlPlaneDaemonStatus(state, running, apiReady);
    }

    public async Task<ControlPlaneDaemonState?> ReadStateAsync(string stateDirectory)
    {
        var stateFile = GetStateFile(stateDirectory);
        if (!File.Exists(stateFile))
        {
            return null;
        }

        await using var stream = File.OpenRead(stateFile);
        return await JsonSerializer.DeserializeAsync<ControlPlaneDaemonState>(
            stream,
            SerializerOptions);
    }

    private static async Task<int> StartHostProcessAsync(
        string hostProject,
        string? dataDirectory,
        string? hostSettingsPath,
        Uri url,
        bool noBuild,
        string stateDirectory,
        CancellationToken cancellationToken)
    {
        if (!noBuild)
        {
            await BuildHostProjectAsync(hostProject, cancellationToken);
        }

        var targetPath = await GetHostTargetPathAsync(hostProject, cancellationToken);
        if (string.IsNullOrWhiteSpace(targetPath) ||
            !File.Exists(targetPath))
        {
            throw new FileNotFoundException(
                $"The host output assembly '{targetPath}' does not exist. Build the host first or omit --no-build.",
                targetPath);
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await StartDetachedUnixHostProcessAsync(
                targetPath,
                Path.GetDirectoryName(hostProject) ?? Environment.CurrentDirectory,
                dataDirectory,
                hostSettingsPath,
                url,
                stateDirectory,
                cancellationToken);
        }

        var process = StartWindowsHostProcess(
            targetPath,
            Path.GetDirectoryName(hostProject) ?? Environment.CurrentDirectory,
            dataDirectory,
            hostSettingsPath,
            url);
        return process.Id;
    }

    private static Process StartWindowsHostProcess(
        string targetPath,
        string workingDirectory,
        string? dataDirectory,
        string? hostSettingsPath,
        Uri url)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add(targetPath);
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(url.ToString());
        AddDataDirectoryArguments(startInfo.ArgumentList, dataDirectory);
        AddHostSettingsArguments(startInfo.ArgumentList, hostSettingsPath);

        return Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell host process.");
    }

    private static async Task<int> StartDetachedUnixHostProcessAsync(
        string targetPath,
        string workingDirectory,
        string? dataDirectory,
        string? hostSettingsPath,
        Uri url,
        string stateDirectory,
        CancellationToken cancellationToken)
    {
        var logFile = Path.Combine(Path.GetFullPath(stateDirectory), "control-plane.log");
        var arguments = new List<string>
        {
            targetPath,
            "--urls",
            url.ToString()
        };
        AddDataDirectoryArguments(arguments, dataDirectory);
        AddHostSettingsArguments(arguments, hostSettingsPath);

        var command = string.Join(
            " ",
            [
                "cd",
                ShellQuote(workingDirectory),
                "||",
                "exit",
                "1;",
                "nohup",
                "dotnet",
                .. arguments.Select(ShellQuote),
                ">",
                ShellQuote(logFile),
                "2>&1",
                "<",
                "/dev/null",
                "&",
                "echo",
                "$!"
            ]);
        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using var launcher = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell host process.");
        var outputTask = launcher.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = launcher.StandardError.ReadToEndAsync(cancellationToken);
        await launcher.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (launcher.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to start the CloudShell host process. {error}".Trim());
        }

        var pidText = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        if (!int.TryParse(pidText, out var processId))
        {
            throw new InvalidOperationException(
                $"Failed to read the CloudShell host process id. {output}".Trim());
        }

        return processId;
    }

    private static async Task BuildHostProjectAsync(
        string hostProject,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(hostProject);

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start dotnet build.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var output = await outputTask;
            var error = await errorTask;
            throw new InvalidOperationException(
                $"Failed to build the CloudShell host project. {output} {error}".Trim());
        }
    }

    private static async Task<string> GetHostTargetPathAsync(
        string hostProject,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(hostProject);
        startInfo.ArgumentList.Add("-getProperty:TargetPath");

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to evaluate the CloudShell host target path.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Failed to evaluate the CloudShell host target path. {error}".Trim());
        }

        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault() ?? string.Empty;
    }

    private static async Task WaitForReadyAsync(
        Uri baseUrl,
        string? bearerToken,
        int processId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        while (!linked.Token.IsCancellationRequested)
        {
            if (!IsProcessRunning(processId))
            {
                throw new InvalidOperationException(
                    "Control Plane process exited before it was ready.");
            }

            if (await IsReadyAsync(baseUrl, bearerToken, linked.Token))
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), linked.Token);
        }

        throw new TimeoutException($"Control Plane did not become ready within {timeout.TotalSeconds:N0} seconds.");
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
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
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

    private static string ResolveHostProject(string? hostProject)
    {
        if (!string.IsNullOrWhiteSpace(hostProject))
        {
            var fullPath = Path.GetFullPath(hostProject);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"The host project '{fullPath}' does not exist.", fullPath);
            }

            return fullPath;
        }

        var directory = new DirectoryInfo(Environment.CurrentDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "CloudShell.Host", "CloudShell.Host.csproj");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Could not find CloudShell.Host/CloudShell.Host.csproj. Use --host-project.");
    }

    private static async Task WriteStateAsync(
        string stateFile,
        ControlPlaneDaemonState state,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(stateFile);
        await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
    }

    private static string GetStateFile(string stateDirectory) =>
        Path.Combine(Path.GetFullPath(stateDirectory), "control-plane.json");

    private static string? ResolveDataDirectory(string? dataDirectory) =>
        string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.GetFullPath(dataDirectory);

    private static void AddDataDirectoryArguments(
        ICollection<string> arguments,
        string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return;
        }

        arguments.Add("--CloudShell:DataDirectory");
        arguments.Add(Path.GetFullPath(dataDirectory));
    }

    private static void AddHostSettingsArguments(
        ICollection<string> arguments,
        string? hostSettingsPath)
    {
        if (string.IsNullOrWhiteSpace(hostSettingsPath))
        {
            return;
        }

        arguments.Add("--CloudShell:HostSettingsPath");
        arguments.Add(Path.GetFullPath(hostSettingsPath));
    }

    private static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> TryStopAsync(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return false;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // Stale state is not worth failing a stop command.
        }
    }
}

internal sealed record ControlPlaneDaemonState(
    int ProcessId,
    Uri BaseUrl,
    string HostProjectPath,
    string? DataDirectory,
    DateTimeOffset StartedAt);

internal sealed record ControlPlaneDaemonStopResult(
    bool StateFound,
    bool WasRunning,
    int? ProcessId);

internal sealed record ControlPlaneDaemonStatus(
    ControlPlaneDaemonState? State,
    bool ProcessRunning,
    bool ApiReady);
