using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true));
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<MowerFleet>();
builder.Services.AddHttpClient<DeviceRegistryClient>();
builder.Services.AddHostedService<DeviceRegistryMowerObserver>();

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapGet("/health", (MowerFleet fleet) => Results.Ok(new
{
    status = "healthy",
    service = "CloudShell Robotic Mower backend",
    replica = MowerRuntime.ReplicaOrdinal,
    mowerCount = fleet.Snapshots.Count
}));
app.MapGet("/alive", () => Results.Ok(new
{
    status = "alive",
    replica = MowerRuntime.ReplicaOrdinal
}));
app.MapGet("/api/mowers", (MowerFleet fleet) => Results.Ok(fleet.Snapshots));
app.MapHub<MowerHub>("/hubs/mowers");

app.Run();

public sealed class MowerHub(
    MowerFleet fleet,
    DeviceRegistryClient registryClient,
    ILogger<MowerHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation(
            "Mower SignalR operator client {ConnectionId} connected to replica {Replica}.",
            Context.ConnectionId,
            MowerRuntime.ReplicaOrdinal);
        await Clients.Caller.SendAsync(
            "FleetSnapshot",
            fleet.Snapshots,
            MowerRuntime.ReplicaOrdinal);
        await base.OnConnectedAsync();
    }

    public async Task RequestFleetSnapshot()
    {
        await Clients.Caller.SendAsync(
            "FleetSnapshot",
            fleet.Snapshots,
            MowerRuntime.ReplicaOrdinal);
    }

    public async Task SetMowerCommand(
        string mowerId,
        string command,
        string? requestedBy = null)
    {
        var normalizedMowerId = MowerFleet.NormalizeMowerId(mowerId);
        if (!fleet.TryGetSnapshot(normalizedMowerId, out var snapshot) ||
            string.IsNullOrWhiteSpace(snapshot.DeviceId))
        {
            throw new HubException($"Mower '{normalizedMowerId}' is not enrolled yet.");
        }

        var normalizedCommand = MowerFleet.NormalizeCommand(command);
        var issued = fleet.RecordCommand(
            normalizedMowerId,
            normalizedCommand,
            snapshot.MowingPattern,
            requestedBy);
        await registryClient.SetDesiredCommandAsync(
            snapshot.DeviceId,
            issued,
            Context.ConnectionAborted);
        logger.LogInformation(
            "Recorded desired {Command} command for mower {MowerId} device {DeviceId} through Device Registry on replica {Replica}.",
            issued.Command,
            issued.MowerId,
            snapshot.DeviceId,
            MowerRuntime.ReplicaOrdinal);
        await Clients.All.SendAsync("MowerCommandIssued", issued);
    }

    public async Task SetMowerPattern(
        string mowerId,
        string pattern,
        string? requestedBy = null)
    {
        var normalizedMowerId = MowerFleet.NormalizeMowerId(mowerId);
        if (!fleet.TryGetSnapshot(normalizedMowerId, out var snapshot) ||
            string.IsNullOrWhiteSpace(snapshot.DeviceId))
        {
            throw new HubException($"Mower '{normalizedMowerId}' is not enrolled yet.");
        }

        var normalizedPattern = MowerFleet.NormalizePattern(pattern);
        var issued = fleet.RecordPattern(
            normalizedMowerId,
            normalizedPattern,
            requestedBy);
        await registryClient.SetDesiredPatternAsync(
            snapshot.DeviceId,
            issued,
            Context.ConnectionAborted);
        logger.LogInformation(
            "Recorded desired {Pattern} pattern for mower {MowerId} device {DeviceId} through Device Registry on replica {Replica}.",
            issued.Pattern,
            issued.MowerId,
            snapshot.DeviceId,
            MowerRuntime.ReplicaOrdinal);
        await Clients.All.SendAsync("MowerPatternIssued", issued);
    }
}

