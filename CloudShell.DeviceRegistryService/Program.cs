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
builder.Services.AddHostedService<DeviceRegistryMqttHostedService>();

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

api.MapPost("/registries/{registryId}/devices/{deviceId}/sync", SyncDevice)
    .WithName("CloudShellDeviceRegistryService_SyncDevice");

api.MapGet("/registries/{registryId}/devices/{deviceId}/twin", GetDeviceTwin)
    .WithName("CloudShellDeviceRegistryService_GetDeviceTwin");

api.MapPut("/registries/{registryId}/devices/{deviceId}/twin/desired", SetDeviceDesiredState)
    .WithName("CloudShellDeviceRegistryService_SetDeviceDesiredState");

api.MapPost("/registries/{registryId}/devices/{deviceId}/disable", DisableDevice)
    .WithName("CloudShellDeviceRegistryService_DisableDevice");

api.MapPost("/registries/{registryId}/devices/{deviceId}/enable", EnableDevice)
    .WithName("CloudShellDeviceRegistryService_EnableDevice");

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

    var timestamp = DateTimeOffset.UtcNow;
    return Results.Ok(store.ListDevices(registry.Id)
        .Select(device => ToMetadataResponse(registry, store, device, timestamp))
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.EnrollDevice(
        registry,
        request,
        timestamp);
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
        result.Device.Properties,
        ResolvePresence(registry, result.Device, timestamp),
        result.Device.EnrollmentProfileName,
        result.Device.EnrollmentProfileKind,
        result.Device.LastSeenTransport,
        result.Device.DisabledAt,
        result.Device.DisabledReason));
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.RecordHeartbeat(
        registry.Id,
        deviceId,
        clientId,
        request,
        timestamp);
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

    return Results.Ok(ToMetadataResponse(registry, store, result.Device!, timestamp));
}

static IResult SyncDevice(
    string registryId,
    string deviceId,
    DeviceSyncRequest request,
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.SyncDevice(
        registry.Id,
        deviceId,
        clientId,
        request,
        timestamp);
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
            title: "Device sync rejected");
    }

    return Results.Ok(new DeviceSyncResponse(
        ToMetadataResponse(registry, store, result.Device!, timestamp),
        result.Device!.Twin.Desired,
        result.Device.Twin.Reported,
        result.DesiredStateChanged,
        result.Device.Twin.LastSyncedAt));
}

static IResult GetDeviceTwin(
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
        !HasListDevicesPermission(registry, httpRequest))
    {
        return NotFound();
    }

    var device = store.ListDevices(registry.Id).FirstOrDefault(candidate =>
        string.Equals(candidate.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    if (device is null)
    {
        return Results.Problem(
            "The device was not found.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    return Results.Ok(ToTwinResponse(device.Twin));
}

static IResult SetDeviceDesiredState(
    string registryId,
    string deviceId,
    DeviceDesiredStateRequest request,
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

    var result = store.SetDesiredState(
        registry.Id,
        deviceId,
        request,
        DateTimeOffset.UtcNow);
    if (result.IsNotFound)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    return Results.Ok(ToTwinResponse(result.Device!.Twin));
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.RevokeDevice(
        registry.Id,
        deviceId,
        request.Reason,
        timestamp);
    if (result.IsNotFound)
    {
        return Results.Problem(
            result.Failure,
            statusCode: StatusCodes.Status404NotFound,
            title: "Device not found");
    }

    return Results.Ok(ToMetadataResponse(registry, store, result.Device!, timestamp));
}

static IResult DisableDevice(
    string registryId,
    string deviceId,
    DeviceDisableRequest request,
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.DisableDevice(
        registry.Id,
        deviceId,
        request.Reason,
        timestamp);
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
            title: "Device disable rejected");
    }

    return Results.Ok(ToMetadataResponse(registry, store, result.Device!, timestamp));
}

static IResult EnableDevice(
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

    var timestamp = DateTimeOffset.UtcNow;
    var result = store.EnableDevice(
        registry.Id,
        deviceId);
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
            title: "Device enable rejected");
    }

    return Results.Ok(ToMetadataResponse(registry, store, result.Device!, timestamp));
}

static DeviceMetadataResponse ToMetadataResponse(
    DeviceRegistryDefinition registry,
    DeviceRegistryServiceStore store,
    DeviceRecord device,
    DateTimeOffset timestamp) =>
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
        device.RevokedReason,
        ResolvePresence(registry, device, timestamp),
        device.EnrollmentProfileName,
        device.EnrollmentProfileKind,
        device.LastSeenTransport,
        device.DisabledAt,
        device.DisabledReason);

static DeviceTwinResponse ToTwinResponse(DeviceTwinDocument twin) =>
    new(
        twin.Desired,
        twin.Reported,
        twin.LastSyncedAt);

static string ResolvePresence(
    DeviceRegistryDefinition registry,
    DeviceRecord device,
    DateTimeOffset timestamp)
{
    if (string.Equals(device.Status, DeviceRecordStatuses.Revoked, StringComparison.OrdinalIgnoreCase) ||
        device.RevokedAt is not null)
    {
        return DevicePresenceStatuses.Revoked;
    }

    if (string.Equals(device.Status, DeviceRecordStatuses.Disabled, StringComparison.OrdinalIgnoreCase) ||
        device.DisabledAt is not null)
    {
        return DevicePresenceStatuses.Disabled;
    }

    if (device.LastSeenAt is null)
    {
        return DevicePresenceStatuses.Unknown;
    }

    return registry.HeartbeatStaleAfterSeconds is > 0 &&
        timestamp - device.LastSeenAt.Value > TimeSpan.FromSeconds(registry.HeartbeatStaleAfterSeconds.Value)
            ? DevicePresenceStatuses.Stale
            : DevicePresenceStatuses.Online;
}

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
