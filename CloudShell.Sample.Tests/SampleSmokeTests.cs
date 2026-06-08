using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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

        private static string FindRepositoryRoot()
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
