using CloudShell.Abstractions.Authorization;
using CloudShell.Hosting.ResourceManager;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class OrganizationDashboardSettingsTests
{
    [Fact]
    public async Task OrganizationLayoutRequiresPermissionAndIsSharedAcrossUsers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cloudshell-dashboard-test-" + Guid.NewGuid());
        try
        {
            var env = new TestEnvironment { ContentRootPath = directory };
            var admin = new OrganizationDashboardSettings(env, new Authorization(true));
            var reader = new OrganizationDashboardSettings(env, new Authorization(false));
            const string layout = "{\"Version\":1,\"Widgets\":[\"status\"]}";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.SetAsync("resource", layout));
            Assert.Null(await reader.GetAsync("resource"));
            await admin.SetAsync("resource", layout);
            Assert.Equal(layout, await reader.GetAsync("resource"));
            Assert.Null(await reader.GetAsync("other-resource"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private sealed class Authorization(bool allowed) : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => allowed && permission == CloudShellPermissions.Shell.Configure;
        public bool CanAccessResourceGroup(string? group, string permission) => false;
        public bool CanAccessResource(string resource, string? group, string permission) => false;
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
