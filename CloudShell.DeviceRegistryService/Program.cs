using CloudShell.Abstractions.Authorization;
using CloudShell.ControlPlane.Authentication;
using CloudShell.DeviceRegistryService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<DeviceRegistryServiceOptions>(
    builder.Configuration.GetSection(DeviceRegistryServiceOptions.SectionName));
builder.Services.Configure<CloudShellAuthenticationOptions>(
    builder.Configuration.GetSection(CloudShellAuthenticationOptions.SectionName));
builder.Services.AddSingleton<DeviceRegistryServiceStore>();
builder.Services.AddSingleton<BuiltInAuthorityTokenService>();
builder.Services.AddSingleton<BuiltInResourceIdentityRegistry>();
builder.Services.AddSingleton<CloudShellBearerTokenValidationService>();
builder.Services.AddHostedService<DeviceRegistryIdentityRegistrationService>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "Healthy",
    service = "CloudShell Device Registry Service"
}))
.AllowAnonymous();

app.UseCloudShellServiceBearerAuthentication();
app.MapCloudShellBuiltInAuthority();

var api = app
    .MapGroup("/api/devices")
    .WithTags("Devices");

api.MapGet("/registries/{registryId}/devices", ListDevices)
    .WithName("CloudShellDeviceRegistryService_ListDevices")
    .AllowAnonymous();

api.MapPost("/registries/{registryId}/enroll", EnrollDevice)
    .WithName("CloudShellDeviceRegistryService_EnrollDevice")
    .AllowAnonymous();

app.Run();

static IResult ListDevices(
    string registryId,
    HttpRequest request,
    DeviceRegistryServiceStore store)
{
    var registry = store.GetRegistry(registryId);
    if (!HasBearerToken(request))
    {
        return Unauthorized("A Device Registry bearer token is required.");
    }

    if (registry is null ||
        !IsAuthorized(registry, request))
    {
        return NotFound();
    }

        return Results.Ok(store.ListDevices(registry.Id)
        .Select(device => new DeviceMetadataResponse(
            device.Id,
            device.Subject,
            device.IdentityCategory,
            store.CreatePrincipal(device),
            device.IdentityProviderId,
            device.IdentityResourceId,
            device.IdentityName,
            device.ClientId,
            device.Claims,
            device.Properties,
            device.EnrolledAt))
        .ToArray());
}

static IResult EnrollDevice(
    string registryId,
    DeviceEnrollmentRequest request,
    HttpRequest httpRequest,
    DeviceRegistryServiceStore store)
{
    var registry = store.GetRegistry(registryId);
    if (registry is null)
    {
        return NotFound();
    }

    var result = store.EnrollDevice(
        registry,
        request,
        DateTimeOffset.UtcNow);
    if (!result.IsAccepted)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Enrollment rejected");
    }

    return Results.Ok(new DeviceEnrollmentResponse(
        result.Device!.Id,
        result.Device.RegistryId,
        result.Device.Subject,
        result.Device.IdentityCategory,
        store.CreatePrincipal(result.Device),
        result.Device.IdentityProviderId,
        result.Device.IdentityResourceId,
        result.Device.IdentityName,
        result.Device.ClientId,
        result.ClientSecret!,
        BuildAbsoluteTokenEndpoint(httpRequest),
        result.Device.EnrolledAt,
        result.Device.Claims,
        result.Device.Properties));
}

static string BuildAbsoluteTokenEndpoint(HttpRequest request)
{
    var host = request.Host.HasValue
        ? request.Host.Value
        : "localhost";
    return $"{request.Scheme}://{host}/api/auth/v1/token";
}

static bool IsAuthorized(
    DeviceRegistryDefinition registry,
    HttpRequest request) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        registry.Id,
        DeviceRegistryResourceOperationPermissions.EnrollDevices);

static bool HasBearerToken(HttpRequest request)
{
    var authorization = request.Headers.Authorization.FirstOrDefault();
    const string bearerPrefix = "Bearer ";
    return authorization?.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase) == true &&
        !string.IsNullOrWhiteSpace(authorization[bearerPrefix.Length..]);
}

static IResult Unauthorized(string detail) =>
    Results.Problem(
        detail,
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Unauthorized");

static IResult NotFound() =>
    Results.Problem(
        "The Device Registry was not found.",
        statusCode: StatusCodes.Status404NotFound,
        title: "Not found");

public sealed class DeviceRegistryIdentityRegistrationService(
    DeviceRegistryServiceStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        store.RehydrateDeviceIdentities();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
