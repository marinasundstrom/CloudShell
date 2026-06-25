using System.Net;
using System.Net.Sockets;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class AspNetCoreProjectProcessRuntimeControllerTests
{
    [Fact]
    public void CommandFactory_CreatesRunCommandFromGraphAttributes()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            arguments: "--urls http://localhost:5229",
            hotReload: false,
            useLaunchSettings: false);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal("dotnet", command.FileName);
        Assert.Equal(
            "run --project \"/repo/src/Api/Api.csproj\" --no-launch-profile -- --urls http://localhost:5229",
            command.Arguments);
        Assert.Equal("/repo/src/Api", command.WorkingDirectory);
        Assert.False(command.UseShellExecute);
        Assert.True(command.RedirectStandardOutput);
        Assert.True(command.RedirectStandardError);
        Assert.Equal(resource.EffectiveResourceId, command.Environment[AspNetCoreProjectEnvironmentNames.ResourceId]);
        Assert.Equal(resource.Name, command.Environment[AspNetCoreProjectEnvironmentNames.ResourceName]);
    }

    [Fact]
    public void CommandFactory_CreatesWatchCommandWhenHotReloadIsEnabled()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            hotReload: true,
            useLaunchSettings: false);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal(
            "watch --project \"/repo/src/Api/Api.csproj\" run --no-launch-profile",
            command.Arguments);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsDiagnosticWhenProjectFileIsMissing()
    {
        var resource = CreateResource("missing/CloudShell.Missing.csproj");
        var controller = new AspNetCoreProjectProcessRuntimeController();

        var diagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Start);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("application.aspNetCoreProject.projectFileMissing", diagnostic.Code);
        Assert.Equal(resource.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_StartsProjectReferenceApiFromGraphAttributes()
    {
        var port = GetFreeTcpPort();
        var projectPath = Path.Combine(
            FindRepositoryRoot(),
            "samples",
            "ProjectReference",
            "Api",
            "CloudShell.ProjectReferenceApi.csproj");
        var resource = CreateResource(
            projectPath,
            arguments: $"--urls http://127.0.0.1:{port}",
            hotReload: false,
            useLaunchSettings: false);

        await using var controller = new AspNetCoreProjectProcessRuntimeController();

        var diagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(2)
        };
        var response = await GetHealthyResponseAsync(
            httpClient,
            $"http://127.0.0.1:{port}/health");

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Project Reference API", body, StringComparison.Ordinal);
    }

    private static Resource CreateResource(
        string projectPath,
        string? arguments = null,
        bool? hotReload = null,
        bool? useLaunchSettings = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, string>
        {
            [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] = projectPath
        };

        if (arguments is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                arguments;
        }

        if (hotReload.HasValue)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                hotReload.Value.ToString().ToLowerInvariant();
        }

        if (useLaunchSettings.HasValue)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                useLaunchSettings.Value.ToString().ToLowerInvariant();
        }

        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [new AspNetCoreProjectResourceTypeProvider().TypeDefinition]);

        return resolver.Resolve(new ResourceGraphState(
            "api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: attributes));
    }

    private static async Task<HttpResponseMessage> GetHealthyResponseAsync(
        HttpClient httpClient,
        string requestUri)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var response = await httpClient.GetAsync(requestUri);
                if (response.IsSuccessStatusCode)
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException exception)
            {
                lastException = exception;
            }
            catch (TaskCanceledException exception)
            {
                lastException = exception;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new TimeoutException(
            $"Timed out waiting for ASP.NET Core project health endpoint '{requestUri}'.",
            lastException);
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

        throw new DirectoryNotFoundException(
            "Could not locate the CloudShell repository root.");
    }

    private static int GetFreeTcpPort()
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
        }
    }
}
