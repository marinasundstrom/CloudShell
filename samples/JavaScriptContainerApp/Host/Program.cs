using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

var sampleRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("../..", sampleRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var appPath = Path.Combine(sampleRootPath, "App");
var appEndpoint = builder.Configuration["JavaScriptContainerApp:Endpoint"]
    ?? "http://localhost:5174";
var settingsServiceEndpoint = builder.Configuration["JavaScriptContainerApp:SettingsEndpoint"]
    ?? "http://localhost:5102";
var settingsResourceId = "configuration.store:javascript-container-app-settings";
var appResourceId = "application.container-app:javascript-container-frontend";
var settingsEntriesEndpoint =
    $"{settingsServiceEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/entries";
var appEndpointUri = new Uri(appEndpoint);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var group = resources.AddResourceGroup(
                "group:javascript-container-app",
                "JavaScript Container App",
                "Resources for the JavaScript container app sample.");

            var settings = resources
                .AddConfigurationStore("javascript-container-app-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(group)
                .WithEndpoint(settingsServiceEndpoint)
                .WithAutoStart(false);

            resources
                .AddJavaScriptApp("javascript-container-frontend", appPath)
                .AsContainer(tag: "dev", dockerfile: "Dockerfile")
                .WithDisplayName("JavaScript Container Frontend")
                .WithResourceGroup(group)
                .WithAutoStart(false)
                .WithReplicas(3)
                .WithPackageManager("npm")
                .WithScript("dev")
                .WithServiceDiscovery()
                .WithReference(settings)
                .WithHttpEndpoint(
                    host: appEndpointUri.Host,
                    port: appEndpointUri.Port,
                    targetPort: 8080)
                .WithEnvironmentVariable(
                    "PORT",
                    "8080")
                .WithEnvironmentVariable(
                    "CLOUDSHELL_SETTINGS_ENDPOINT",
                    settingsEntriesEndpoint)
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "javascript-container-frontend")
                .WithHttpHealthCheck(
                    "/healthz",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http");
        });
    });

builder.Services.AddLocalDockerContainerApplicationRuntime(options =>
{
    options.AddApplication(appResourceId, appPath, runtime =>
    {
        runtime.IngressContainerName = "cloudshell-javascript-container-app-ingress";
        runtime.IngressConfigurationDirectory = Path.Combine(
            sampleRootPath,
            "Host",
            "Data",
            "javascript-container-app-ingress");
        runtime.ReplicaContainerNamePrefix = "cloudshell-javascript-container-app-replica-";
        runtime.ReplicaNetworkAliasPrefix = "cloudshell-javascript-container-app-replica-";
        runtime.ReplicaResourceIdPrefix = "runtime-container:application-container-app-javascript-container-frontend:replica-";
        runtime.ReplicaServiceNamePrefix = "javascript-container-frontend-replica-";
        runtime.ReplicaProbePortStart = appEndpointUri.Port + 100;
        runtime.RuntimeResourceProviderId = "javascript-container-app.runtime";
        runtime.RuntimeResourceProviderName = "JavaScript container app sample runtime";
        runtime.RuntimeMaterialization = "javascriptContainerApp";
    });
});

cloudShell.UseConfigurationStoreResourceProvider(runtime =>
{
    runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
    runtime.ServiceWorkingDirectory = repositoryRootPath;
    runtime.Entries.Add(new("Sample--Message", "Hello from the JavaScript container app host"));
});

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
