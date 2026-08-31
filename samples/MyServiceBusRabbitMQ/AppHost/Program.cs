using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("myservicebus-rabbitmq", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "MyServiceBusRabbitMQ");

var sampleRoot = app.ResolvePath("..");
var rabbitMqDataPath = app.ResolvePath("..", "Data", "rabbitmq");
var postgresDataPath = app.ResolvePath("..", "Data", "postgres");
const string myServiceBusVersion = "0.1.0-preview.8";

app.DefineResources(resources =>
{
    var rabbitData = resources
        .AddVolume("myservicebus-rabbitmq-data", rabbitMqDataPath)
        .WithDisplayName("RabbitMQ Data");
    var postgresData = resources
        .AddVolume("myservicebus-postgres-data", postgresDataPath)
        .WithDisplayName("PostgreSQL Data");

    var rabbitMq = resources
        .AddRabbitMQ("myservicebus-rabbitmq")
        .WithDisplayName("RabbitMQ")
        .WithVersion("4.1.8")
        .WithManagementUi()
        .WithUser("myservicebus", "local-development-only")
        .WithAmqpEndpoint(host: "localhost", port: 5679)
        .WithManagementEndpoint(host: "localhost", port: 15679)
        .MountVolume(rabbitData, RabbitMQResourceDefaults.DataPath);

    var postgres = resources
        .AddContainerApplication("myservicebus-postgres")
        .WithDisplayName("Outbox PostgreSQL")
        .WithImage("postgres:17.6-alpine")
        .WithEnvironmentVariable("POSTGRES_DB", "myservicebus")
        .WithEnvironmentVariable("POSTGRES_USER", "postgres")
        .WithEnvironmentVariable("POSTGRES_HOST_AUTH_METHOD", "trust")
        .MountVolume(postgresData, "/var/lib/postgresql/data")
        .WithTcpEndpoint("postgres", targetPort: 5432, host: "localhost", port: 55433);

    var monitoring = resources
        .AddContainerApplication("myservicebus-monitoring")
        .WithDisplayName("MyServiceBus Monitoring")
        .WithImage($"ghcr.io/marinasundstrom/myservicebus-monitoring-collector:{myServiceBusVersion}")
        .WithEnvironmentVariable("DOTNET_ENVIRONMENT", "Development")
        .WithEnvironmentVariable("Monitoring__Storage__Provider", "PostgreSql")
        .WithEnvironmentVariable("ConnectionStrings__Monitoring", "Host=myservicebus-postgres;Port=5432;Database=myservicebus;Username=postgres")
        .WithHttpEndpoint(targetPort: 8080, host: "localhost", port: 5310)
        .WithHttpHealthCheck("/health", endpointName: "http")
        .WithHttpLivenessCheck("/alive", endpointName: "http")
        .DependsOn(postgres);

    var dashboard = resources
        .AddContainerApplication("myservicebus-dashboard")
        .WithDisplayName("MyServiceBus Dashboard")
        .WithImage($"ghcr.io/marinasundstrom/myservicebus-monitoring-dashboard:{myServiceBusVersion}")
        .WithEnvironmentVariable("DOTNET_ENVIRONMENT", "Development")
        .WithEnvironmentVariable("Dashboard__MonitoringServiceAddress", "http://myservicebus-monitoring:8080")
        .WithHttpEndpoint(targetPort: 8080, host: "localhost", port: 5311)
        .WithHttpHealthCheck("/health", endpointName: "http")
        .WithHttpLivenessCheck("/alive", endpointName: "http")
        .DependsOn(monitoring);

    var raven = resources
        .AddContainerApplication("myservicebus-order-intake")
        .WithDisplayName("Raven Order Intake")
        .WithImage("cloudshell-myservicebus-raven:dev")
        .WithContainerBuild(sampleRoot, "RavenOrderService/Dockerfile")
        .WithReplicas(2)
        .WithEnvironmentVariable("ASPNETCORE_URLS", "http://+:8080")
        .WithEnvironmentVariable("RABBITMQ_HOST", "host.docker.internal")
        .WithEnvironmentVariable("RABBITMQ_PORT", "5679")
        .WithEnvironmentVariable("RABBITMQ_USER", "myservicebus")
        .WithEnvironmentVariable("RABBITMQ_PASSWORD", "local-development-only")
        .WithEnvironmentVariable("POSTGRES_HOST", "myservicebus-postgres")
        .WithEnvironmentVariable("MONITORING_SERVICE_URL", "http://myservicebus-monitoring:8080")
        .WithHttpEndpoint(targetPort: 8080, host: "localhost", port: 5401)
        .WithHttpHealthCheck("/health/live", endpointName: "http")
        .WithHttpLivenessCheck("/health/live", endpointName: "http")
        .WithRuntimeLogSources()
        .DependsOn(rabbitMq)
        .DependsOn(postgres)
        .DependsOn(monitoring);

    resources
        .AddContainerApplication("myservicebus-fulfillment")
        .WithDisplayName("Java Fulfillment")
        .WithImage("cloudshell-myservicebus-java:dev")
        .WithContainerBuild(sampleRoot, "JavaFulfillmentService/Dockerfile")
        .WithReplicas(3)
        .WithEnvironmentVariable("HTTP_PORT", "8080")
        .WithEnvironmentVariable("RABBITMQ_HOST", "host.docker.internal")
        .WithEnvironmentVariable("RABBITMQ_PORT", "5679")
        .WithEnvironmentVariable("RABBITMQ_USER", "myservicebus")
        .WithEnvironmentVariable("RABBITMQ_PASSWORD", "local-development-only")
        .WithEnvironmentVariable("POSTGRES_HOST", "myservicebus-postgres")
        .WithEnvironmentVariable("MONITORING_SERVICE_URL", "http://myservicebus-monitoring:8080")
        .WithHttpEndpoint(targetPort: 8080, host: "localhost", port: 5420)
        .WithHttpHealthCheck("/health/live", endpointName: "http")
        .WithHttpLivenessCheck("/health/live", endpointName: "http")
        .WithRuntimeLogSources()
        .DependsOn(rabbitMq)
        .DependsOn(postgres)
        .DependsOn(monitoring);

    _ = dashboard;
    _ = raven;
});

return await app.LaunchAsync();
