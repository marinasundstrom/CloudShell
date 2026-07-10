using System.Text;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.DeploymentArtifacts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Tests;

public sealed class DeploymentArtifactStoreTests
{
    [Fact]
    public void GetStatus_DefaultsToDisabled()
    {
        var store = CreateStore(new());

        var status = store.GetStatus();

        Assert.False(status.IsEnabled);
        Assert.Equal(DeploymentArtifactStoreKinds.Disabled, status.Kind);
        Assert.Equal(256L * 1024L * 1024L, status.MaxUploadBytes);
        Assert.Equal(["tar.gz", "zip"], status.AllowedPackageKinds);
    }

    [Fact]
    public async Task CompleteUpload_CommitsRevisionAndComputesHash()
    {
        var contentRoot = CreateTempDirectory();
        var store = CreateStore(
            new DeploymentArtifactOptions
            {
                Store = new()
                {
                    Kind = DeploymentArtifactStoreKinds.FileSystem,
                    RootPath = "Data/deployment-artifacts",
                    MaxUploadBytes = 1024,
                    AllowedPackageKinds = ["zip"]
                }
            },
            contentRoot);
        var bytes = Encoding.UTF8.GetBytes("print('hello')");

        var upload = await store.CreateUploadSessionAsync(
            new(
                "application.python-app",
                "api",
                "zip",
                ContentLength: bytes.Length,
                ArtifactLayoutKind: "pythonSourceDirectory"));
        await store.WriteUploadContentAsync(
            "api",
            upload.UploadId,
            new MemoryStream(bytes));

        var revision = await store.CompleteUploadAsync("api", new(upload.UploadId));

        Assert.Equal("deployment-artifact:api", revision.ArtifactId);
        Assert.Equal("zip", revision.PackageKind);
        Assert.Equal("pythonSourceDirectory", revision.ArtifactLayoutKind);
        Assert.Equal(bytes.Length, revision.SizeBytes);
        Assert.Equal(
            "96f43d529af3430cb6b0e2c02f6b38ef1a121e8a31d2d09a3ebb716f2f35c9de",
            revision.ContentSha256);
        Assert.DoesNotContain(contentRoot, revision.ArtifactId, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.GetRevisionAsync("api", revision.ArtifactId, revision.RevisionId);
        Assert.Equal(revision, loaded);
        await using var content = await store.OpenRevisionContentAsync("api", revision.ArtifactId, revision.RevisionId);
        using var reader = new StreamReader(content, Encoding.UTF8);
        Assert.Equal("print('hello')", await reader.ReadToEndAsync());
        Assert.False(Directory.Exists(Path.Combine(contentRoot, "Data", "deployment-artifacts", ".staging", upload.UploadId)));
    }

    [Fact]
    public async Task WriteUploadContent_RejectsContentPastConfiguredLimit()
    {
        var store = CreateStore(
            new DeploymentArtifactOptions
            {
                Store = new()
                {
                    Kind = DeploymentArtifactStoreKinds.FileSystem,
                    RootPath = "artifacts",
                    MaxUploadBytes = 4,
                    AllowedPackageKinds = ["zip"]
                }
            });
        var upload = await store.CreateUploadSessionAsync(
            new("application.executable", "tool", "zip"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.WriteUploadContentAsync(
                "tool",
                upload.UploadId,
                new MemoryStream(Encoding.UTF8.GetBytes("12345"))));
    }

    [Fact]
    public async Task UploadOperations_RejectResourceMismatch()
    {
        var store = CreateStore(
            new DeploymentArtifactOptions
            {
                Store = new()
                {
                    Kind = DeploymentArtifactStoreKinds.FileSystem,
                    RootPath = "artifacts",
                    AllowedPackageKinds = ["zip"]
                }
            });
        var upload = await store.CreateUploadSessionAsync(
            new("application.executable", "tool", "zip", ResourceId: "application.executable:tool"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.WriteUploadContentAsync(
                "application.executable:other",
                upload.UploadId,
                new MemoryStream(Encoding.UTF8.GetBytes("123"))));
    }

    [Fact]
    public async Task CreateUploadSession_RejectsDisallowedPackageKind()
    {
        var store = CreateStore(
            new DeploymentArtifactOptions
            {
                Store = new()
                {
                    Kind = DeploymentArtifactStoreKinds.FileSystem,
                    RootPath = "artifacts",
                    AllowedPackageKinds = ["zip"]
                }
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.CreateUploadSessionAsync(
                new("application.python-app", "api", "tar.gz")));
    }

    private static FileSystemDeploymentArtifactStore CreateStore(
        DeploymentArtifactOptions options,
        string? contentRoot = null) =>
        new(
            Options.Create(options),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment(contentRoot ?? CreateTempDirectory()));

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cloudshell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
