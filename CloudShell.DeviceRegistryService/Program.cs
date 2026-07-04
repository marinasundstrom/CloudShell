using System.Security.Claims;
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

api.MapPost("/registries/{registryId}/devices/{deviceId}/heartbeat", HeartbeatDevice)
    .WithName("CloudShellDeviceRegistryService_HeartbeatDevice");

api.MapPost("/registries/{registryId}/devices/{deviceId}/revoke", RevokeDevice)
    .WithName("CloudShellDeviceRegistryService_RevokeDevice");

api.MapDelete("/registries/{registryId}/devices/{deviceId}", RemoveDevice)
    .WithName("CloudShellDeviceRegistryService_RemoveDevice");

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
        !HasListDevicesPermission(registry, request))
    {
        return NotFound();
    }

    return Results.Ok(store.ListDevices(registry.Id)
        .Select(device => ToMetadataResponse(store, device))
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
        result.Device.Status,
        result.Device.LastSeenAt,
        result.Device.LastSeenSource,
        result.Device.RevokedAt,
        result.Device.RevokedReason,
        result.Device.Claims,
        result.Device.Properties));
}

static IResult HeartbeatDevice(
    string registryId,
    string deviceId,
    DeviceHeartbeatRequest request,
    HttpRequest httpRequest,
    DeviceRegistryServiceStore store)
{
    var registry = store.GetRegistry(registryId);
    if (registry is null)
    {
        return NotFound();
    }

    if (!HasBearerToken(httpRequest))
    {
        return Unauthorized("A device bearer token is required.");
    }

    var clientId = httpRequest.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
    if (string.IsNullOrWhiteSpace(clientId))
    {
        return Unauthorized("A device bearer token is required.");
    }

    var result = store.RecordHeartbeat(
        registry.Id,
        deviceId,
        clientId,
        request,
        DateTimeOffset.UtcNow);
    if (result.IsNotFound)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    if (!result.IsAccepted)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status403Forbidden,
            title: "Heartbeat rejected");
    }

    return Results.Ok(ToMetadataResponse(store, result.Device!));
}

static IResult RemoveDevice(
    string registryId,
    string deviceId,
    HttpRequest httpRequest,
    DeviceRegistryServiceStore store)
{
    var registry = store.GetRegistry(registryId);
    if (!HasBearerToken(httpRequest))
    {
        return Unauthorized("A Device Registry bearer token is required.");
    }

    if (registry is null ||
        !HasManageDevicesPermission(registry, httpRequest))
    {
        return NotFound();
    }

    var result = store.RemoveDevice(
        registry.Id,
        deviceId);
    if (result.IsNotFound)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    return Results.NoContent();
}

static IResult RevokeDevice(
    string registryId,
    string deviceId,
    DeviceRevokeRequest request,
    HttpRequest httpRequest,
    DeviceRegistryServiceStore store)
{
    var registry = store.GetRegistry(registryId);
    if (!HasBearerToken(httpRequest))
    {
        return Unauthorized("A Device Registry bearer token is required.");
    }

    if (registry is null ||
        !HasManageDevicesPermission(registry, httpRequest))
    {
        return NotFound();
    }

    var result = store.RevokeDevice(
        registry.Id,
        deviceId,
        request.Reason,
        DateTimeOffset.UtcNow);
    if (result.IsNotFound)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    return Results.Ok(ToMetadataResponse(store, result.Device!));
}

static DeviceMetadataResponse ToMetadataResponse(
    DeviceRegistryServiceStore store,
    DeviceRecord device) =>
    new(
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
        device.EnrolledAt,
        device.Status,
        device.LastSeenAt,
        device.LastSeenSource,
        device.RevokedAt,
        device.RevokedReason);

static string BuildAbsoluteTokenEndpoint(HttpRequest request)
{
    var host = request.Host.HasValue
        ? request.Host.Value
        : "localhost";
    return $"{request.Scheme}://{host}/api/auth/v1/token";
}

static bool HasListDevicesPermission(
    DeviceRegistryDefinition registry,
    HttpRequest request) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        registry.Id,
        DeviceRegistryResourceOperationPermissions.EnrollDevices) ||
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        registry.Id,
        DeviceRegistryResourceOperationPermissions.ManageDevices);

static bool HasManageDevicesPermission(
    DeviceRegistryDefinition registry,
    HttpRequest request) =>
    ResourcePermissionClaimAuthorization.HasResourcePermission(
        request.HttpContext.User,
        registry.Id,
        DeviceRegistryResourceOperationPermissions.ManageDevices);

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