public sealed class DeviceRegistryMowerObserver(
    MowerFleet fleet,
    DeviceRegistryClient registryClient,
    IHubContext<MowerHub> hubContext,
    IConfiguration configuration,
    ILogger<DeviceRegistryMowerObserver> logger) : BackgroundService
{
    private readonly TimeSpan pollInterval = TimeSpan.FromSeconds(
        Math.Max(1, configuration.GetValue<int?>("MowerRegistry:PollIntervalSeconds") ?? 1));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var reports = await registryClient.GetMowerReportsAsync(stoppingToken);
                foreach (var report in reports)
                {
                    var snapshot = fleet.RecordReportedState(report);
                    await hubContext.Clients.All.SendAsync("MowerTelemetry", snapshot, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Failed to read mower state from Device Registry.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}

public sealed class DeviceRegistryClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<DeviceRegistryClient> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string endpoint = ReadRequired(configuration, "MowerRegistry:Endpoint", "http://localhost:7160");
    private readonly string registryId = ReadRequired(configuration, "MowerRegistry:ResourceId", "iot.device-registry:park-devices");
    private readonly string managementClientId = ReadRequired(configuration, "MowerRegistry:ManagementClientId", "device-registry-admin");
    private readonly string managementClientSecret = ReadRequired(configuration, "MowerRegistry:ManagementClientSecret", "local-development-device-registry-admin-secret");

    public async Task<IReadOnlyList<MowerRegistryReport>> GetMowerReportsAsync(
        CancellationToken cancellationToken)
    {
        var token = await RequestManagementTokenAsync(cancellationToken);
        using var devicesRequest = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices",
            token);
        using var devicesResponse = await httpClient.SendAsync(devicesRequest, cancellationToken);
        await EnsureSuccessAsync(devicesResponse, cancellationToken);
        var devices = await devicesResponse.Content.ReadFromJsonAsync<DeviceMetadataResponse[]>(
            SerializerOptions,
            cancellationToken) ?? [];

        var reports = new List<MowerRegistryReport>();
        foreach (var device in devices)
        {
            if (!IsRoboticMower(device))
            {
                continue;
            }

            var twin = await GetTwinAsync(device.DeviceId, token, cancellationToken);
            reports.Add(MowerRegistryReport.From(device, twin));
        }

        return reports;
    }

    public async Task SetDesiredCommandAsync(
        string deviceId,
        MowerCommand command,
        CancellationToken cancellationToken)
    {
        var token = await RequestManagementTokenAsync(cancellationToken);
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices/{Uri.EscapeDataString(deviceId)}/twin/desired",
            token);
        request.Content = JsonContent.Create(
            new
            {
                state = new
                {
                    command = command.Command,
                    commandId = command.CommandId,
                    pattern = command.Pattern,
                    requestedBy = command.RequestedBy,
                    requestedAt = command.Timestamp,
                    backendReplica = command.BackendReplica
                }
            },
            options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetDesiredPatternAsync(
        string deviceId,
        MowerPatternChange pattern,
        CancellationToken cancellationToken)
    {
        var token = await RequestManagementTokenAsync(cancellationToken);
        using var request = CreateAuthorizedRequest(
            HttpMethod.Put,
            $"/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices/{Uri.EscapeDataString(deviceId)}/twin/desired",
            token);
        request.Content = JsonContent.Create(
            new
            {
                state = new
                {
                    pattern = pattern.Pattern,
                    patternChangeId = pattern.PatternChangeId,
                    requestedBy = pattern.RequestedBy,
                    requestedAt = pattern.Timestamp,
                    backendReplica = pattern.BackendReplica
                }
            },
            options: SerializerOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<DeviceTwinResponse> GetTwinAsync(
        string deviceId,
        string token,
        CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Get,
            $"/api/devices/registries/{Uri.EscapeDataString(registryId)}/devices/{Uri.EscapeDataString(deviceId)}/twin",
            token);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<DeviceTwinResponse>(
            SerializerOptions,
            cancellationToken) ??
            throw new JsonException("Device Registry returned an empty twin response.");
    }

    private async Task<string> RequestManagementTokenAsync(
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.PostAsync(
            $"{endpoint.TrimEnd('/')}/api/auth/v1/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = managementClientId,
                ["client_secret"] = managementClientSecret,
                ["scope"] = "ControlPlane.Access"
            }),
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString() ??
            throw new JsonException("Device Registry token response did not include an access token.");
    }

    private HttpRequestMessage CreateAuthorizedRequest(
        HttpMethod method,
        string path,
        string token)
    {
        var request = new HttpRequestMessage(method, $"{endpoint.TrimEnd('/')}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "Device Registry request failed with {StatusCode}: {Body}",
            (int)response.StatusCode,
            body);
        response.EnsureSuccessStatusCode();
    }

    private static bool IsRoboticMower(DeviceMetadataResponse device) =>
        HasValue(device.Claims, "deviceType", "robotic-mower") ||
        HasValue(device.Properties, "deviceType", "robotic-mower") ||
        device.Subject.StartsWith("mower/", StringComparison.OrdinalIgnoreCase);

    private static bool HasValue(
        IReadOnlyDictionary<string, string>? values,
        string name,
        string expected) =>
        values is not null &&
        values.TryGetValue(name, out var value) &&
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private static string ReadRequired(
        IConfiguration configuration,
        string name,
        string fallback)
    {
        var value = configuration[name];
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}

public sealed class MowerFleet
{
    private readonly ConcurrentDictionary<string, MowerSnapshot> snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MowerCommand> commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MowerPatternChange> patterns =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MowerSnapshot> Snapshots =>
        snapshots.Values
            .OrderBy(snapshot => snapshot.MowerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGetSnapshot(
        string mowerId,
        out MowerSnapshot snapshot) =>
        snapshots.TryGetValue(NormalizeMowerId(mowerId), out snapshot!);

    public MowerSnapshot RecordReportedState(MowerRegistryReport report) =>
        snapshots.AddOrUpdate(
            report.MowerId,
            _ => CreateSnapshot(report),
            (_, existing) => CreateSnapshot(report, existing));

    public MowerCommand RecordCommand(
        string mowerId,
        string command,
        string? pattern,
        string? requestedBy)
    {
        var normalizedMowerId = NormalizeMowerId(mowerId);
        var normalizedPattern = patterns.TryGetValue(normalizedMowerId, out var pendingPattern)
            ? pendingPattern.Pattern
            : NormalizePattern(pattern);
        var issued = new MowerCommand(
            normalizedMowerId,
            command,
            normalizedPattern,
            string.IsNullOrWhiteSpace(requestedBy) ? "frontend" : requestedBy.Trim(),
            DateTimeOffset.UtcNow,
            MowerRuntime.ReplicaOrdinal,
            Guid.NewGuid().ToString("N"));
        commands[normalizedMowerId] = issued;
        patterns[normalizedMowerId] = new MowerPatternChange(
            normalizedMowerId,
            normalizedPattern,
            issued.RequestedBy,
            issued.Timestamp,
            issued.BackendReplica,
            issued.CommandId);
        snapshots.AddOrUpdate(
            normalizedMowerId,
            _ => new MowerSnapshot(
                normalizedMowerId,
                normalizedMowerId,
                "Unknown park",
                DeviceId: null,
                EnrollmentStatus: "unknown",
                Presence: "unknown",
                LastSeenTransport: "unknown",
                Mode: command == "start" ? "starting" : "stopping",
                BladeEnabled: command == "start",
                Latitude: 40.7933,
                Longitude: -73.9617,
                Heading: 90,
                BatteryPercent: 100,
                LastCommand: command,
                MowingPattern: normalizedPattern,
                LastUpdated: DateTimeOffset.UtcNow,
                ReportedVersion: 0,
                DesiredVersion: 0,
                BackendReplica: MowerRuntime.ReplicaOrdinal),
            (_, existing) => existing with
            {
                LastCommand = command,
                MowingPattern = normalizedPattern,
                LastUpdated = DateTimeOffset.UtcNow,
                DesiredVersion = Math.Max(existing.DesiredVersion, existing.DesiredVersion + 1),
                BackendReplica = MowerRuntime.ReplicaOrdinal
            });
        return issued;
    }

    public MowerPatternChange RecordPattern(
        string mowerId,
        string pattern,
        string? requestedBy)
    {
        var normalizedMowerId = NormalizeMowerId(mowerId);
        var normalizedPattern = NormalizePattern(pattern);
        var issued = new MowerPatternChange(
            normalizedMowerId,
            normalizedPattern,
            string.IsNullOrWhiteSpace(requestedBy) ? "frontend" : requestedBy.Trim(),
            DateTimeOffset.UtcNow,
            MowerRuntime.ReplicaOrdinal,
            Guid.NewGuid().ToString("N"));
        patterns[normalizedMowerId] = issued;
        snapshots.AddOrUpdate(
            normalizedMowerId,
            _ => new MowerSnapshot(
                normalizedMowerId,
                normalizedMowerId,
                "Unknown park",
                DeviceId: null,
                EnrollmentStatus: "unknown",
                Presence: "unknown",
                LastSeenTransport: "unknown",
                Mode: "idle",
                BladeEnabled: false,
                Latitude: 40.7933,
                Longitude: -73.9617,
                Heading: 90,
                BatteryPercent: 100,
                LastCommand: "none",
                MowingPattern: normalizedPattern,
                LastUpdated: DateTimeOffset.UtcNow,
                ReportedVersion: 0,
                DesiredVersion: 0,
                BackendReplica: MowerRuntime.ReplicaOrdinal),
            (_, existing) => existing with
            {
                MowingPattern = normalizedPattern,
                LastUpdated = DateTimeOffset.UtcNow,
                DesiredVersion = Math.Max(existing.DesiredVersion, existing.DesiredVersion + 1),
                BackendReplica = MowerRuntime.ReplicaOrdinal
            });
        return issued;
    }

    public static string NormalizeMowerId(string value) =>
        string.IsNullOrWhiteSpace(value) ? "mower-unknown" : value.Trim();

    public static string NormalizeCommand(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "start" or "mow" or "resume" => "start",
            "stop" or "dock" or "pause" => "stop",
            _ => "stop"
        };

    public static string NormalizePattern(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "spiral" => "spiral",
            "wander" or "random" => "wander",
            _ => "lanes"
        };

    private MowerSnapshot CreateSnapshot(
        MowerRegistryReport report,
        MowerSnapshot? existing = null) =>
        new(
            report.MowerId,
            report.DisplayName,
            report.ParkName,
            report.DeviceId,
            report.EnrollmentStatus,
            report.Presence,
            report.LastSeenTransport,
            report.Mode,
            report.BladeEnabled,
            report.Latitude,
            report.Longitude,
            report.Heading,
            report.BatteryPercent,
            report.LastCommand ??
                (commands.TryGetValue(report.MowerId, out var command)
                    ? command.Command
                    : existing?.LastCommand ?? "none"),
            report.MowingPattern ??
                (patterns.TryGetValue(report.MowerId, out var pattern)
                    ? pattern.Pattern
                    : existing?.MowingPattern ?? "lanes"),
            report.LastUpdated,
            report.ReportedVersion,
            report.DesiredVersion,
            MowerRuntime.ReplicaOrdinal);
}

public sealed record MowerRegistryReport(
    string MowerId,
    string DisplayName,
    string ParkName,
    string DeviceId,
    string EnrollmentStatus,
    string Presence,
    string LastSeenTransport,
    string Mode,
    bool BladeEnabled,
    double Latitude,
    double Longitude,
    double Heading,
    double BatteryPercent,
    string? LastCommand,
    string? MowingPattern,
    DateTimeOffset LastUpdated,
    long ReportedVersion,
    long DesiredVersion)
{
    public static MowerRegistryReport From(
        DeviceMetadataResponse device,
        DeviceTwinResponse twin)
    {
        var reported = twin.Reported.State;
        var mowerId = ReadString(device.Properties, "mowerId") ??
            ReadString(reported, "mowerId") ??
            device.Subject.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ??
            device.DeviceId;
        var displayName = ReadString(device.Properties, "mowerName") ??
            ReadString(reported, "displayName") ??
            mowerId;
        var parkName = ReadString(device.Properties, "parkName") ??
            ReadString(reported, "parkName") ??
            "Unknown park";

        return new(
            MowerFleet.NormalizeMowerId(mowerId),
            displayName,
            parkName,
            device.DeviceId,
            device.Status,
            device.Presence,
            string.IsNullOrWhiteSpace(device.LastSeenTransport) ? "unknown" : device.LastSeenTransport,
            ReadString(reported, "mode") ?? "idle",
            ReadBool(reported, "bladeEnabled"),
            ReadDouble(reported, "latitude", 40.7933),
            ReadDouble(reported, "longitude", -73.9617),
            ReadDouble(reported, "heading", 90),
            Math.Clamp(ReadDouble(reported, "batteryPercent", 100), 0, 100),
            ReadString(reported, "lastCommand"),
            MowerFleet.NormalizePattern(ReadString(reported, "mowingPattern")),
            twin.Reported.UpdatedAt ?? device.LastSeenAt ?? DateTimeOffset.UtcNow,
            twin.Reported.Version,
            twin.Desired.Version);
    }

    private static string? ReadString(
        IReadOnlyDictionary<string, string>? values,
        string name) =>
        values is not null &&
        values.TryGetValue(name, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    private static string? ReadString(
        IReadOnlyDictionary<string, JsonElement> values,
        string name) =>
        values.TryGetValue(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(
        IReadOnlyDictionary<string, JsonElement> values,
        string name) =>
        values.TryGetValue(name, out var value) &&
        value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
            _ => false
        };

    private static double ReadDouble(
        IReadOnlyDictionary<string, JsonElement> values,
        string name,
        double fallback) =>
        values.TryGetValue(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Number => value.TryGetDouble(out var parsed) ? parsed : fallback,
                JsonValueKind.String => double.TryParse(value.GetString(), out var parsed) ? parsed : fallback,
                _ => fallback
            }
            : fallback;
}

public sealed record MowerSnapshot(
    string MowerId,
    string DisplayName,
    string ParkName,
    string? DeviceId,
    string EnrollmentStatus,
    string Presence,
    string LastSeenTransport,
    string Mode,
    bool BladeEnabled,
    double Latitude,
    double Longitude,
    double Heading,
    double BatteryPercent,
    string LastCommand,
    string MowingPattern,
    DateTimeOffset LastUpdated,
    long ReportedVersion,
    long DesiredVersion,
    string BackendReplica);

public sealed record MowerCommand(
    string MowerId,
    string Command,
    string Pattern,
    string RequestedBy,
    DateTimeOffset Timestamp,
    string BackendReplica,
    string CommandId);

public sealed record MowerPatternChange(
    string MowerId,
    string Pattern,
    string RequestedBy,
    DateTimeOffset Timestamp,
    string BackendReplica,
    string PatternChangeId);

public sealed record DeviceMetadataResponse(
    string DeviceId,
    string RegistryId,
    string Subject,
    string IdentityCategory,
    JsonElement Principal,
    string IdentityProviderId,
    string IdentityResourceId,
    string IdentityName,
    string ClientId,
    IReadOnlyDictionary<string, string> Claims,
    IReadOnlyDictionary<string, string> Properties,
    DateTimeOffset EnrolledAt,
    string Status,
    DateTimeOffset? LastSeenAt,
    string? LastSeenSource,
    DateTimeOffset? RevokedAt,
    string? RevokedReason,
    string Presence,
    string? EnrollmentProfileName,
    string? EnrollmentProfileKind,
    string? LastSeenTransport,
    DateTimeOffset? DisabledAt,
    string? DisabledReason);

public sealed record DeviceTwinResponse(
    DeviceTwinState Desired,
    DeviceTwinState Reported,
    DateTimeOffset? LastSyncedAt);

public sealed record DeviceTwinState(
    long Version,
    DateTimeOffset? UpdatedAt,
    IReadOnlyDictionary<string, JsonElement> State);

[JsonSerializable(typeof(DeviceMetadataResponse[]))]
[JsonSerializable(typeof(DeviceTwinResponse))]
public sealed partial class MowerJsonContext : JsonSerializerContext;

public static class MowerRuntime
{
    public static string ReplicaOrdinal =>
        Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1";
}
