using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using CloudShell.DeviceRegistryService;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Tests;

public sealed class DeviceRegistryServiceStoreTests
{
    [Fact]
    public void EnrollDevice_RejectsMatchingClaimsWithoutEnrollmentProof()
    {
        using var directory = TestDirectory.Create();
        var identities = new BuiltInResourceIdentityRegistry();
        var store = CreateStore(directory.Path, identities, enrollmentToken: "trusted-token");
        var registry = CreateRegistry();

        var result = store.EnrollDevice(
            registry,
            new DeviceEnrollmentRequest(
                "device/test-pc",
                new Dictionary<string, string>
                {
                    ["manufacturer"] = "cloudshell"
                }),
            DateTimeOffset.UtcNow);

        Assert.False(result.IsAccepted);
        Assert.Equal("Device enrollment proof is required.", result.Failure);
        Assert.Empty(identities.ListRegistrations());
    }

    [Fact]
    public void EnrollDevice_RejectsInvalidEnrollmentProof()
    {
        using var directory = TestDirectory.Create();
        var identities = new BuiltInResourceIdentityRegistry();
        var store = CreateStore(directory.Path, identities, enrollmentToken: "trusted-token");
        var registry = CreateRegistry();

        var result = store.EnrollDevice(
            registry,
            new DeviceEnrollmentRequest(
                "device/test-pc",
                new Dictionary<string, string>
                {
                    ["manufacturer"] = "cloudshell"
                },
                EnrollmentToken: "wrong-token"),
            DateTimeOffset.UtcNow);

        Assert.False(result.IsAccepted);
        Assert.Equal("Device enrollment proof is invalid.", result.Failure);
        Assert.Empty(identities.ListRegistrations());
    }

    [Fact]
    public void EnrollDevice_AcceptsValidEnrollmentProofAndRegistersDeviceIdentity()
    {
        using var directory = TestDirectory.Create();
        var identities = new BuiltInResourceIdentityRegistry();
        var store = CreateStore(directory.Path, identities, enrollmentToken: "trusted-token");
        var registry = CreateRegistry();

        var result = store.EnrollDevice(
            registry,
            new DeviceEnrollmentRequest(
                "device/test-pc",
                new Dictionary<string, string>
                {
                    ["manufacturer"] = "cloudshell"
                },
                EnrollmentToken: "trusted-token"),
            DateTimeOffset.UtcNow);

        Assert.True(result.IsAccepted);
        Assert.NotNull(result.Device);
        Assert.False(string.IsNullOrWhiteSpace(result.ClientSecret));
        Assert.True(identities.TryGetClient(result.Device.ClientId, out var client));
        var resourcePermission = Assert.Single(client.ResourcePermissions);
        Assert.Equal("configuration.store:device-settings", resourcePermission.ResourceId);
        Assert.Equal(ConfigurationStoreResourceOperationPermissions.ReadSettings, resourcePermission.Permission);
    }

    private static DeviceRegistryServiceStore CreateStore(
        string directory,
        BuiltInResourceIdentityRegistry identities,
        string? enrollmentToken) =>
        new(
            Options.Create(new DeviceRegistryServiceOptions
            {
                DefinitionsPath = Path.Combine(directory, "device-registries.json"),
                DevicesPath = Path.Combine(directory, "devices.json"),
                EnrollmentToken = enrollmentToken
            }),
            identities,
            new TestHostEnvironment(directory));

    private static DeviceRegistryDefinition CreateRegistry() =>
        new()
        {
            Id = "iot.device-registry:devices",
            EnrollmentProfiles =
            [
                new()
                {
                    Name = "default",
                    Kind = DeviceEnrollmentProfileKinds.Group,
                    Policy = new()
                    {
                        SubjectPrefixes = ["device/"],
                        RequiredClaims =
                        [
                            new("manufacturer", "cloudshell")
                        ]
                    },
                    PermissionGrants =
                    [
                        new(
                            "configuration.store:device-settings",
                            ConfigurationStoreResourceOperationPermissions.ReadSettings)
                    ]
                }
            ]
        };

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string path)
        {
            Path = path;
            Directory.CreateDirectory(path);
        }

        public string Path { get; }

        public static TestDirectory Create() =>
            new(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"cloudshell-device-registry-store-{Guid.NewGuid():N}"));

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
