using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceWebAppContainerRuntimeTests
{
    [Fact]
    public async Task Start_ProjectsProviderConfigurationIntoOwnedContainer()
    {
        var definitionsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-container-runtime-test-{Guid.NewGuid():N}");
        var containerHost = new RecordingContainerHostRuntime();
        await using var runtime = new ResourceWebAppContainerRuntime(containerHost);
        var resource = new ResourceResolver(
            [ConfigurationStoreResourceTypeProvider.ClassDefinition],
            [new ConfigurationStoreResourceTypeProvider().TypeDefinition])
            .Resolve(new ResourceState(
                "settings",
                ConfigurationStoreResourceTypeProvider.ResourceTypeId,
                Attributes: new Dictionary<ResourceAttributeId, string>
                {
                    [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                        "http://localhost:55138"
                }));

        try
        {
            var diagnostics = await runtime.ExecuteAsync(
                resource,
                "start",
                ConfigurationStoreResourceTypeProvider.Attributes.Endpoint,
                new ResourceWebAppContainerOptions(
                    "cloudshell/configuration-store:test",
                    "configuration-store",
                    "CloudShell__ConfigurationStoreService__DefinitionsPath",
                    "CloudShell__ConfigurationStoreService__ResourceId",
                    "configuration-stores.json",
                    TimeSpan.FromMilliseconds(1))
                {
                    DefinitionsDirectory = definitionsDirectory,
                    EnvironmentVariables = new Dictionary<string, string>
                    {
                        ["Authentication__BuiltInAuthority__Enabled"] = "true"
                    }
                },
                (definition, endpoint) => new
                {
                    id = definition.EffectiveResourceId,
                    endpoint
                },
                "configuration.store",
                "Configuration Store");

            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Code == "configuration.store.serviceNotReady");
            var specification = Assert.IsType<ContainerHostContainerSpec>(containerHost.Container);
            Assert.Equal("cloudshell/configuration-store:test", specification.Image);
            Assert.Equal(
                "cloudshell-configuration-store-configuration-store-settings",
                specification.Name);
            Assert.Equal(
                "http://0.0.0.0:8080",
                specification.EnvironmentVariables!["ASPNETCORE_URLS"]);
            Assert.Equal(
                "/cloudshell/definitions/configuration-stores.json",
                specification.EnvironmentVariables[
                    "CloudShell__ConfigurationStoreService__DefinitionsPath"]);
            Assert.Equal(
                resource.EffectiveResourceId,
                specification.EnvironmentVariables[
                    "CloudShell__ConfigurationStoreService__ResourceId"]);
            var port = Assert.Single(specification.PublishedPorts!);
            Assert.Equal("127.0.0.1", port.HostAddress);
            Assert.Equal(55138, port.HostPort);
            Assert.Equal(8080, port.ContainerPort);
            var mount = Assert.Single(specification.Mounts!);
            Assert.True(mount.ReadOnly);
            Assert.Equal("/cloudshell/definitions", mount.Target);
            Assert.True(File.Exists(Path.Combine(mount.Source, "configuration-stores.json")));
            Assert.Equal(specification.Name, containerHost.RemovedContainer?.ContainerName);
        }
        finally
        {
            if (Directory.Exists(definitionsDirectory))
            {
                Directory.Delete(definitionsDirectory, recursive: true);
            }
        }
    }

    private sealed class RecordingContainerHostRuntime : IContainerHostRuntime
    {
        public ContainerHostContainerSpec? Container { get; private set; }

        public ContainerHostRuntimeHandle? RemovedContainer { get; private set; }

        public Task<ContainerHostOperationResult> RunContainerAsync(
            ContainerHostContainerSpec container,
            CancellationToken cancellationToken = default)
        {
            Container = container;
            return Task.FromResult(new ContainerHostOperationResult(0, "container-id", string.Empty));
        }

        public Task<ContainerHostInspectionResult> InspectContainerAsync(
            ContainerHostRuntimeHandle handle,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ContainerHostInspectionResult(null, "not found"));

        public Task<ContainerHostOperationResult> RemoveContainerAsync(
            ContainerHostRuntimeHandle handle,
            CancellationToken cancellationToken = default)
        {
            RemovedContainer = handle;
            return Task.FromResult(new ContainerHostOperationResult(0, string.Empty, string.Empty));
        }

        public Task<ContainerHostOperationResult> EnsureNetworkAsync(
            ContainerHostNetworkSpec network,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ContainerHostOperationResult> RemoveNetworkAsync(
            ContainerHostNetworkSpec network,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ContainerHostOperationResult> StartContainerAsync(
            ContainerHostRuntimeHandle handle,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ContainerHostOperationResult> ExecuteContainerAsync(
            ContainerHostRuntimeHandle handle,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ContainerHostOperationResult> ReadContainerLogsAsync(
            ContainerHostLogRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
