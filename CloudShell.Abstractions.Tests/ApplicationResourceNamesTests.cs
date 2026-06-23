using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceNamesTests
{
    [Theory]
    [InlineData("application:api", "application-api")]
    [InlineData(" My API ", "my-api")]
    [InlineData("!!!", "cloudshell")]
    public void CreateStableIdentifier_NormalizesResourceSafeIdentifiers(
        string value,
        string expected)
    {
        Assert.Equal(expected, ApplicationResourceNames.CreateStableIdentifier(value));
    }

    [Theory]
    [InlineData("application:api", 1, "runtime-container:application-api:replica-1")]
    [InlineData("application:api", 0, "runtime-container:application-api:replica-1")]
    [InlineData("application:My API", 3, "runtime-container:application-my-api:replica-3")]
    public void CreateRuntimeContainerResourceId_UsesStableApplicationIdentityAndReplicaOrdinal(
        string resourceId,
        int replica,
        string expected)
    {
        Assert.Equal(expected, ApplicationResourceNames.CreateRuntimeContainerResourceId(resourceId, replica));
    }
}
