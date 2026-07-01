using System.Diagnostics;
using System.Net;
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
        var process = StartHostProcess(hostProject, command.Url, command.NoBuild);
        var state = new ControlPlaneDaemonState(
            process.Id,
            command.Url,
            hostProject,
            DateTimeOffset.UtcNow);

        await WriteStateAsync(stateFile, state, cancellationToken);

        try
        {
            await WaitForReadyAsync(
                command.Url,
                command.BearerToken,
                process,
                TimeSpan.FromSeconds(command.TimeoutSeconds),
                cancellationToken);
        }
        catch
        {
            await TryStopAsync(process.Id);
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

    private static Process StartHostProcess(string hostProject, Uri url, bool noBuild)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Path.GetDirectoryName(hostProject) ?? Environment.CurrentDirectory,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(hostProject);
        if (noBuild)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(url.ToString());

        return Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the CloudShell host process.");
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
                throw new InvalidOperationException(
                    $"Control Plane process exited before it was ready. Exit code: {process.ExitCode}.");
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
    DateTimeOffset StartedAt);

internal sealed record ControlPlaneDaemonStopResult(
    bool StateFound,
    bool WasRunning,
    int? ProcessId);

internal sealed record ControlPlaneDaemonStatus(
    ControlPlaneDaemonState? State,
    bool ProcessRunning,
    bool ApiReady);
