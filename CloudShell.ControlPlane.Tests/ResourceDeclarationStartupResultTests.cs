using CloudShell.ControlPlane.ResourceManager;

namespace CloudShell.ControlPlane.Tests;

public sealed class ResourceDeclarationStartupResultTests
{
    [Fact]
    public void ResourceDeclarationStartupResult_ClassifiesDiagnosticSeverities()
    {
        var result = new ResourceDeclarationStartupResult(
        [
            new ResourceDeclarationStartupDiagnostic("Information", "configuration:app", "ready"),
            ResourceDeclarationStartupDiagnostic.Warning("application:api", "warning"),
            ResourceDeclarationStartupDiagnostic.Error("application:frontend", "failed")
        ]);

        Assert.Equal(1, result.InformationCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.ErrorCount);
        Assert.True(result.HasWarnings);
        Assert.True(result.HasErrors);
    }
}
