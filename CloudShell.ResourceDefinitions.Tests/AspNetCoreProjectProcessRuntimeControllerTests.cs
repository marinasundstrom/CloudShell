using System.Net;
using System.Net.Sockets;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
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
            "run --project \"/repo/src/Api/Api.csproj\" --no-build --no-launch-profile -- --urls http://localhost:5229",
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
            "watch --non-interactive --project \"/repo/src/Api/Api.csproj\" run --no-launch-profile",
            command.Arguments);
        Assert.Equal(
            "true",
            command.Environment[AspNetCoreProjectEnvironmentNames.DotNetWatchRestartOnRudeEdit]);
    }

    [Fact]
    public void CommandFactory_CreatesUrlsArgumentFromEndpointRequests()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            hotReload: false,
            useLaunchSettings: false,
            endpointRequests:
            [
                new(
                    "http",
                    "http",
                    Host: "127.0.0.1",
                    Port: 5229,
                    Exposure: "Local")
            ]);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal(
            "run --project \"/repo/src/Api/Api.csproj\" --no-build --no-launch-profile -- --urls http://127.0.0.1:5229",
            command.Arguments);
    }

    [Fact]
    public void CommandFactory_AppliesGraphEnvironmentVariables()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            environmentVariables: new Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>
            {
                ["CLOUDSHELL_TRACE_INGEST_ENDPOINT"] = new(
                    "http://localhost:5104/api/control-plane/v1/traces/ingest"),
                ["ApplicationTopology__SqlServer__ResourceName"] = new(
                    "graph-application-topology-sql-server"),
                ["EMPTY_VALUE"] = new()
            });
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal(
            "http://localhost:5104/api/control-plane/v1/traces/ingest",
            command.Environment["CLOUDSHELL_TRACE_INGEST_ENDPOINT"]);
        Assert.Equal(
            "graph-application-topology-sql-server",
            command.Environment["ApplicationTopology__SqlServer__ResourceName"]);
        Assert.Equal(string.Empty, command.Environment["EMPTY_VALUE"]);
    }

    [Fact]
    public void CommandFactory_LetsGraphEnvironmentVariablesOverrideDerivedValues()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            environmentVariables: new Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>
            {
                ["services__project-reference-api__http__0"] = new("http://127.0.0.1:6000")
            });
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(
                resource,
                "/repo/src/Api/Api.csproj",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["services__project-reference-api__http__0"] = "http://127.0.0.1:5229"
                });

        Assert.Equal(
            "http://127.0.0.1:6000",
            command.Environment["services__project-reference-api__http__0"]);
    }

    [Fact]
    public async Task EnvironmentReferenceResolver_ResolvesConfigurationAndSecretEnvironmentVariables()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            environmentVariables: new Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>
            {
                ["SAMPLE_MESSAGE"] = new(
                    ConfigurationEntryRef: new(
                        "configuration.store:sample-app",
                        "Sample:Message")),
                ["SAMPLE_API_KEY"] = new(
                    SecretRef: new(
                        "secrets.vault:sample-app",
                        "sample-api-key")),
                ["LITERAL"] = new("literal")
            });
        var resolver = new AspNetCoreProjectEnvironmentReferenceResolver(
            [new FixedConfigurationEntryReferenceResolver()],
            [new FixedSecretReferenceResolver()]);

        var values = await resolver.ResolveAsync(resource);

        Assert.Equal("Hello from configuration", values["SAMPLE_MESSAGE"]);
        Assert.Equal("secret-value", values["SAMPLE_API_KEY"]);
        Assert.False(values.ContainsKey("LITERAL"));
    }

    [Fact]
    public async Task ServiceDiscoveryResolver_DerivesVariablesFromGraphReferences()
    {
        const string apiResourceId = "application.aspnet-core-project:graph-project-reference-api";
        var apiState = CreateState(
            "graph-project-reference-api",
            "src/Api/Api.csproj",
            resourceId: apiResourceId,
            endpointRequests:
            [
                new(
                    "http",
                    "http",
                    Host: "127.0.0.1",
                    Port: 5229,
                    Exposure: "Local")
            ],
            serviceDiscoveryName: "project-reference-api");
        var frontend = CreateResource(
            "src/Frontend/Frontend.csproj",
            name: "graph-project-reference-frontend",
            references:
            [
                ResourceReference.ReferenceResourceId(
                    apiResourceId,
                    typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            ]);
        var resolver = new AspNetCoreProjectServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider([apiState])));

        var variables = await resolver.ResolveAsync(frontend);

        Assert.Equal(
            "http://127.0.0.1:5229",
            variables["services__project-reference-api__http__0"]);
        Assert.Equal(
            "http://127.0.0.1:5229",
            variables["services__graph-project-reference-api__http__0"]);
        Assert.Equal(
            "http://127.0.0.1:5229",
            variables["services__application.aspnet-core-project-graph-project-reference-api__http__0"]);
    }

    [Fact]
    public async Task ServiceDiscoveryResolver_DerivesVariablesFromReferencedGraphServiceEndpoints()
    {
        const string configurationResourceId = "configuration.store:graph-settings";
        const string secretsResourceId = "secrets.vault:graph-secrets";
        var configurationState = new ResourceGraphState(
            "graph-settings",
            ConfigurationStoreResourceTypeProvider.ResourceTypeId,
            ResourceId: configurationResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ConfigurationStoreResourceTypeProvider.Attributes.Endpoint] =
                    "http://127.0.0.1:5138"
            });
        var secretsState = new ResourceGraphState(
            "graph-secrets",
            SecretsVaultResourceTypeProvider.ResourceTypeId,
            ResourceId: secretsResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SecretsVaultResourceTypeProvider.Attributes.Endpoint] =
                    "http://127.0.0.1:6138"
            });
        var api = CreateResource(
            "src/Api/Api.csproj",
            references:
            [
                ResourceReference.ReferenceResourceId(
                    configurationResourceId,
                    typeId: ConfigurationStoreResourceTypeProvider.ResourceTypeId),
                ResourceReference.ReferenceResourceId(
                    secretsResourceId,
                    typeId: SecretsVaultResourceTypeProvider.ResourceTypeId)
            ]);
        var resolver = new AspNetCoreProjectServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider(
            [
                configurationState,
                secretsState
            ])));

        var variables = await resolver.ResolveAsync(api);

        Assert.Equal(
            "http://127.0.0.1:5138",
            variables["services__graph-settings__entries__0"]);
        Assert.Equal(
            "http://127.0.0.1:5138",
            variables["services__configuration.store-graph-settings__entries__0"]);
        Assert.Equal(
            "graph-settings",
            variables["CLOUDSHELL_CONFIGURATION_SERVICE_NAME"]);
        Assert.Equal(
            configurationResourceId,
            variables["CLOUDSHELL_CONFIGURATION_GRAPH_SETTINGS_STORE_ID"]);
        Assert.Equal(
            "http://127.0.0.1:5138/api/configuration/stores/configuration.store%3Agraph-settings/entries",
            variables["CLOUDSHELL_CONFIGURATION_GRAPH_SETTINGS_ENDPOINT"]);
        Assert.Equal(
            "http://127.0.0.1:5138/api/configuration/stores/configuration.store%3Agraph-settings/entries",
            variables["CLOUDSHELL_CONFIGURATION_CONFIGURATION_STORE_GRAPH_SETTINGS_ENDPOINT"]);
        Assert.Equal(
            "http://127.0.0.1:6138",
            variables["services__graph-secrets__secrets__0"]);
        Assert.Equal(
            "http://127.0.0.1:6138",
            variables["services__secrets.vault-graph-secrets__secrets__0"]);
        Assert.Equal(
            "graph-secrets",
            variables["CLOUDSHELL_SECRETS_VAULT_NAME"]);
        Assert.Equal(
            secretsResourceId,
            variables["CLOUDSHELL_SECRETS_GRAPH_SECRETS_VAULT_ID"]);
        Assert.Equal(
            "http://127.0.0.1:6138/api/secrets/vaults/secrets.vault%3Agraph-secrets/secrets",
            variables["CLOUDSHELL_SECRETS_GRAPH_SECRETS_ENDPOINT"]);
        Assert.Equal(
            "http://127.0.0.1:6138/api/secrets/vaults/secrets.vault%3Agraph-secrets/secrets",
            variables["CLOUDSHELL_SECRETS_SECRETS_VAULT_GRAPH_SECRETS_ENDPOINT"]);
    }

    [Fact]
    public async Task ServiceDiscoveryResolver_DerivesVariablesFromReferencedSqlServerEndpointRequests()
    {
        const string sqlResourceId = "application.sql-server:application-topology-sql-server";
        var sqlState = new ResourceGraphState(
            "application-topology-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: sqlResourceId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [SqlServerResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "tds",
                            "tcp",
                            TargetPort: 1433,
                            Host: "127.0.0.1",
                            Port: 14334,
                            Exposure: "Local")
                    })
            });
        var api = CreateResource(
            "src/Api/Api.csproj",
            references:
            [
                ResourceReference.ReferenceResourceId(
                    sqlResourceId,
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId)
            ]);
        var resolver = new AspNetCoreProjectServiceDiscoveryEnvironmentResolver(
            new ResourceGraphModel(new InMemoryResourceStateProvider([sqlState])));

        var variables = await resolver.ResolveAsync(api);

        Assert.Equal(
            "127.0.0.1:14334",
            variables["services__application-topology-sql-server__tds__0"]);
        Assert.Equal(
            "127.0.0.1:14334",
            variables["services__application.sql-server-application-topology-sql-server__tcp__0"]);
    }

    [Fact]
    public void CommandFactory_PrefersExplicitProjectArgumentsOverEndpointRequests()
    {
        var resource = CreateResource(
            "src/Api/Api.csproj",
            arguments: "--urls http://localhost:5010",
            hotReload: false,
            useLaunchSettings: false,
            endpointRequests:
            [
                new(
                    "http",
                    "http",
                    Host: "127.0.0.1",
                    Port: 5229,
                    Exposure: "Local")
            ]);
        var command = new AspNetCoreProjectProcessCommandFactory()
            .CreateStartInfo(resource, "/repo/src/Api/Api.csproj");

        Assert.Equal(
            "run --project \"/repo/src/Api/Api.csproj\" --no-build --no-launch-profile -- --urls http://localhost:5010",
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
    public void GetStatus_ReturnsStoppedWhenProcessIsNotTracked()
    {
        var resource = CreateResource("src/Api/Api.csproj");
        var controller = new AspNetCoreProjectProcessRuntimeController();

        var status = controller.GetStatus(resource);

        Assert.Equal(AspNetCoreProjectRuntimeStatus.Stopped, status);
    }

    [Fact]
    public void NoopRuntimeController_ReturnsUnknownStatus()
    {
        var resource = CreateResource("src/Api/Api.csproj");
        var controller = new NoopAspNetCoreProjectRuntimeController();

        var status = controller.GetStatus(resource);

        Assert.Equal(AspNetCoreProjectRuntimeStatus.Unknown, status);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ExecuteAsync_StartsProjectReferenceApiFromEndpointRequestAttributes()
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
            hotReload: false,
            useLaunchSettings: false,
            endpointRequests:
            [
                new(
                    "http",
                    "http",
                    Host: "127.0.0.1",
                    Port: port,
                    Exposure: "Local")
            ]);

        await using var controller = new AspNetCoreProjectProcessRuntimeController(
            environmentProviders:
            [
                new FixedAspNetCoreProjectRuntimeEnvironmentProvider(
                    new Dictionary<string, string>
                    {
                        ["CLOUDSHELL_PROJECT_REFERENCE_ENVIRONMENT_TAG"] =
                            "graph-runtime-provider"
                    })
            ]);

        var diagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Equal(AspNetCoreProjectRuntimeStatus.Running, controller.GetStatus(resource));

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
        Assert.Contains("graph-runtime-provider", body, StringComparison.Ordinal);
        var output = await WaitForOutputAsync(controller, resource.EffectiveResourceId);
        Assert.NotEmpty(output);

        var stopDiagnostics = await controller.ExecuteAsync(
            resource,
            AspNetCoreProjectResourceTypeProvider.Operations.Stop);

        Assert.Empty(stopDiagnostics);
        Assert.Equal(AspNetCoreProjectRuntimeStatus.Stopped, controller.GetStatus(resource));
    }

    private static Resource CreateResource(
        string projectPath,
        string name = "api",
        string? resourceId = null,
        string? arguments = null,
        bool? hotReload = null,
        bool? useLaunchSettings = null,
        IReadOnlyList<NetworkingEndpointRequestValue>? endpointRequests = null,
        IReadOnlyDictionary<string, AspNetCoreProjectEnvironmentVariableValue>? environmentVariables = null,
        IReadOnlyList<ResourceReference>? references = null,
        string? serviceDiscoveryName = null)
    {
        var resolver = new ResourceResolver(
            [AspNetCoreProjectResourceTypeProvider.ClassDefinition],
            [new AspNetCoreProjectResourceTypeProvider().TypeDefinition],
            attributeValueShapeProviders:
            [
                new NetworkingEndpointShapeProvider(),
                new AspNetCoreProjectShapeProvider()
            ]);

        return resolver.Resolve(CreateState(
            name,
            projectPath,
            resourceId,
            arguments,
            hotReload,
            useLaunchSettings,
            endpointRequests,
            environmentVariables,
            references,
            serviceDiscoveryName));
    }

    private static ResourceGraphState CreateState(
        string name,
        string projectPath,
        string? resourceId = null,
        string? arguments = null,
        bool? hotReload = null,
        bool? useLaunchSettings = null,
        IReadOnlyList<NetworkingEndpointRequestValue>? endpointRequests = null,
        IReadOnlyDictionary<string, AspNetCoreProjectEnvironmentVariableValue>? environmentVariables = null,
        IReadOnlyList<ResourceReference>? references = null,
        string? serviceDiscoveryName = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
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
                hotReload.Value;
        }

        if (useLaunchSettings.HasValue)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                useLaunchSettings.Value;
        }

        if (endpointRequests is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                ResourceAttributeValue.FromObject(endpointRequests);
        }

        if (environmentVariables is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                ResourceAttributeValue.FromObject(environmentVariables);
        }

        if (serviceDiscoveryName is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName] =
                serviceDiscoveryName;
        }

        if (references is not null)
        {
            attributes[AspNetCoreProjectResourceTypeProvider.Attributes.References] =
                ResourceAttributeValue.FromObject(references);
        }

        return new ResourceGraphState(
            name,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: resourceId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            Attributes: attributes);
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

    private static async Task<IReadOnlyList<AspNetCoreProjectRuntimeOutputEntry>> WaitForOutputAsync(
        IAspNetCoreProjectRuntimeOutputReader outputReader,
        string resourceId)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var output = outputReader.ReadOutput(resourceId);
            if (output.Count > 0)
            {
                return output;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        return [];
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

    private sealed class FixedAspNetCoreProjectRuntimeEnvironmentProvider(
        IReadOnlyDictionary<string, string> variables) : IAspNetCoreProjectRuntimeEnvironmentProvider
    {
        public ValueTask<IReadOnlyDictionary<string, string>> ResolveAsync(
            Resource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(variables);
    }

    private sealed class FixedConfigurationEntryReferenceResolver : IConfigurationEntryReferenceResolver
    {
        public ResourceSettingResolutionResult ResolveConfigurationEntry(
            ConfigurationEntryReference reference,
            ResourceSettingResolutionContext context) =>
            reference is
            {
                StoreResourceId: "configuration.store:sample-app",
                EntryName: "Sample:Message"
            }
                ? ResourceSettingResolutionResult.Resolved("Hello from configuration")
                : ResourceSettingResolutionResult.Failed("Configuration entry not found.");
    }

    private sealed class FixedSecretReferenceResolver : ISecretReferenceResolver
    {
        public ValueTask<ResourceSettingResolutionResult> ResolveSecretAsync(
            SecretReference reference,
            ResourceSettingResolutionContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(reference is
            {
                VaultResourceId: "secrets.vault:sample-app",
                SecretName: "sample-api-key"
            }
                ? ResourceSettingResolutionResult.Resolved("secret-value")
                : ResourceSettingResolutionResult.Failed("Secret not found."));
    }
}
