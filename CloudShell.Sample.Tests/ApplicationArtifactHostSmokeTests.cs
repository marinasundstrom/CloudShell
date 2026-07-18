using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Api;
using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;

namespace CloudShell.Sample.Tests;

[Collection(SampleSmokeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ApplicationArtifactHostSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task DevelopmentHost_UploadsAppliesAndRunsAspNetCoreArtifact()
    {
        var hostPort = await GetFreePortAsync();
        var appPort = await GetFreePortAsync();
        var workDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-artifact-host-smoke-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDirectory);

        var packagePath = await CreatePublishedAspNetCorePackageAsync(workDirectory);
        await using var host = await HostProcess.StartAsync(workDirectory, hostPort);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);
            var addPageHtml = await host.GetStringAsync(
                "/resources/add?type=application.dotnet-app");
            Assert.Contains("Package layout", addPageHtml);
            Assert.Contains("Package kind", addPageHtml);
            Assert.Contains("Package", addPageHtml);
            Assert.DoesNotContain("Application artifact upload is not enabled", addPageHtml);

            const string resourceName = "artifact-smoke-api";
            const string resourceType = "application.dotnet-app";
            const string resourceId = $"{resourceType}:{resourceName}";

            var status = await host.GetJsonAsync<DeploymentArtifactStoreStatus>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/status");
            Assert.True(status.IsEnabled);

            var layouts = await host.GetJsonAsync<DeploymentArtifactLayoutDescriptor[]>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/layouts?resourceTypeId={Uri.EscapeDataString(resourceType)}");
            var layout = Assert.Single(layouts, candidate =>
                string.Equals(candidate.Kind, "dotnetPublishedOutput", StringComparison.OrdinalIgnoreCase));

            var packageBytes = await File.ReadAllBytesAsync(packagePath);
            var contentSha256 = Convert.ToHexString(SHA256.HashData(packageBytes)).ToLowerInvariant();
            var upload = await host.PostJsonAsync<DeploymentArtifactUploadSession>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/upload",
                new CreateDeploymentArtifactUploadSessionRequest(
                    resourceType,
                    resourceName,
                    "zip",
                    Path.GetFileName(packagePath),
                    packageBytes.Length,
                    contentSha256,
                    layout.Kind),
                expectedStatusCode: HttpStatusCode.Created);

            await host.PutBytesAsync(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/uploads/{Uri.EscapeDataString(upload.UploadId)}/content",
                packageBytes);
            var revision = await host.PostJsonAsync<DeploymentArtifactRevision>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/uploads/{Uri.EscapeDataString(upload.UploadId)}/complete",
                new CompleteDeploymentArtifactUploadRequest(contentSha256));

            var validation = await host.PostJsonAsync<ResourceDefinitionValidationResult>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/validate",
                new ValidateDeploymentArtifactRequest(
                    resourceType,
                    resourceName,
                    revision.ArtifactId,
                    revision.RevisionId,
                    ".",
                    layout.Kind));
            Assert.Empty(validation.Diagnostics);

            var resource = CreateArtifactResource(resourceName, revision, appPort);
            var apply = await host.PostJsonAsync<ResourceTemplateApplyResult>(
                "/api/control-plane/v1/resource-templates/apply",
                new ResourceTemplateApplyRequest(
                    new ResourceTemplate(
                        "ASP.NET Core artifact smoke",
                        [resource],
                        EnvironmentId: "local")));
            Assert.False(apply.HasErrors, FormatDiagnostics(apply.Diagnostics));
            Assert.True(apply.IsCommitted);
            var appliedResourceJson = await host.GetStringAsync(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}");
            Assert.Contains(ApplicationArtifactAttributeIds.Source, appliedResourceJson);
            Assert.Contains(revision.RevisionId, appliedResourceJson);

            await host.PostEmptyAsync(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/actions/start");
            await WaitForHttpSuccessAsync($"http://127.0.0.1:{appPort}/health", StartupTimeout);

            var response = await GetStringWithRetryAsync(
                $"http://127.0.0.1:{appPort}/",
                StartupTimeout);
            Assert.Equal("artifact-smoke-v1", response);

            var revisions = await host.GetJsonAsync<DeploymentArtifactRevision[]>(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/artifacts/{Uri.EscapeDataString(revision.ArtifactId)}/revisions");
            Assert.Contains(revisions, stored =>
                string.Equals(stored.RevisionId, revision.RevisionId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            host.Dispose();
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }

    private static ResourceDefinition CreateArtifactResource(
        string resourceName,
        DeploymentArtifactRevision revision,
        int appPort)
    {
        var definition = new AspNetCoreProjectResourceDefinitionBuilder(resourceName)
            .UseLaunchSettings(false)
            .WithHotReload(false)
            .WithHttpEndpoint(port: appPort, host: "127.0.0.1")
            .Build();
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>(
            definition.ResourceAttributeValues)
        {
            [ApplicationArtifactAttributeIds.SourceKind] =
                ResourceAttributeValue.String(DeploymentArtifactSourceKinds.UploadedArtifact),
            [ApplicationArtifactAttributeIds.SourceOwner] =
                ResourceAttributeValue.String(ApplicationArtifactAttributeIds.ResourceManagerUiSourceOwner),
            [ApplicationArtifactAttributeIds.Enabled] =
                ResourceAttributeValue.Boolean(true),
            [ApplicationArtifactAttributeIds.Source] =
                ResourceAttributeValue.FromObject(new ApplicationArtifactReference(
                    revision.ArtifactId,
                    revision.RevisionId,
                    revision.PackageKind,
                    revision.ContentSha256,
                    revision.SizeBytes,
                    ".",
                    revision.ArtifactLayoutKind,
                    revision.SourceKind,
                    revision.SourceVersion,
                    revision.CanRehydrate))
        };

        return definition with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
    }

    private static async Task<string> CreatePublishedAspNetCorePackageAsync(string workDirectory)
    {
        var sourceDirectory = Path.Combine(workDirectory, "src");
        var publishDirectory = Path.Combine(workDirectory, "publish");
        var packagePath = Path.Combine(workDirectory, "artifact-smoke-api.zip");
        Directory.CreateDirectory(sourceDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "ArtifactSmokeApi.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup>
                <TargetFramework>net11.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Program.cs"),
            """
            var builder = WebApplication.CreateBuilder(args);
            var app = builder.Build();
            app.MapGet("/", () => "artifact-smoke-v1");
            app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
            app.Run();
            """);

        await RunProcessAsync(
            "dotnet",
            [
                "publish",
                Path.Combine(sourceDirectory, "ArtifactSmokeApi.csproj"),
                "--configuration",
                "Release",
                "--output",
                publishDirectory
            ],
            workDirectory,
            ProcessTimeout);
        ZipFile.CreateFromDirectory(publishDirectory, packagePath);
        return packagePath;
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException($"Could not start '{fileName}'.");
        var output = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch
        {
            TryKill(process);
            throw;
        }

        Assert.True(
            process.ExitCode == 0,
            $"{fileName} {string.Join(' ', arguments)} exited with {process.ExitCode}.{Environment.NewLine}{output}");
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

    private static async Task WaitForHttpSuccessAsync(string url, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;
        string? lastStatus = null;
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                using var response = await client.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"HTTP endpoint '{url}' did not return success within {timeout}.{Environment.NewLine}{lastStatus ?? lastException?.Message}");
    }

    private static async Task<string> GetStringWithRetryAsync(string url, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3)
        };

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                return await client.GetStringAsync(url);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"HTTP endpoint '{url}' did not return content within {timeout}.{Environment.NewLine}{lastException?.Message}");
    }

    private static string FormatDiagnostics(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
            $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message} ({diagnostic.Target})"));

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup for failed integration processes.
        }
    }

    private sealed class HostProcess : IAsyncDisposable, IDisposable
    {
        private readonly Process _process;
        private readonly string _cleanupDirectory;
        private readonly StringBuilder _output = new();

        private HostProcess(Process process, Uri baseAddress, string cleanupDirectory)
        {
            _process = process;
            BaseAddress = baseAddress;
            _cleanupDirectory = cleanupDirectory;
        }

        public Uri BaseAddress { get; }

        public static Task<HostProcess> StartAsync(string dataDirectory, int port)
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
            startInfo.ArgumentList.Add(Path.Combine(root, "CloudShell.Host", "CloudShell.Host.csproj"));
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(baseAddress.ToString());

            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["Authentication__Enabled"] = "false";
            startInfo.Environment["ApplicationResources__HostRunResourceTypesEnabled"] = "true";
            startInfo.Environment["Persistence__ConnectionString"] =
                $"Data Source={Path.Combine(dataDirectory, "cloudshell.db")}";
            startInfo.Environment["Identity__BuiltIn__Persistence__ConnectionString"] =
                $"Data Source={Path.Combine(dataDirectory, "identity.db")}";
            startInfo.Environment["DeploymentArtifacts__Enabled"] = "true";
            startInfo.Environment["DeploymentArtifacts__Store__Kind"] = "FileSystem";
            startInfo.Environment["DeploymentArtifacts__Store__RootPath"] =
                Path.Combine(dataDirectory, "deployment-artifacts");

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start CloudShell.Host.");
            var host = new HostProcess(process, baseAddress, dataDirectory);
            host.Capture(process.StandardOutput);
            host.Capture(process.StandardError);
            return Task.FromResult(host);
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
            string? lastStatus = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                ThrowIfExited(path);
                try
                {
                    using var response = await client.GetAsync(path);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }

                    lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"CloudShell.Host did not return success for '{path}' within {timeout}." +
                $"{Environment.NewLine}{lastStatus ?? lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        public async Task<string> GetStringAsync(string path)
        {
            using var client = CreateClient();
            using var response = await client.GetAsync(path);
            return await ReadSuccessAsync(response, $"GET {path}");
        }

        public async Task<TValue> GetJsonAsync<TValue>(string path)
        {
            var json = await GetStringAsync(path);
            var value = JsonSerializer.Deserialize<TValue>(
                json,
                ResourceDefinitionJson.Options);
            Assert.NotNull(value);
            return value;
        }

        public async Task<TValue> PostJsonAsync<TValue>(
            string path,
            object body,
            HttpStatusCode expectedStatusCode = HttpStatusCode.OK)
        {
            using var client = CreateClient();
            using var response = await client.PostAsJsonAsync(
                path,
                body,
                ResourceDefinitionJson.Options);
            Assert.Equal(expectedStatusCode, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            var value = JsonSerializer.Deserialize<TValue>(
                content,
                ResourceDefinitionJson.Options);
            Assert.NotNull(value);
            return value;
        }

        public async Task PostEmptyAsync(string path)
        {
            using var client = CreateClient();
            using var response = await client.PostAsync(path, content: null);
            await ReadSuccessAsync(response, $"POST {path}");
        }

        public async Task PutBytesAsync(string path, byte[] bytes)
        {
            using var client = CreateClient();
            using var response = await client.PutAsync(
                path,
                new ByteArrayContent(bytes));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public void Dispose()
        {
            TryKill(_process);
            _process.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            if (Directory.Exists(_cleanupDirectory))
            {
                Directory.Delete(_cleanupDirectory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private HttpClient CreateClient() =>
            new()
            {
                BaseAddress = BaseAddress,
                Timeout = StartupTimeout
            };

        private void Capture(StreamReader reader)
        {
            Task.Run(async () =>
            {
                try
                {
                    while (await reader.ReadLineAsync() is { } line)
                    {
                        lock (_output)
                        {
                            _output.AppendLine(line);
                        }
                    }
                }
                catch
                {
                    // Process output capture is diagnostic-only.
                }
            });
        }

        private void ThrowIfExited(string operation)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"CloudShell.Host exited with code {_process.ExitCode} before '{operation}' completed." +
                    $"{Environment.NewLine}{GetOutput()}");
            }
        }

        private string GetOutput()
        {
            lock (_output)
            {
                return _output.ToString();
            }
        }

        private async Task<string> ReadSuccessAsync(
            HttpResponseMessage response,
            string operation)
        {
            using (response)
            {
                var content = await response.Content.ReadAsStringAsync();
                Assert.True(
                    response.IsSuccessStatusCode,
                    $"{operation} returned {(int)response.StatusCode} {response.ReasonPhrase}: {content}" +
                    $"{Environment.NewLine}{GetOutput()}");
                return content;
            }
        }

        private static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
