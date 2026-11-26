using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceHost;
using Microsoft.Extensions.Configuration;

namespace CloudShell.Sample.Tests;

public sealed class SampleLifecyclePreviewTests
{
    [Fact]
    public async Task AbortStartupKeepsResourceStoppedAndAllowsRetry()
    {
        var provider = CreateProvider(120000);
        var starting = provider.ExecuteActionAsync(Context(provider), ResourceAction.Start);
        var resource = Worker(provider);
        Assert.Equal(ResourceState.Starting, resource.State);
        var abort = Assert.Single(resource.ResourceActions);
        Assert.Equal(ResourceActionKind.Stop, abort.Kind);
        Assert.Equal("Abort startup", abort.DisplayName);
        await provider.ExecuteActionAsync(Context(provider), abort);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.Equal(ResourceState.Stopped, Worker(provider).State);
        var retry = provider.ExecuteActionAsync(Context(provider), ResourceAction.Start);
        Assert.Equal(ResourceState.Starting, Worker(provider).State);
        await provider.ExecuteActionAsync(Context(provider), ResourceAction.Stop);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry);
        Assert.Equal(ResourceState.Stopped, Worker(provider).State);
    }

    [Fact]
    public async Task DefaultSampleStillStartsImmediately()
    {
        var provider = CreateProvider(0);
        await provider.ExecuteActionAsync(Context(provider), ResourceAction.Start);
        Assert.Equal(ResourceState.Running, Worker(provider).State);
    }

    private static SampleResourceProvider CreateProvider(int delay) => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["DesignPreview:StartupDelayMilliseconds"] = delay.ToString() }).Build());
    private static Resource Worker(SampleResourceProvider provider) => provider.GetResources().Single(r => r.Id == "sample:worker");
    private static ResourceProcedureContext Context(SampleResourceProvider provider) => new(Worker(provider), null, null, null!);
}
