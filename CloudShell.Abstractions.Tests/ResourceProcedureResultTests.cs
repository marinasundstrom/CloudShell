using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceProcedureResultTests
{
    [Fact]
    public void Combine_ReturnsEmptyMessageWhenNoResults()
    {
        var result = ResourceProcedureResult.Combine([], "No settings were updated.");

        Assert.Equal("No settings were updated.", result.Message);
        Assert.False(result.RestartRequired);
        Assert.Empty(result.Signals);
    }

    [Fact]
    public void Combine_ConcatenatesMessagesAndSignals()
    {
        var result = ResourceProcedureResult.Combine(
            [
                ResourceProcedureResult.Completed("App settings updated.") with
                {
                    Signals = [ResourceProcedureSignal.Warning("App setting warning.")]
                },
                ResourceProcedureResult.Completed("Environment variables updated.") with
                {
                    Signals = [ResourceProcedureSignal.Error("Environment variable error.")]
                }
            ],
            "No settings were updated.");

        Assert.Equal("App settings updated. Environment variables updated.", result.Message);
        Assert.False(result.RestartRequired);
        Assert.Collection(
            result.Signals,
            signal =>
            {
                Assert.Equal(ResourceSignalSeverity.Warning, signal.Severity);
                Assert.Equal("App setting warning.", signal.Message);
            },
            signal =>
            {
                Assert.Equal(ResourceSignalSeverity.Error, signal.Severity);
                Assert.Equal("Environment variable error.", signal.Message);
            });
    }

    [Fact]
    public void Combine_PreservesFirstRestartRequirement()
    {
        var result = ResourceProcedureResult.Combine(
            [
                ResourceProcedureResult.Completed("App settings updated."),
                ResourceProcedureResult.CompletedWithRestartRequired(
                    "Environment variables updated.",
                    "application:api",
                    "Restart API.")
            ],
            "No settings were updated.");

        Assert.Equal("App settings updated. Environment variables updated.", result.Message);
        Assert.True(result.RestartRequired);
        Assert.Equal("application:api", result.RestartResourceId);
        Assert.Equal("Restart API.", result.RestartMessage);
    }
}
