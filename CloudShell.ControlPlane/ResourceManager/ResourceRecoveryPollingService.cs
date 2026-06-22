using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceRecoveryPollingService(
    IServiceScopeFactory scopes,
    IResourceRecoveryStore recoveryStore,
    IResourceOrchestrationSettings orchestrationSettings,
    IHostApplicationLifetime applicationLifetime,
    IOptionsMonitor<ResourceRecoveryOptions> options,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private bool pollingFailureLogged;
    private readonly ILogger logger = loggerFactory.CreateLogger(CloudShellLogCategories.ResourceRecoveryPolling);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.EnableLocalPolling)
            {
                await RefreshAsync(stoppingToken);
            }

            try
            {
                await Task.Delay(GetInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var resourceIds = recoveryStore.GetPolicies()
            .Where(policy => policy.Value.Enabled)
            .Select(policy => policy.Key)
            .ToArray();
        if (resourceIds.Length == 0)
        {
            return;
        }

        try
        {
            using var scope = scopes.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<IResourceRecoveryManager>();
            foreach (var resourceId in resourceIds)
            {
                await RefreshResourceAsync(recovery, resourceId, cancellationToken);
            }

            pollingFailureLogged = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LogPollingFailure(exception);
        }
    }

    private async Task RefreshResourceAsync(
        IResourceRecoveryManager recovery,
        string resourceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await recovery.RefreshResourceRecoveryAsync(resourceId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Resource recovery polling failed for resource '{ResourceId}': {Message}",
                resourceId,
                exception.Message);
        }
    }

    private void LogPollingFailure(Exception exception)
    {
        if (pollingFailureLogged)
        {
            return;
        }

        logger.LogWarning(
            "Resource recovery polling failed; further polling failures are suppressed until polling succeeds: {Message}",
            exception.Message);
        pollingFailureLogged = true;
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

    private TimeSpan GetInterval()
    {
        var configured = options.CurrentValue.PollIntervalSeconds;
        var seconds = configured is null
            ? orchestrationSettings.GetHealthCheckIntervalSettings().Seconds
            : ResourceOrchestratorSelectionDefaults.NormalizeHealthCheckInterval(configured.Value);
        return TimeSpan.FromSeconds(seconds);
    }
}
