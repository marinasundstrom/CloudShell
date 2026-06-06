using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Docker;

internal sealed class DockerDiscoveryService(
    DockerContainerResourceProvider provider,
    DockerProviderOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await provider.RefreshAsync(stoppingToken);
            await Task.Delay(options.RefreshInterval, stoppingToken);
        }
    }
}
