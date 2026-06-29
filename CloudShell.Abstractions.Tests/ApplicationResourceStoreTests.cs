using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceStoreTests
{
    [Fact]
    public void ApplicationResourceStore_LoadsPersistedHealthChecks()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var options = new ApplicationProviderOptions
            {
                DefinitionsPath = "application-resources.json"
            };
            var environment = new TestHostEnvironment(contentRoot);
            var store = new ApplicationResourceStore(options, environment);
            store.Save(
                new ApplicationResourceDefinition(
                    "application:api",
                    "API",
                    "dotnet",
                    "run",
                    "/workspace",
                    healthChecks:
                    [
                        new ResourceHealthCheck(
                            "/healthz",
                            ResourceProbeType.Health,
                            "http",
                            "health",
                            TimeSpan.FromSeconds(2),
                            IntervalSeconds: 15),
                        new ResourceHealthCheck(
                            new ResourceProbeSource(
                                "provider.process",
                                Metadata: new Dictionary<string, string>
                                {
                                    ["provider"] = "applications"
                                }),
                            ResourceProbeType.Liveness,
                            "process",
                            intervalSeconds: 30)
                    ]));

            var reloadedStore = new ApplicationResourceStore(options, environment);
            var application = reloadedStore.GetApplication("application:api");

            Assert.NotNull(application);
            Assert.Equal(2, application.HealthChecks.Count);
            Assert.Equal("/healthz", application.HealthChecks[0].Path);
            Assert.Equal(ResourceProbeType.Health, application.HealthChecks[0].Type);
            Assert.Equal("http", application.HealthChecks[0].EndpointName);
            Assert.Equal(TimeSpan.FromSeconds(2), application.HealthChecks[0].Timeout);
            Assert.Equal(15, application.HealthChecks[0].IntervalSeconds);
            Assert.Equal("provider.process", application.HealthChecks[1].EffectiveSource.Kind);
            Assert.Equal("applications", application.HealthChecks[1].EffectiveSource.Metadata?["provider"]);
            Assert.Equal(30, application.HealthChecks[1].IntervalSeconds);
        }
        finally
        {
            if (Directory.Exists(contentRoot))
            {
                Directory.Delete(contentRoot, recursive: true);
            }
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
