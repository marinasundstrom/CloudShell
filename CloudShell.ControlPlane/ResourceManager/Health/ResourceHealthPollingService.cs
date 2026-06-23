using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager.Health;

public sealed class ResourceHealthPollingService(
    IServiceScopeFactory scopes,
    IResourceOrchestrationSettings orchestrationSettings,
    IHostApplicationLifetime applicationLifetime,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private bool pollingFailureLogged;
    private readonly ILogger logger = loggerFactory.CreateLogger(CloudShellLogCategories.ResourceHealthPolling);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = await RefreshAsync(stoppingToken);

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task<TimeSpan> RefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var health = scope.ServiceProvider.GetRequiredService<IResourceHealthManager>();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            await health.ListResourceHealthAsync(cancellationToken);
            var resources = await resourceManager.ListResourcesAsync(cancellationToken: cancellationToken);
            pollingFailureLogged = false;
            return GetPollingInterval(resources);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return GetDefaultInterval();
        }
        catch (Exception exception)
        {
            if (!pollingFailureLogged)
            {
                logger.LogWarning(
                    "Resource health polling failed; further failures are suppressed until polling succeeds: {Message}",
                    exception.Message);
                pollingFailureLogged = true;
            }

            return GetDefaultInterval();
        }
    }

    private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken)
    {
        if (applicationLifetime.ApplicationStarted.IsCancellationRequested)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            applicationLifetime.ApplicationStarted,
            stoppingToken);
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        }
        catch (OperationCanceledException) when (
            applicationLifetime.ApplicationStarted.IsCancellationRequested ||
            stoppingToken.IsCancellationRequested)
        {
        }
    }

    private TimeSpan GetDefaultInterval() =>
        TimeSpan.FromSeconds(orchestrationSettings.GetHealthCheckIntervalSettings().Seconds);

    private TimeSpan GetPollingInterval(IReadOnlyList<Resource> resources)
    {
        var defaultInterval = orchestrationSettings.GetHealthCheckIntervalSettings().Seconds;
        var intervalSeconds = defaultInterval;

        foreach (var check in resources.SelectMany(resource => resource.ResourceHealthChecks))
        {
            var checkInterval = check.IntervalSeconds is null
                ? defaultInterval
                : ResourceOrchestratorSelectionDefaults.NormalizeHealthCheckInterval(check.IntervalSeconds.Value);
            intervalSeconds = Math.Min(intervalSeconds, checkInterval);
        }

        return TimeSpan.FromSeconds(intervalSeconds);
    }
}
