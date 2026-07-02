using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceModel;

var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
var sampleRoot = Path.Combine(repositoryRoot, "samples", "CSharpAppHost");
var javascriptAppPath = Path.Combine(repositoryRoot, "samples", "JavaScriptApp", "App");
var cliProject = Environment.GetEnvironmentVariable("CLOUDSHELL_CLI_PROJECT")
    ?? Path.Combine(repositoryRoot, "CloudShell.Cli", "CloudShell.Cli.csproj");
var hostProject = Environment.GetEnvironmentVariable("CLOUDSHELL_HOST_PROJECT")
    ?? Path.Combine(repositoryRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj");
var controlPlaneUrl = new Uri(Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_URL")
    ?? "http://127.0.0.1:5099");
var stateDirectory = Environment.GetEnvironmentVariable("CLOUDSHELL_STATE_DIR")
    ?? Path.Combine(sampleRoot, ".cloudshell");
var dataDirectory = ReadArgumentValue(args, "--data-dir")
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_DATA_DIR")
    ?? stateDirectory;
var settingsEndpoint = Environment.GetEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT")
    ?? "http://localhost:5103";
var settingsResourceId = "configuration.store:csharp-app-settings";
var settingsEntriesEndpoint =
    $"{settingsEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/entries";
var appEndpoint = new Uri(Environment.GetEnvironmentVariable("CLOUDSHELL_APP_ENDPOINT")
    ?? "http://localhost:5175");

var app = CloudShellDistributedApplication.CreateBuilder(
    "csharp-app-host",
    args);

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("csharp-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsEndpoint)
        .WithAutoStart(false);

    resources
        .AddJavaScriptApp("csharp-declared-frontend", javascriptAppPath)
        .WithDisplayName("C# Declared Frontend")
        .WithAutoStart(false)
        .WithPackageManager("npm")
        .WithScript("dev")
        .WithServiceDiscovery()
        .WithReference(settings)
        .WithHttpEndpoint(
            host: appEndpoint.Host,
            port: appEndpoint.Port,
            targetPort: appEndpoint.Port)
        .WithEnvironmentVariable(
            "PORT",
            appEndpoint.Port.ToString())
        .WithEnvironmentVariable(
            "CLOUDSHELL_SETTINGS_ENDPOINT",
            settingsEntriesEndpoint)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "csharp-declared-frontend")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

var metadata = new Dictionary<string, string>
{
    ["cloudshell.source"] = "csharp",
    ["cloudshell.sample"] = "CSharpAppHost"
};

var launcherOptions = new CloudShellHostLauncherOptions
{
    CliProjectPath = cliProject,
    ControlPlaneUrl = ReadArgumentValue(args, "--control-plane") is { } explicitControlPlane
        ? new Uri(explicitControlPlane)
        : controlPlaneUrl,
    StateDirectory = stateDirectory,
    DataDirectory = dataDirectory,
    HostProjectPath = hostProject,
    HostUrl = controlPlaneUrl,
    NoBuild = HasArgument(args, "--no-build"),
    StartHost = HasArgument(args, "--start") || HasArgument(args, "--run"),
    TemplatePath = HasArgument(args, "--template-path")
        ? ReadArgumentValue(args, "--template-path")
        : null,
    EnvironmentId = "local",
    Metadata = metadata,
    BearerToken = Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_TOKEN")
};

if (HasArgument(args, "--apply") || HasArgument(args, "--start") || HasArgument(args, "--run"))
{
    var result = await app.ApplyAsync(launcherOptions);
    return result.ExitCode;
}

var template = app.BuildTemplate("local", metadata);
Console.Write(ResourceTemplateSerializer.SerializeTemplate(template));
return 0;

static bool HasArgument(
    string[] args,
    string name) =>
    args.Any(argument => string.Equals(argument, name, StringComparison.OrdinalIgnoreCase));

static string? ReadArgumentValue(
    string[] args,
    string name)
{
    for (var index = 0; index < args.Length - 1; index++)
    {
        if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[index + 1];
        }
    }

    return null;
}

static string FindRepositoryRoot(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate the CloudShell repository root.");
}
