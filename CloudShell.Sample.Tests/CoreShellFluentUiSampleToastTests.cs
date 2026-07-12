using CoreShell;

namespace CloudShell.Sample.Tests;

public sealed class CoreShellFluentUiSampleToastTests
{
    [Fact]
    public async Task InMemoryToastService_ExpiresPlainToastsAfterTimeToLive()
    {
        var service = new InMemoryCoreShellToastService();

        await service.PublishAsync(new CoreShellToastRequest(
            "Saved",
            "The sample operation completed.",
            CoreShellNotificationSeverity.Success,
            CoreShellNotificationStatus.Succeeded,
            TimeToLive: TimeSpan.Zero));

        Assert.Empty(await service.GetToastsAsync());
    }

    [Fact]
    public async Task InMemoryToastService_KeepsInProgressToastsUntilProgressCompletes()
    {
        var service = new InMemoryCoreShellToastService();

        var toast = await service.PublishAsync(new CoreShellToastRequest(
            "Running",
            "The sample operation is running.",
            CoreShellNotificationSeverity.Info,
            CoreShellNotificationStatus.InProgress,
            TimeToLive: TimeSpan.Zero));

        Assert.Collection(
            await service.GetToastsAsync(),
            item => Assert.Equal(toast.Id, item.Id));

        await service.UpdateAsync(
            toast.Id,
            new CoreShellToastUpdate(
                Title: "Completed",
                Message: "The sample operation completed.",
                Severity: CoreShellNotificationSeverity.Success,
                Status: CoreShellNotificationStatus.Succeeded));

        Assert.Empty(await service.GetToastsAsync());
    }

    [Fact]
    public async Task InMemoryToastService_KeepsNeverAutoDismissToastsUntilDismissed()
    {
        var service = new InMemoryCoreShellToastService();

        var toast = await service.PublishAsync(new CoreShellToastRequest(
            "Saved",
            "The sample operation completed.",
            CoreShellNotificationSeverity.Success,
            CoreShellNotificationStatus.Succeeded,
            TimeToLive: TimeSpan.Zero,
            AutoDismiss: CoreShellToastAutoDismissBehavior.Never));

        Assert.Collection(
            await service.GetToastsAsync(),
            item => Assert.Equal(toast.Id, item.Id));

        await service.DismissAsync(toast.Id);

        Assert.Empty(await service.GetToastsAsync());
    }
}
