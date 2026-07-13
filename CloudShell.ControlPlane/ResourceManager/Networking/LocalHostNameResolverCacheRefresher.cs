using System.ComponentModel;
using System.Diagnostics;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

public interface ILocalHostNameResolverCacheRefresher
{
    Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default);
}

public sealed class LocalHostNameResolverCacheRefresher : ILocalHostNameResolverCacheRefresher
{
    private readonly ILocalHostNameResolverCacheRefreshCommandRunner commandRunner;
    private readonly ILocalHostNameResolverCacheRefreshCommandPlanner commandPlanner;

    public LocalHostNameResolverCacheRefresher(
        ILocalHostNameResolverCacheRefreshCommandRunner? commandRunner = null,
        ILocalHostNameResolverCacheRefreshCommandPlanner? commandPlanner = null)
    {
        this.commandRunner = commandRunner ?? new ProcessLocalHostNameResolverCacheRefreshCommandRunner();
        this.commandPlanner = commandPlanner ??
            new LocalHostNameResolverCacheRefreshCommandPlanner(
                HostOperatingSystem.Current,
                new PathHostToolResolver());
    }

    public Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var plan = commandPlanner.CreatePlan();
        return plan.Commands.Count == 0
            ? Task.FromResult(LocalHostNameResolverRefreshResult.NotAttempted(plan.UnavailableReason))
            : RefreshAsync(plan.Commands, cancellationToken);
    }

    public async Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        LocalHostNameResolverRefreshPlatform platform,
        CancellationToken cancellationToken = default) =>
        await RefreshAsync(GetPlatformCommands(platform), cancellationToken).ConfigureAwait(false);

    private async Task<LocalHostNameResolverRefreshResult> RefreshAsync(
        IReadOnlyList<LocalHostNameResolverCacheRefreshCommand> commands,
        CancellationToken cancellationToken)
    {
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
}

public interface ILocalHostNameResolverCacheRefreshCommandPlanner
{
    LocalHostNameResolverCacheRefreshPlan CreatePlan();
}

public sealed class LocalHostNameResolverCacheRefreshCommandPlanner(
    HostOperatingSystem hostOperatingSystem,
    IHostToolResolver toolResolver) : ILocalHostNameResolverCacheRefreshCommandPlanner
{
    public LocalHostNameResolverCacheRefreshPlan CreatePlan()
    {
        var platform = GetPlatform(hostOperatingSystem);
        var commands = LocalHostNameResolverCacheRefresher
            .GetPlatformCommands(platform)
            .Where(command => toolResolver.IsAvailable(command.FileName))
            .ToArray();
        if (commands.Length > 0)
        {
            return LocalHostNameResolverCacheRefreshPlan.Available(commands);
        }

        return LocalHostNameResolverCacheRefreshPlan.Unavailable(GetUnavailableReason(platform));
    }

    private static LocalHostNameResolverRefreshPlatform GetPlatform(HostOperatingSystem operatingSystem) =>
        operatingSystem.Kind switch
        {
            HostOperatingSystemKind.MacOS => LocalHostNameResolverRefreshPlatform.MacOS,
            HostOperatingSystemKind.Windows => LocalHostNameResolverRefreshPlatform.Windows,
            HostOperatingSystemKind.Linux => LocalHostNameResolverRefreshPlatform.Linux,
            _ => LocalHostNameResolverRefreshPlatform.Unsupported
        };

    private static string GetUnavailableReason(LocalHostNameResolverRefreshPlatform platform) =>
        platform switch
        {
            LocalHostNameResolverRefreshPlatform.MacOS =>
                "Resolver cache was not refreshed because no supported macOS resolver cache tool is available.",
            LocalHostNameResolverRefreshPlatform.Windows =>
                "Resolver cache was not refreshed because no supported Windows resolver cache tool is available.",
            LocalHostNameResolverRefreshPlatform.Linux =>
                "Resolver cache was not refreshed because no supported Linux resolver cache tool is available. Install resolvectl, systemd-resolve, or nscd, or disable resolver refresh.",
            _ =>
                "Resolver cache was not refreshed because this operating system has no configured refresh command."
        };
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

public sealed record LocalHostNameResolverCacheRefreshPlan(
    IReadOnlyList<LocalHostNameResolverCacheRefreshCommand> Commands,
    string UnavailableReason)
{
    public static LocalHostNameResolverCacheRefreshPlan Available(
        IReadOnlyList<LocalHostNameResolverCacheRefreshCommand> commands) =>
        new(commands, string.Empty);

    public static LocalHostNameResolverCacheRefreshPlan Unavailable(string reason) =>
        new([], reason);
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
