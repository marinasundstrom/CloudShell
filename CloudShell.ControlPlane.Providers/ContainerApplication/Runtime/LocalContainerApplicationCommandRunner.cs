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
}

public sealed class ProcessLocalContainerApplicationCommandRunner :
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
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Command '{fileName}' could not be started.");
        using var timeoutCancellation = timeout is null
            ? null
            : new CancellationTokenSource(timeout.Value);
        using var linkedCancellation = timeoutCancellation is null
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        var waitCancellationToken = linkedCancellation?.Token ?? cancellationToken;
        var outputTask = process.StandardOutput.ReadToEndAsync(waitCancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(waitCancellationToken);
        try
        {
            await process.WaitForExitAsync(waitCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutCancellation?.IsCancellationRequested == true &&
            !cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            return new LocalContainerApplicationCommandResult(
                LocalContainerApplicationCommandResult.TimeoutExitCode,
                string.Empty,
                $"Command '{fileName} {string.Join(' ', arguments)}' timed out.");
        }

        var result = new LocalContainerApplicationCommandResult(
            process.ExitCode,
            await outputTask.ConfigureAwait(false),
            await errorTask.ConfigureAwait(false));
        if (throwOnError && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' failed with exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}: {result.Error}");
        }

        return result;
    }
}
