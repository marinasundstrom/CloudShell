using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceHealthPollingService(
    IServiceScopeFactory scopes,
    IResourceOrchestrationSettings orchestrationSettings,
    ILogger<ResourceHealthPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RefreshAsync(stoppingToken);

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
        try
        {
            using var scope = scopes.CreateScope();
            var health = scope.ServiceProvider.GetRequiredService<IResourceHealthManager>();
            await health.ListResourceHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Resource health polling failed.");
        }
    }

    private TimeSpan GetInterval() =>
        TimeSpan.FromSeconds(orchestrationSettings.GetHealthCheckIntervalSettings().Seconds);
}
