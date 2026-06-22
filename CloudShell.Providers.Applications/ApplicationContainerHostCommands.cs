using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace CloudShell.Providers.Applications;

internal static class ApplicationContainerHostCommands
{
    private const string ContainerHostExecutableMetadataKey = "cloudshell.executable";

    public static async Task<int> RunAsync(
        ContainerHostDescriptor engine,
        IReadOnlyList<string> arguments,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var command = GetCommandName(arguments);
        var commandLine = FormatCommandLine(arguments);
        var startInfo = CreateStartInfo(engine);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                logger.LogWarning(
                    "Container host {ContainerHostId} could not start command {ContainerHostCommandLine} ({ContainerHostCommand}).",
                    engine.Id,
                    commandLine,
                    command);
                return -1;
            }

            LogStarted(logger, process, engine, command, commandLine);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await WaitForExitOrKillAsync(process, engine, cancellationToken, logger, command, commandLine);
            LogExited(logger, process, engine, command, commandLine);
            var output = await outputTask;
            var error = await errorTask;
            if (!string.IsNullOrWhiteSpace(output))
            {
                log.Append(output.Trim(), "process", "Information");
            }

            if (!string.IsNullOrWhiteSpace(error) &&
                !error.Contains("No such container", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                log.Append(error.Trim(), "process", process.ExitCode == 0 ? "Information" : "Warning");
            }

            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(
                exception,
                "Container host {ContainerHostId} failed to start command {ContainerHostCommandLine} ({ContainerHostCommand}).",
                engine.Id,
                commandLine,
                command);
            log.Append(exception.Message, "process", "Warning");
            return -1;
        }
        finally
        {
            if (process is not null)
            {
                LogReleased(logger, process, engine, command, commandLine);
                process.Dispose();
            }
        }
    }

    public static async Task<ContainerHostCommandResult> CaptureAsync(
        ContainerHostDescriptor engine,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        var command = GetCommandName(arguments);
        var commandLine = FormatCommandLine(arguments);
        var startInfo = CreateStartInfo(engine);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process? process = null;
        try
        {
            process = Process.Start(startInfo);
            if (process is null)
            {
                logger.LogWarning(
                    "Container host {ContainerHostId} could not start command {ContainerHostCommandLine} ({ContainerHostCommand}).",
                    engine.Id,
                    commandLine,
                    command);
                return new ContainerHostCommandResult(-1, string.Empty, "Container host command could not be started.");
            }

            LogStarted(logger, process, engine, command, commandLine);
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await WaitForExitOrKillAsync(process, engine, cancellationToken, logger, command, commandLine);
            LogExited(logger, process, engine, command, commandLine);
            return new ContainerHostCommandResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogWarning(
                exception,
                "Container host {ContainerHostId} failed to start command {ContainerHostCommandLine} ({ContainerHostCommand}).",
                engine.Id,
                commandLine,
                command);
            return new ContainerHostCommandResult(-1, string.Empty, exception.Message);
        }
        finally
        {
            if (process is not null)
            {
                LogReleased(logger, process, engine, command, commandLine);
                process.Dispose();
            }
        }
    }

    public static ProcessStartInfo CreateStartInfo(ContainerHostDescriptor engine) =>
        ConfigureEnvironment(
            new ProcessStartInfo
            {
                FileName = GetExecutable(engine),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            engine);

    public static string GetExecutable(ContainerHostDescriptor engine) =>
        engine.HostMetadata.TryGetValue(ContainerHostExecutableMetadataKey, out var executable) &&
        !string.IsNullOrWhiteSpace(executable)
            ? executable
            : engine.Kind == ContainerHostKind.Podman ? "podman" : "docker";

    public static ProcessStartInfo ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor engine)
    {
        if (string.IsNullOrWhiteSpace(engine.Endpoint))
        {
            return startInfo;
        }

        if (engine.Kind == ContainerHostKind.Podman)
        {
            startInfo.Environment["CONTAINER_HOST"] = engine.Endpoint;
            return startInfo;
        }

        startInfo.Environment["DOCKER_HOST"] = engine.Endpoint;
        return startInfo;
    }

    public static async Task WaitForExitOrKillAsync(
        Process process,
        ContainerHostDescriptor engine,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        string? command = null,
        string? commandLine = null)
    {
        var resolvedCommand = command ?? "unknown";
        var resolvedCommandLine = string.IsNullOrWhiteSpace(commandLine)
            ? resolvedCommand
            : commandLine;
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug(
                "Container host {ContainerHostId} cancellation requested for command {ContainerHostCommandLine} ({ContainerHostCommand}); terminating host interaction.",
                engine.Id,
                resolvedCommandLine,
                resolvedCommand);
            KillIfRunning(logger, process, engine, resolvedCommand, resolvedCommandLine);
            logger?.LogDebug(
                "Container host {ContainerHostId} canceled command {ContainerHostCommandLine} and released the host interaction ({ContainerHostCommand}).",
                engine.Id,
                resolvedCommandLine,
                resolvedCommand);
            throw;
        }
    }

    public static void KillIfRunning(
        ILogger? logger,
        Process process,
        ContainerHostDescriptor engine,
        string command,
        string commandLine)
    {
        if (process.HasExited)
        {
            return;
        }

        logger?.LogDebug(
            "Container host {ContainerHostId} killing canceled command {ContainerHostCommandLine} ({ContainerHostCommand}, process {ProcessId}).",
            engine.Id,
            commandLine,
            command,
            process.Id);
        ProcessShutdown.KillProcessTreeAndWait(process);
    }

    public static string GetCommandName(IReadOnlyList<string> arguments) =>
        arguments.Count > 0 && !string.IsNullOrWhiteSpace(arguments[0])
            ? arguments[0]
            : "unknown";

    public static string FormatCommandLine(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? "unknown"
            : string.Join(' ', arguments.Select(QuoteCommandArgument));

    public static void LogStarted(
        ILogger logger,
        Process process,
        ContainerHostDescriptor engine,
        string command,
        string commandLine) =>
        logger.LogDebug(
            "Container host {ContainerHostId} started command {ContainerHostCommandLine} ({ContainerHostCommand}, process {ProcessId}).",
            engine.Id,
            commandLine,
            command,
            process.Id);

    public static void LogExited(
        ILogger logger,
        Process process,
        ContainerHostDescriptor engine,
        string command,
        string commandLine) =>
        logger.LogDebug(
            "Container host {ContainerHostId} completed command {ContainerHostCommandLine} with exit code {ExitCode} ({ContainerHostCommand}, process {ProcessId}).",
            engine.Id,
            commandLine,
            process.ExitCode,
            command,
            process.Id);

    public static void LogReleased(
        ILogger logger,
        Process process,
        ContainerHostDescriptor engine,
        string command,
        string commandLine) =>
        logger.LogDebug(
            "Container host {ContainerHostId} released command {ContainerHostCommandLine} after process termination ({ContainerHostCommand}, process {ProcessId}, exit code {ExitCode}).",
            engine.Id,
            commandLine,
            command,
            process.Id,
            TryGetExitCode(process));

    private static string QuoteCommandArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
        {
            return "\"\"";
        }

        return argument.Any(char.IsWhiteSpace) || argument.Contains('"', StringComparison.Ordinal)
            ? $"\"{argument.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

internal sealed record ContainerHostCommandResult(
    int ExitCode,
    string Output,
    string Error);
