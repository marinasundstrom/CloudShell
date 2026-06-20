using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceRegistrationNavigationContextTests
{
    [Theory]
    [InlineData("/resources")]
    [InlineData("/resources/application%3Aapi/endpoints")]
    [InlineData("/resources/application%3Aapi/details?tab=networking%3Aendpoints")]
    public void GetReturnUrlOrDefault_AllowsLocalReturnUrls(string returnUrl)
    {
        var context = new ResourceRegistrationNavigationContext(returnUrl);

        Assert.Equal(returnUrl, context.GetReturnUrlOrDefault());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com/resources")]
    [InlineData("//example.com/resources")]
    [InlineData("/resources\r\nLocation: https://example.com")]
    public void GetReturnUrlOrDefault_FallsBackForInvalidReturnUrls(string? returnUrl)
    {
        var context = new ResourceRegistrationNavigationContext(returnUrl);

        Assert.Equal(ResourceRegistrationNavigationContext.DefaultReturnUrl, context.GetReturnUrlOrDefault());
    }
}
