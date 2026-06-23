using System.ComponentModel;
using System.Diagnostics;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

public interface ILocalHostNameResolverCacheRefresher
{
    Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default);
}

public sealed class LocalHostNameResolverCacheRefresher(
    ILocalHostNameResolverCacheRefreshCommandRunner? commandRunner = null) : ILocalHostNameResolverCacheRefresher
{
    private readonly ILocalHostNameResolverCacheRefreshCommandRunner commandRunner =
        commandRunner ?? new ProcessLocalHostNameResolverCacheRefreshCommandRunner();

    public Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default) =>
        RefreshAsync(GetCurrentPlatform(), cancellationToken);

    public async Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        LocalHostNameResolverRefreshPlatform platform,
        CancellationToken cancellationToken = default)
    {
        var commands = GetPlatformCommands(platform);
        if (commands.Count == 0)
        {
            return LocalHostNameResolverRefreshResult.NotAttempted(
                "Resolver cache was not refreshed because this operating system has no configured refresh command.");
        }

        var failures = new List<string>();
        foreach (var command in commands)
        {
            var result = await commandRunner.RunAsync(command, cancellationToken).ConfigureAwait(false);
            if (result.Succeeded)
            {
                return LocalHostNameResolverRefreshResult.Success(
                    $"Refreshed resolver cache using `{command.DisplayName}`.");
            }

            failures.Add(result.Message);
        }

        return LocalHostNameResolverRefreshResult.Failed(
            $"Could not refresh resolver cache automatically. Tried: {string.Join("; ", failures)}.");
    }

    public static IReadOnlyList<LocalHostNameResolverCacheRefreshCommand> GetPlatformCommands(
        LocalHostNameResolverRefreshPlatform platform) =>
        platform switch
        {
            LocalHostNameResolverRefreshPlatform.MacOS =>
            [
                new("dscacheutil", ["-flushcache"]),
                new("killall", ["-HUP", "mDNSResponder"])
            ],
            LocalHostNameResolverRefreshPlatform.Windows =>
            [
                new("ipconfig", ["/flushdns"])
            ],
            LocalHostNameResolverRefreshPlatform.Linux =>
            [
                new("resolvectl", ["flush-caches"]),
                new("systemd-resolve", ["--flush-caches"]),
                new("nscd", ["-i", "hosts"])
            ],
            _ => []
        };

    private static LocalHostNameResolverRefreshPlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsMacOS())
        {
            return LocalHostNameResolverRefreshPlatform.MacOS;
        }

        if (OperatingSystem.IsWindows())
        {
            return LocalHostNameResolverRefreshPlatform.Windows;
        }

        if (OperatingSystem.IsLinux())
        {
            return LocalHostNameResolverRefreshPlatform.Linux;
        }

        return LocalHostNameResolverRefreshPlatform.Unsupported;
    }
}

public interface ILocalHostNameResolverCacheRefreshCommandRunner
{
    Task<LocalHostNameResolverCacheRefreshCommandResult> RunAsync(
        LocalHostNameResolverCacheRefreshCommand command,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessLocalHostNameResolverCacheRefreshCommandRunner :
    ILocalHostNameResolverCacheRefreshCommandRunner
{
    public async Task<LocalHostNameResolverCacheRefreshCommandResult> RunAsync(
        LocalHostNameResolverCacheRefreshCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo(command.FileName)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            foreach (var argument in command.Arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return LocalHostNameResolverCacheRefreshCommandResult.Failed(
                    $"`{command.DisplayName}` could not be started.");
            }

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            await standardOutputTask.ConfigureAwait(false);
            var error = await standardErrorTask.ConfigureAwait(false);
            if (process.ExitCode == 0)
            {
                return LocalHostNameResolverCacheRefreshCommandResult.Success(
                    $"`{command.DisplayName}` succeeded.");
            }

            var detail = string.IsNullOrWhiteSpace(error)
                ? $"exited with code {process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
                : error.Trim();
            return LocalHostNameResolverCacheRefreshCommandResult.Failed(
                $"`{command.DisplayName}` {detail}.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Win32Exception exception)
        {
            return LocalHostNameResolverCacheRefreshCommandResult.Failed(
                $"`{command.DisplayName}` is unavailable: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return LocalHostNameResolverCacheRefreshCommandResult.Failed(
                $"`{command.DisplayName}` could not be started: {exception.Message}");
        }
    }
}

public enum LocalHostNameResolverRefreshPlatform
{
    Unsupported,
    MacOS,
    Windows,
    Linux
}

public sealed record LocalHostNameResolverCacheRefreshCommand(
    string FileName,
    IReadOnlyList<string> Arguments)
{
    public string DisplayName =>
        Arguments.Count == 0
            ? FileName
            : $"{FileName} {string.Join(' ', Arguments)}";
}

public sealed record LocalHostNameResolverCacheRefreshCommandResult(
    bool Succeeded,
    string Message)
{
    public static LocalHostNameResolverCacheRefreshCommandResult Success(string message) =>
        new(true, message);

    public static LocalHostNameResolverCacheRefreshCommandResult Failed(string message) =>
        new(false, message);
}

public sealed record LocalHostNameResolverRefreshResult(
    bool Attempted,
    bool Succeeded,
    string Message)
{
    public static LocalHostNameResolverRefreshResult NotAttempted(string message) =>
        new(false, false, message);

    public static LocalHostNameResolverRefreshResult Success(string message) =>
        new(true, true, message);

    public static LocalHostNameResolverRefreshResult Failed(string message) =>
        new(true, false, message);
}
