using System.Security.Claims;
using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Shell;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class ControlPlaneUserSettingsProviderTests
{
    [Fact]
    public async Task SettingsAreIsolatedByAuthenticatedUser()
    {
        var contentRoot = CreateContentRoot();
        var accessor = new HttpContextAccessor();
        var settings = new ControlPlaneUserSettingsProvider(
            accessor,
            new AllowAllAuthorizationService(),
            new TestHostEnvironment(contentRoot));

        accessor.HttpContext = CreateHttpContext("user-1");
        await settings.SetSettingAsync("shell.theme", "Dark");

        accessor.HttpContext = CreateHttpContext("user-2");
        await settings.SetSettingAsync("shell.theme", "Light");

        accessor.HttpContext = CreateHttpContext("user-1");
        var firstSetting = await settings.GetSettingAsync("shell.theme");

        accessor.HttpContext = CreateHttpContext("user-2");
        var secondSetting = await settings.GetSettingAsync("shell.theme");

        Assert.Equal("Dark", firstSetting?.Value);
        Assert.Equal("Light", secondSetting?.Value);
    }

    [Fact]
    public async Task SettingsUseLocalProfileWhenAuthenticationIsNotEnabled()
    {
        var contentRoot = CreateContentRoot();
        var settings = new ControlPlaneUserSettingsProvider(
            new HttpContextAccessor(),
            new AuthenticationDisabledAuthorizationService(),
            new TestHostEnvironment(contentRoot));

        await settings.SetSettingAsync("shell.navigation.collapsed", "true");

        var setting = await settings.GetSettingAsync("shell.navigation.collapsed");
        Assert.Equal("true", setting?.Value);
    }

    private static DefaultHttpContext CreateHttpContext(string userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId)],
            "Test");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
    }

    private static string CreateContentRoot()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        return contentRoot;
    }

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class AuthenticationDisabledAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
