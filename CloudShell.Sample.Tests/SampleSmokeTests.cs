using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Sample.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SampleSmokeCollection
{
    public const string Name = "Sample smoke tests";
}

[Collection(SampleSmokeCollection.Name)]
public sealed class SampleSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task ProjectReferenceHost_RendersResourcesAndServesControlPlaneApi()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Project Reference API", resourcesHtml);
        Assert.Contains("Project Reference Frontend", resourcesHtml);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var resources = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-api");
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-frontend");
    }

    [Fact]
    public async Task SettingsAndSecretsSample_ProjectsReferenceBackedEnvironmentResources()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var resources = document.RootElement.EnumerateArray().ToArray();
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:sample-app");
        var secrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:sample-app");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:settings-secrets-api");
        var dependsOn = api
            .GetProperty("dependsOn")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var identity = api.GetProperty("identity");

        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("secrets.vault", secrets.GetProperty("typeId").GetString());
        Assert.Contains("configuration:sample-app", dependsOn);
        Assert.Contains("secrets-vault:sample-app", dependsOn);
        Assert.Equal("identity:development", identity.GetProperty("providerId").GetString());
        Assert.Equal("settings-secrets-api", identity.GetProperty("name").GetString());

        var grantsJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants?identityResourceId=application%3Asettings-secrets-api&identityName=settings-secrets-api");
        using var grantsDocument = JsonDocument.Parse(grantsJson);
        var grants = grantsDocument.RootElement.EnumerateArray().ToArray();
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "secrets-vault:sample-app" &&
                grant.GetProperty("permission").GetString() == SecretsVaultResourceOperationPermissions.ReadSecrets);
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "configuration:sample-app" &&
                grant.GetProperty("permission").GetString() == ConfigurationStoreResourceOperationPermissions.ReadEntries);

        var provisioning = await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Asettings-secrets-api/identity/provision");
        using var provisioningDocument = JsonDocument.Parse(provisioning);
        Assert.Equal(
            "identity:development",
            provisioningDocument.RootElement.GetProperty("providerId").GetString());

        await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Asettings-secrets-api/actions/run?startDependencies=true");

        var startedApiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var startedDocument = JsonDocument.Parse(startedApiJson);
        var startedResources = startedDocument.RootElement.EnumerateArray().ToArray();
        settings = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "configuration:sample-app");
        secrets = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:sample-app");
        api = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "application:settings-secrets-api");

        var resourceToken = await host.GetClientCredentialsTokenAsync(
            "application:settings-secrets-api/settings-secrets-api",
            "local-development-settings-secrets-api-secret",
            "ControlPlane.Access");
        var apiEndpoint = GetPrimaryEndpointAddress(api);
        var settingsEndpoint = GetEndpointAddress(settings, "entries");
        var secretsEndpoint = GetEndpointAddress(secrets, "secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{apiEndpoint.TrimEnd('/')}/configuration",
            null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(settingsEndpoint, resourceToken, StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{secretsEndpoint.TrimEnd('/')}/sample-api-key",
            resourceToken,
            StartupTimeout);

        var settingsJson = await host.GetAbsoluteStringAsync(settingsEndpoint, resourceToken);
        using var settingsDocument = JsonDocument.Parse(settingsJson);
        Assert.Contains(
            settingsDocument.RootElement.EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");

        var secretJson = await host.GetAbsoluteStringAsync(
            $"{secretsEndpoint.TrimEnd('/')}/sample-api-key",
            resourceToken);
        using var secretDocument = JsonDocument.Parse(secretJson);
        Assert.Equal(
            "local-development-api-key",
            secretDocument.RootElement.GetProperty("value").GetString());

        var apiConfigurationJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/configuration");
        using var apiConfigurationDocument = JsonDocument.Parse(apiConfigurationJson);
        Assert.Equal(
            "connected",
            apiConfigurationDocument.RootElement.GetProperty("status").GetString());
        var apiEntries = apiConfigurationDocument.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(
            apiEntries,
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");
    }

    [Fact]
    public async Task SplitHostingSample_RendersUiThroughRemoteControlPlane()
    {
        var controlPlanePort = await GetFreePortAsync();
        var uiPort = await GetFreePortAsync();

        using var controlPlane = await SampleProcess.StartAsync(
            "samples/SplitHosting/ControlPlane/CloudShell.SplitHosting.ControlPlane.csproj",
            controlPlanePort,
            environment:
            [
                ("Authentication__BuiltInAuthority__Issuer", $"http://localhost:{controlPlanePort}")
            ]);
        await controlPlane.WaitForHttpOkAsync("/openapi/control-plane-v1.json", StartupTimeout);

        using var ui = await SampleProcess.StartAsync(
            "samples/SplitHosting/UI/CloudShell.SplitHosting.UI.csproj",
            uiPort,
            environment:
            [
                ("CloudShell__ControlPlane__BaseAddress", controlPlane.BaseAddress.ToString())
            ]);
        await ui.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await ui.GetStringAsync("/resources");
        Assert.Contains("Split Sample Network", resourcesHtml);

        var token = await controlPlane.GetClientCredentialsTokenAsync(
            "cloudshell-split-ui",
            "local-development-client-secret",
            "ControlPlane.Access");
        var apiJson = await controlPlane.GetStringAsync(
            "/api/control-plane/v1/resources",
            token);
        using var document = JsonDocument.Parse(apiJson);
        var resource = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("network:split-sample", resource.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ResourceHostSample_ExecutesResourceActionFromAdvertisedHref()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/CloudShell.ResourceHost/CloudShell.ResourceHost.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var apiResource = document.RootElement.EnumerateArray().Single(resource =>
            resource.GetProperty("id").GetString() == "sample:api");
        Assert.Equal((int)ResourceState.Running, apiResource.GetProperty("state").GetInt32());

        var stopAction = apiResource
            .GetProperty("resourceActions")
            .GetProperty("stop");
        Assert.Equal("POST", stopAction.GetProperty("method").GetString());
        var stopHref = stopAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The stop action did not include an href.");

        var actionJson = await host.SendAsync(HttpMethod.Post, stopHref);
        using var actionDocument = JsonDocument.Parse(actionJson);
        Assert.Contains(
            "Stop completed",
            actionDocument.RootElement.GetProperty("message").GetString());

        var stoppedJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("sample:api")}");
        using var stoppedDocument = JsonDocument.Parse(stoppedJson);
        var stoppedResource = stoppedDocument.RootElement;
        Assert.Equal((int)ResourceState.Stopped, stoppedResource.GetProperty("state").GetInt32());
        Assert.True(stoppedResource.GetProperty("resourceActions").TryGetProperty("run", out _));
    }

    [Fact]
    public async Task ContainerAppDeploymentSample_UpdatesMockImageTagThroughRevisionApi()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:sample-api");
        var registry = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:container:sample-registry");

        Assert.Equal("localhost:5023", app.GetProperty("attributes").GetProperty("container.registry").GetString());
        Assert.Equal("localhost:5023", registry.GetProperty("attributes").GetProperty("container.registry").GetString());

        var updateJson = await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/container-apps/v1/application%3Asample-api/revisions",
            """
            {
              "image": "cloudshell/mock-api:20260608.2",
              "restartIfRunning": false,
              "triggeredBy": "sample-smoke-test"
            }
            """);
        using var updateDocument = JsonDocument.Parse(updateJson);

        Assert.Contains(
            "cloudshell/mock-api:20260608.2",
            updateDocument.RootElement.GetProperty("message").GetString());

        var updatedJson = await host.GetStringAsync(
            "/api/control-plane/v1/resources/application%3Asample-api");
        using var updatedDocument = JsonDocument.Parse(updatedJson);
        var updatedAttributes = updatedDocument.RootElement.GetProperty("attributes");
        Assert.Equal(
            "cloudshell/mock-api:20260608.2",
            updatedAttributes.GetProperty("container.image").GetString());
        Assert.NotEqual(
            "unrevisioned",
            updatedAttributes.GetProperty("container.revision").GetString());
    }

    [Fact]
    public async Task HostVirtualNetworkSample_ProjectsVirtualNetworkAndHostProvider()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var network = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "network:sample-vnet");
        var attributes = network.GetProperty("attributes");

        Assert.Equal("cloudshell.virtualNetwork", network.GetProperty("typeId").GetString());
        Assert.Equal("providerRequired", attributes.GetProperty("network.hostReadiness").GetString());
        Assert.Equal("networking:host-macos", attributes.GetProperty("network.mappingProviders").GetString());

        if (OperatingSystem.IsMacOS())
        {
            Assert.Contains(resources, resource =>
                resource.GetProperty("id").GetString() == "networking:host-macos");
        }
    }

    [Fact]
    public async Task LoadBalancerSample_AppliesTraefikConfigurationFromAdvertisedAction()
    {
        var root = SampleProcess.FindRepositoryRoot();
        var dataDirectory = Path.Combine(root, "samples", "LoadBalancer", "Data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        using var host = await SampleProcess.StartAsync(
            "samples/LoadBalancer/CloudShell.LoadBalancer.csproj",
            await GetFreePortAsync(),
            [("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME", "true")]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var loadBalancer = Assert.Single(resourcesDocument.RootElement.EnumerateArray(), resource =>
            resource.GetProperty("id").GetString() == "load-balancer:public");
        var api = Assert.Single(resourcesDocument.RootElement.EnumerateArray(), resource =>
            resource.GetProperty("id").GetString() == "application:api");
        var postgres = Assert.Single(resourcesDocument.RootElement.EnumerateArray(), resource =>
            resource.GetProperty("id").GetString() == "application:postgres");
        var attributes = loadBalancer.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");
        var postgresAttributes = postgres.GetProperty("attributes");

        Assert.Equal("cloudshell.loadBalancer", loadBalancer.GetProperty("typeId").GetString());
        Assert.Equal("traefik", attributes.GetProperty("loadBalancer.provider").GetString());
        Assert.Equal("docker:sample-host", attributes.GetProperty("loadBalancer.hostResourceId").GetString());
        Assert.Equal("3", attributes.GetProperty("loadBalancer.routes").GetString());
        Assert.Equal(3, loadBalancer.GetProperty("loadBalancerRoutes").GetArrayLength());
        Assert.Equal("traefik/whoami:v1.10", apiAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", apiAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("postgres:16-alpine", postgresAttributes.GetProperty("container.image").GetString());

        var applyAction = loadBalancer
            .GetProperty("resourceActions")
            .GetProperty("applyLoadBalancerConfiguration");
        var applyHref = applyAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The load balancer apply action did not include an href.");

        var applyJson = await host.SendAsync(HttpMethod.Post, applyHref);
        using var applyDocument = JsonDocument.Parse(applyJson);
        Assert.Contains(
            "Applied Traefik configuration for 3 route(s)",
            applyDocument.RootElement.GetProperty("message").GetString());

        var configPath = Path.Combine(dataDirectory, "traefik", "load-balancer-public.dynamic.yml");
        var config = await File.ReadAllTextAsync(configPath);
        Assert.Contains("Host(`app.local`)", config);
        Assert.Contains("Host(`api.local`) && PathPrefix(`/v1`)", config);
        Assert.Contains("url: \"http://cloudshell-application-web:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-1:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-2:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-3:80\"", config);
        Assert.Contains("HostSNI(`*`)", config);
        Assert.Contains("address: \"cloudshell-application-postgres:5432\"", config);
    }

    private static async Task<int> GetFreePortAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            await Task.Yield();
        }
    }

    private static string GetEndpointAddress(JsonElement resource, string endpointName)
    {
        var endpoint = resource
            .GetProperty("endpoints")
            .EnumerateArray()
            .Single(endpoint =>
                endpoint.GetProperty("name").GetString() == endpointName);
        return endpoint.GetProperty("address").GetString() ??
            throw new InvalidOperationException($"Endpoint '{endpointName}' did not include an address.");
    }

    private static string GetPrimaryEndpointAddress(JsonElement resource) =>
        resource.GetProperty("primaryEndpoint").GetString() ??
        throw new InvalidOperationException("The resource did not include a primary endpoint.");

    private sealed class SampleProcess : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder output = new();

        private SampleProcess(Process process, Uri baseAddress)
        {
            this.process = process;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public static Task<SampleProcess> StartAsync(
            string projectPath,
            int port,
            IReadOnlyList<(string Key, string Value)>? environment = null)
        {
            var root = FindRepositoryRoot();
            var baseAddress = new Uri($"http://127.0.0.1:{port}");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(Path.Combine(root, projectPath));
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(baseAddress.ToString());
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Could not start sample project '{projectPath}'.");
            var sample = new SampleProcess(process, baseAddress);
            sample.Capture(process.StandardOutput);
            sample.Capture(process.StandardError);
            return Task.FromResult(sample);
        }

        public async Task WaitForHttpOkAsync(string path, TimeSpan timeout)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(3)
            };
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            Exception? lastException = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample process exited with code {process.ExitCode} before '{path}' was ready.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    using var response = await client.GetAsync(path);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample process did not return a successful response for '{path}' within {timeout}." +
                $"{Environment.NewLine}{lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        public async Task<string> GetStringAsync(string path, string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task WaitForAbsoluteHttpOkAsync(
            string url,
            string? bearerToken,
            TimeSpan timeout)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            Exception? lastException = null;
            string? lastStatus = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample process exited with code {process.ExitCode} before '{url}' was ready.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrWhiteSpace(bearerToken))
                    {
                        request.Headers.Authorization = new("Bearer", bearerToken);
                    }

                    using var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }

                    lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: " +
                        await response.Content.ReadAsStringAsync();
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample process did not return a successful response for '{url}' within {timeout}." +
                $"{Environment.NewLine}{lastStatus ?? lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        public async Task<string> GetAbsoluteStringAsync(string url, string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> SendAsync(
            HttpMethod method,
            string path,
            string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> SendJsonAsync(
            HttpMethod method,
            string path,
            string json,
            string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetClientCredentialsTokenAsync(
            string clientId,
            string clientSecret,
            string scope)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var response = await client.PostAsync(
                "/api/auth/v1/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope
                }));
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("access_token").GetString() ??
                throw new InvalidOperationException("The token endpoint returned no access token.");
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }

        private void Capture(StreamReader reader)
        {
            _ = Task.Run(async () =>
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (output)
                    {
                        output.AppendLine(line);
                    }
                }
            });
        }

        private string GetOutput()
        {
            lock (output)
            {
                return output.ToString();
            }
        }

        public static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CloudShell.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
