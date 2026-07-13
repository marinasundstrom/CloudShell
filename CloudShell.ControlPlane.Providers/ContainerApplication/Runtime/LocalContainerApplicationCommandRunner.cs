using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.ControlPlane.Providers;

public interface ILocalContainerApplicationCommandRunner
{
    LocalContainerApplicationCommandResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null,
        string? workingDirectory = null);

    Task<LocalContainerApplicationCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null,
        string? workingDirectory = null);
}

public sealed record LocalContainerApplicationCommandResult(
    int ExitCode,
    string Output,
    string Error)
{
    public const int TimeoutExitCode = -1;

    public const int UnavailableExitCode = -2;
}

public sealed class ProcessLocalContainerApplicationCommandRunner(
    IContainerHostCommandPlatform containerHostCommandPlatform) :
    ILocalContainerApplicationCommandRunner
{
    public LocalContainerApplicationCommandResult Run(
        string fileName,
        IReadOnlyList<string> arguments,
        bool throwOnError = true,
        TimeSpan? timeout = null,
        string? workingDirectory = null) =>
        RunAsync(fileName, arguments, CancellationToken.None, throwOnError, timeout, workingDirectory)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

    public async Task<LocalContainerApplicationCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        bool throwOnError = true,
        TimeSpan? timeout = null,
        string? workingDirectory = null)
    {
        var startInfo = CreateStartInfo(fileName, arguments, throwOnError, out var unavailableResult);
        if (unavailableResult is not null)
        {
            return unavailableResult;
        }

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var linkedCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var waitCancellationToken = linkedCancellation?.Token ?? cancellationToken;
        Process? process = null;
        try
        {
            process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Command '{startInfo.FileName}' could not be started.");
            using var cancellationRegistration = waitCancellationToken.Register(
                static state => KillProcessTree((Process)state!),
                process);
            var outputTask = process.StandardOutput.ReadToEndAsync(waitCancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(waitCancellationToken);
            await process.WaitForExitAsync(waitCancellationToken).ConfigureAwait(false);
            var result = new LocalContainerApplicationCommandResult(
                process.ExitCode,
                await outputTask.ConfigureAwait(false),
                await errorTask.ConfigureAwait(false));
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Command '{startInfo.FileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
            }

            return result;
        }
        catch (OperationCanceledException) when (
            timeoutCancellation?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            if (process is not null)
            {
                KillProcessTree(process);
            }

            return new LocalContainerApplicationCommandResult(
                LocalContainerApplicationCommandResult.TimeoutExitCode,
                string.Empty,
                $"Command '{startInfo.FileName} {string.Join(' ', arguments)}' timed out.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            if (throwOnError)
            {
                throw;
            }

            return new LocalContainerApplicationCommandResult(
                LocalContainerApplicationCommandResult.UnavailableExitCode,
                string.Empty,
                exception.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private ProcessStartInfo CreateStartInfo(
        string fileName,
        IReadOnlyList<string> arguments,
        bool throwOnError,
        out LocalContainerApplicationCommandResult? unavailableResult)
    {
        unavailableResult = null;
        if (IsContainerRuntimeCommand(fileName))
        {
            var plan = containerHostCommandPlatform.CreatePlan();
            if (!plan.IsAvailable)
            {
                if (throwOnError)
                {
                    throw new InvalidOperationException(plan.UnavailableReason);
                }

                unavailableResult = new LocalContainerApplicationCommandResult(
                    LocalContainerApplicationCommandResult.UnavailableExitCode,
                    string.Empty,
                    plan.UnavailableReason ?? "Container runtime command is unavailable.");
                return new ProcessStartInfo(fileName);
            }

            return plan.CreateStartInfo(arguments);
        }

        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static bool IsContainerRuntimeCommand(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        return string.Equals(name, "docker", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "podman", StringComparison.OrdinalIgnoreCase);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)TimeSpan.FromSeconds(1).TotalMilliseconds);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}
