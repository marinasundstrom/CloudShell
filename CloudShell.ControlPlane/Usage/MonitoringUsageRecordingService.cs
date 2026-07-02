using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Usage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Usage;

public sealed class MonitoringUsageRecordingService(
    IServiceScopeFactory scopes,
    IHostApplicationLifetime applicationLifetime,
    IOptionsMonitor<UsageRecordingOptions> options,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private bool recordingFailureLogged;
    private readonly ILogger logger = loggerFactory.CreateLogger(CloudShellLogCategories.UsageRecording);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForApplicationStartedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.Enabled)
            {
                await RecordAsync(stoppingToken);
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

    private async Task RecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var resourceManager = scope.ServiceProvider.GetRequiredService<IResourceManager>();
            var monitoring = scope.ServiceProvider.GetRequiredService<IResourceMonitoringManager>();
            var usage = scope.ServiceProvider.GetRequiredService<IUsageManager>();
            var mapper = scope.ServiceProvider.GetRequiredService<MonitoringUsageSampleMapper>();
            var currentOptions = options.CurrentValue;
            var maxResources = Math.Clamp(currentOptions.MaxResourcesPerCycle, 1, 10_000);
            var samples = new List<UsageSample>();

            foreach (var resource in (await resourceManager.ListResourcesAsync(cancellationToken: cancellationToken))
                .Take(maxResources))
            {
                var snapshot = await monitoring.GetResourceMonitoringAsync(resource.Id, cancellationToken);
                if (snapshot is null || snapshot.Metrics.Count == 0)
                {
                    continue;
                }

                samples.AddRange(mapper.Map(snapshot, currentOptions));
            }

            if (samples.Count > 0)
            {
                await usage.RecordUsageSamplesAsync(samples, cancellationToken);
            }

            recordingFailureLogged = false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!recordingFailureLogged)
            {
                logger.LogWarning(
                    "Usage recording from monitoring failed; further failures are suppressed until recording succeeds: {Message}",
                    exception.Message);
                recordingFailureLogged = true;
            }
        }
    }

    private TimeSpan GetInterval() =>
        TimeSpan.FromSeconds(Math.Clamp(options.CurrentValue.PollIntervalSeconds, 5, 3600));

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
}
