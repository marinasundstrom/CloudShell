using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceReplicaGroupReconciliationPollingService(
    IServiceScopeFactory scopes,
    IResourceOrchestrationSettings orchestrationSettings,
    IHostApplicationLifetime applicationLifetime,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private bool pollingFailureLogged;
    private readonly ILogger logger = loggerFactory.CreateLogger(CloudShellLogCategories.ResourceReplicaManagement);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessPendingAsync(stoppingToken);

            try
            {
                await Task.Delay(GetInterval(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var reconciliation = scope.ServiceProvider.GetRequiredService<ResourceReplicaGroupReconciliationService>();
            await reconciliation.ProcessPendingAsync(cancellationToken: cancellationToken);
            pollingFailureLogged = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!pollingFailureLogged)
            {
                logger.LogWarning(
                    "Replica group reconciliation polling failed; further failures are suppressed until polling succeeds: {Message}",
                    exception.Message);
                pollingFailureLogged = true;
            }
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

    private TimeSpan GetInterval() =>
        TimeSpan.FromSeconds(orchestrationSettings.GetHealthCheckIntervalSettings().Seconds);
}
