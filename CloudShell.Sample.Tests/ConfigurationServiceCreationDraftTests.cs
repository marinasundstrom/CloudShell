using System.Text.Json;
using CloudShell.ControlPlane.Providers.UI.Configuration.Pages;

namespace CloudShell.Sample.Tests;

public sealed class ConfigurationServiceCreationDraftTests
{
    [Theory]
    [InlineData("")]
    [InlineData("/relative")]
    [InlineData("ftp://example.test")]
    [InlineData("https://user:password@example.test")]
    [InlineData("https://example.test?token=test")]
    [InlineData("https://example.test#fragment")]
    public void CriticalEndpointRejectsMissingInvalidAndCredentialBearingValues(string endpoint)
    {
        Assert.NotNull(new ConfigurationServiceCreationDraft { Endpoint = endpoint }.ValidateEndpoint());
    }

    [Theory]
    [InlineData("http://localhost:7152")]
    [InlineData("https://service.example.test/vault")]
    public void ExplicitServiceEndpointsAreAccepted(string endpoint)
    {
        Assert.Null(new ConfigurationServiceCreationDraft { Endpoint = endpoint }.ValidateEndpoint());
    }

    [Fact]
    public void DraftRoundTripRetainsEndpointAndDeferredGroupWithoutCreatingIt()
    {
        var draft = new ConfigurationServiceCreationDraft { Name = "team-vault", Endpoint = "https://vault.example.test" };
        draft.Group.CreateNew = true;
        draft.Group.Name = "Team";
        draft.Group.Description = "Development";
        var restored = JsonSerializer.Deserialize<ConfigurationServiceCreationDraft>(JsonSerializer.Serialize(draft))!;
        Assert.Equal(draft.Name, restored.Name);
        Assert.Equal(draft.Endpoint, restored.Endpoint);
        Assert.True(restored.Group.CreateNew);
        Assert.Equal("Team", restored.Group.Name);
        Assert.Equal("Development", restored.Group.Description);
        Assert.Null(restored.Group.CreatedGroupId);
        Assert.Null(restored.ValidateBasics());
        restored.Group.Name = "";
        Assert.NotNull(restored.ValidateBasics());
        restored.Name = "";
        Assert.Equal("Name is required.", restored.ValidateBasics());
    }
}
