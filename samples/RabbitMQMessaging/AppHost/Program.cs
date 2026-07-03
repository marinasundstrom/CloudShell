using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

var app = CloudShellDistributedApplication
    .CreateBuilder("rabbitmq-messaging", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "RabbitMQMessaging");

const string exchangeName = "cloudshell.sample.events";
var virtualHost = app.Configuration["RabbitMQMessaging:VirtualHost"] ?? "cloudshell_sample";
var rabbitMqPort = ReadPort(app.Configuration["RabbitMQMessaging:RabbitMQPort"], 5678);
var managementEndpoint = new Uri(app.Configuration["RabbitMQMessaging:ManagementEndpoint"]
    ?? "http://localhost:15678");
var dotNetEndpoint = new Uri(app.Configuration["RabbitMQMessaging:DotNetEndpoint"]
    ?? "http://localhost:5281");
var javaEndpoint = new Uri(app.Configuration["RabbitMQMessaging:JavaEndpoint"]
    ?? "http://localhost:5282");
var dotNetProjectPath = app.ResolvePath("..", "DotNetApi", "CloudShell.RabbitMQMessaging.DotNetApi.csproj");
var javaAppPath = app.ResolvePath("..", "JavaApp");
var javaArtifactPath = Path.Combine("target", "cloudshell-rabbitmq-java-sample.jar");
var javaClassPath = string.Join(
    Path.PathSeparator,
    javaArtifactPath,
    Path.Combine("target", "lib", "*"));
var rabbitMqDataPath = app.ResolvePath("..", "Data", "rabbitmq");

app.DefineResources(resources =>
{
    const string identityProviderId = "identity:built-in";
    const string dotNetIdentityName = "rabbitmq-dotnet";
    const string javaIdentityName = "rabbitmq-java";

    var brokerData = resources
        .AddVolume(
            "rabbitmq-messaging-data",
            path: rabbitMqDataPath)
        .WithDisplayName("RabbitMQ Data");

    var broker = resources
        .AddRabbitMQ("rabbitmq")
        .WithDisplayName("RabbitMQ")
        .WithAmqpEndpoint(
            host: "localhost",
            port: rabbitMqPort)
        .WithManagementEndpoint(
            host: managementEndpoint.Host,
            port: managementEndpoint.Port)
        .WithVirtualHost(virtualHost)
        .MountVolume(brokerData, RabbitMQResourceDefaults.DataPath);

    var dotNet = resources
        .AddAspNetCoreProject("rabbitmq-dotnet", dotNetProjectPath)
        .WithDisplayName(".NET Publisher")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithIdentity(identityProviderId, name: dotNetIdentityName)
        .ProvisionIdentityOnStartup()
        .WithReference(broker)
        .DependsOn(broker)
        .WithHttpEndpoint(
            host: dotNetEndpoint.Host,
            port: dotNetEndpoint.Port)
        .WithEnvironmentVariable("RabbitMQ__Host", "localhost")
        .WithEnvironmentVariable("RabbitMQ__Port", rabbitMqPort.ToString())
        .WithEnvironmentVariable("RabbitMQ__Username", RabbitMQResourceDefaults.DefaultUsername)
        .WithEnvironmentVariable("RabbitMQ__Password", RabbitMQResourceDefaults.DefaultPassword)
        .WithEnvironmentVariable("RabbitMQ__VirtualHost", virtualHost)
        .WithEnvironmentVariable("RabbitMQ__Exchange", exchangeName)
        .WithEnvironmentVariable("RabbitMQ__Queue", "rabbitmq-dotnet-events")
        .WithEnvironmentVariable("OTEL_SERVICE_NAME", "rabbitmq-dotnet")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");

    var java = resources
        .AddJavaApp("rabbitmq-java", javaAppPath, javaArtifactPath)
        .WithDisplayName("Java Publisher")
        .WithMainClass("com.example.cloudshell.rabbitmq.RabbitMqSampleServer")
        .WithClassPath(javaClassPath)
        .WithIdentity(identityProviderId, name: javaIdentityName)
        .ProvisionIdentityOnStartup()
        .WithReference(broker)
        .DependsOn(broker)
        .WithHttpEndpoint(
            host: javaEndpoint.Host,
            port: javaEndpoint.Port,
            targetPort: javaEndpoint.Port)
        .WithEnvironmentVariable("PORT", javaEndpoint.Port.ToString())
        .WithEnvironmentVariable("RABBITMQ_HOST", "localhost")
        .WithEnvironmentVariable("RABBITMQ_PORT", rabbitMqPort.ToString())
        .WithEnvironmentVariable("RABBITMQ_USERNAME", RabbitMQResourceDefaults.DefaultUsername)
        .WithEnvironmentVariable("RABBITMQ_PASSWORD", RabbitMQResourceDefaults.DefaultPassword)
        .WithEnvironmentVariable("RABBITMQ_VHOST", virtualHost)
        .WithEnvironmentVariable("RABBITMQ_EXCHANGE", exchangeName)
        .WithEnvironmentVariable("RABBITMQ_QUEUE", "rabbitmq-java-events")
        .WithEnvironmentVariable("OTEL_SERVICE_NAME", "rabbitmq-java")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");

    AddRabbitMQAppGrants(
        broker,
        dotNet.Principal(dotNetIdentityName, providerId: identityProviderId));
    AddRabbitMQAppGrants(
        broker,
        java.Principal(javaIdentityName, providerId: identityProviderId));
});

return await app.LaunchAsync();

static int ReadPort(string? value, int fallback) =>
    int.TryParse(value, out var port) && port > 0
        ? port
        : fallback;

static void AddRabbitMQAppGrants(
    RabbitMQResourceDefinitionBuilder broker,
    ResourcePrincipalReference principal)
{
    broker.Allow(
        principal,
        RabbitMQResourceOperationPermissions.Configure);
    broker.Allow(
        principal,
        RabbitMQResourceOperationPermissions.Publish);
    broker.Allow(
        principal,
        RabbitMQResourceOperationPermissions.Consume);
}
