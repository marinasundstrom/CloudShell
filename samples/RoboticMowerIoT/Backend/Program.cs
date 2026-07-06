using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

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
    ILogger<MowerHub> logger) : Hub
{
    public override async Task OnConnectedAsync()
    {
        logger.LogInformation(
            "Mower SignalR client {ConnectionId} connected to replica {Replica}.",
            Context.ConnectionId,
            MowerRuntime.ReplicaOrdinal);
        await Clients.Caller.SendAsync(
            "FleetSnapshot",
            fleet.Snapshots,
            MowerRuntime.ReplicaOrdinal);
        await base.OnConnectedAsync();
    }

    public async Task RegisterMower(MowerRegistration registration)
    {
        var normalized = registration.Normalize();
        await Groups.AddToGroupAsync(Context.ConnectionId, MowerFleet.GroupName(normalized.MowerId));
        var snapshot = fleet.Register(Context.ConnectionId, normalized);
        logger.LogInformation(
            "Registered mower {MowerId} connection {ConnectionId} on replica {Replica}.",
            normalized.MowerId,
            Context.ConnectionId,
            MowerRuntime.ReplicaOrdinal);
        await Clients.All.SendAsync("MowerRegistered", snapshot);
        if (fleet.TryGetCommand(normalized.MowerId, out var command))
        {
            await Clients.Caller.SendAsync("MowerCommand", command);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (fleet.Detach(Context.ConnectionId) is { } snapshot)
        {
            logger.LogInformation(
                "Detached mower {MowerId} connection {ConnectionId} on replica {Replica}.",
                snapshot.MowerId,
                Context.ConnectionId,
                MowerRuntime.ReplicaOrdinal);
            await Clients.All.SendAsync("MowerTelemetry", snapshot);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task ReportTelemetry(MowerTelemetry telemetry)
    {
        var normalized = telemetry.Normalize();
        await Groups.AddToGroupAsync(Context.ConnectionId, MowerFleet.GroupName(normalized.MowerId));
        fleet.AttachConnection(Context.ConnectionId, normalized.MowerId);
        var snapshot = fleet.RecordTelemetry(normalized);
        logger.LogInformation(
            "Mower {MowerId} reported {Mode} at {Latitude},{Longitude} on replica {Replica}.",
            snapshot.MowerId,
            snapshot.Mode,
            snapshot.Latitude,
            snapshot.Longitude,
            MowerRuntime.ReplicaOrdinal);
        await Clients.All.SendAsync("MowerTelemetry", snapshot);
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
        var normalizedCommand = MowerFleet.NormalizeCommand(command);
        var issued = fleet.RecordCommand(
            normalizedMowerId,
            normalizedCommand,
            requestedBy);
        logger.LogInformation(
            "Issued {Command} to mower {MowerId} with {ConnectionCount} active connection(s) on replica {Replica}.",
            issued.Command,
            issued.MowerId,
            fleet.GetConnectionCount(issued.MowerId),
            MowerRuntime.ReplicaOrdinal);
        await Clients.Group(MowerFleet.GroupName(normalizedMowerId))
            .SendAsync("MowerCommand", issued);
        await Clients.All.SendAsync("MowerCommandIssued", issued);
    }
}

public sealed class MowerFleet
{
    private readonly ConcurrentDictionary<string, MowerSnapshot> snapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MowerCommand> commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> mowerIdByConnectionId =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> connectionIdsByMowerId =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MowerSnapshot> Snapshots =>
        snapshots.Values
            .OrderBy(snapshot => snapshot.MowerId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public MowerSnapshot Register(
        string connectionId,
        MowerRegistration registration)
    {
        AttachConnection(connectionId, registration.MowerId);
        return snapshots.AddOrUpdate(
            registration.MowerId,
            _ => new MowerSnapshot(
                registration.MowerId,
                registration.DisplayName,
                registration.ParkName,
                registration.DeviceId,
                registration.EnrollmentStatus,
                Mode: "registered",
                BladeEnabled: false,
                Latitude: 40.7933,
                Longitude: -73.9617,
                Heading: 90,
                BatteryPercent: 100,
                LastCommand: commands.TryGetValue(registration.MowerId, out var command)
                    ? command.Command
                    : "none",
                LastUpdated: DateTimeOffset.UtcNow,
                ActiveConnectionCount: GetConnectionCount(registration.MowerId),
                BackendReplica: MowerRuntime.ReplicaOrdinal),
            (_, existing) => existing with
            {
                DisplayName = registration.DisplayName,
                ParkName = registration.ParkName,
                DeviceId = registration.DeviceId,
                EnrollmentStatus = registration.EnrollmentStatus,
                LastUpdated = DateTimeOffset.UtcNow,
                ActiveConnectionCount = GetConnectionCount(registration.MowerId),
                BackendReplica = MowerRuntime.ReplicaOrdinal
            });
    }

    public MowerSnapshot RecordTelemetry(MowerTelemetry telemetry) =>
        snapshots.AddOrUpdate(
            telemetry.MowerId,
            _ => CreateSnapshot(telemetry, displayName: telemetry.MowerId, parkName: "Unknown park"),
            (_, existing) => CreateSnapshot(telemetry, existing.DisplayName, existing.ParkName));

    public MowerCommand RecordCommand(
        string mowerId,
        string command,
        string? requestedBy)
    {
        var issued = new MowerCommand(
            mowerId,
            command,
            string.IsNullOrWhiteSpace(requestedBy) ? "frontend" : requestedBy.Trim(),
            DateTimeOffset.UtcNow,
            MowerRuntime.ReplicaOrdinal);
        commands[mowerId] = issued;
        snapshots.AddOrUpdate(
            mowerId,
            _ => new MowerSnapshot(
                mowerId,
                mowerId,
                "Unknown park",
                DeviceId: null,
                EnrollmentStatus: "unknown",
                Mode: command == "start" ? "starting" : "stopping",
                BladeEnabled: command == "start",
                Latitude: 40.7933,
                Longitude: -73.9617,
                Heading: 90,
                BatteryPercent: 100,
                LastCommand: command,
                LastUpdated: DateTimeOffset.UtcNow,
                ActiveConnectionCount: GetConnectionCount(mowerId),
                BackendReplica: MowerRuntime.ReplicaOrdinal),
            (_, existing) => existing with
            {
                LastCommand = command,
                LastUpdated = DateTimeOffset.UtcNow,
                ActiveConnectionCount = GetConnectionCount(mowerId),
                BackendReplica = MowerRuntime.ReplicaOrdinal
            });
        return issued;
    }

    public bool TryGetCommand(
        string mowerId,
        out MowerCommand command) =>
        commands.TryGetValue(NormalizeMowerId(mowerId), out command!);

    public int GetConnectionCount(string mowerId) =>
        connectionIdsByMowerId.TryGetValue(NormalizeMowerId(mowerId), out var connections)
            ? connections.Count
            : 0;

    public MowerSnapshot? Detach(string connectionId)
    {
        if (!mowerIdByConnectionId.TryRemove(connectionId, out var mowerId))
        {
            return null;
        }

        if (connectionIdsByMowerId.TryGetValue(mowerId, out var connectionIds))
        {
            connectionIds.TryRemove(connectionId, out _);
            if (connectionIds.IsEmpty)
            {
                connectionIdsByMowerId.TryRemove(mowerId, out _);
            }
        }

        return snapshots.AddOrUpdate(
            mowerId,
            _ => new MowerSnapshot(
                mowerId,
                mowerId,
                "Unknown park",
                DeviceId: null,
                EnrollmentStatus: "unknown",
                Mode: "offline",
                BladeEnabled: false,
                Latitude: 40.7933,
                Longitude: -73.9617,
                Heading: 90,
                BatteryPercent: 0,
                LastCommand: commands.TryGetValue(mowerId, out var command)
                    ? command.Command
                    : "none",
                LastUpdated: DateTimeOffset.UtcNow,
                ActiveConnectionCount: GetConnectionCount(mowerId),
                BackendReplica: MowerRuntime.ReplicaOrdinal),
            (_, existing) => existing with
            {
                ActiveConnectionCount = GetConnectionCount(mowerId),
                LastUpdated = DateTimeOffset.UtcNow,
                BackendReplica = MowerRuntime.ReplicaOrdinal
            });
    }

    public static string GroupName(string mowerId) =>
        $"mower:{NormalizeMowerId(mowerId)}";

    public static string NormalizeMowerId(string value) =>
        string.IsNullOrWhiteSpace(value) ? "mower-unknown" : value.Trim();

    public static string NormalizeCommand(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "start" or "mow" or "resume" => "start",
            "stop" or "dock" or "pause" => "stop",
            _ => "stop"
        };

    private MowerSnapshot CreateSnapshot(
        MowerTelemetry telemetry,
        string displayName,
        string parkName) =>
        new(
            telemetry.MowerId,
            displayName,
            parkName,
            telemetry.DeviceId,
            telemetry.EnrollmentStatus,
            telemetry.Mode,
            telemetry.BladeEnabled,
            telemetry.Latitude,
            telemetry.Longitude,
            telemetry.Heading,
            telemetry.BatteryPercent,
            telemetry.LastCommand,
            telemetry.Timestamp,
            GetConnectionCount(telemetry.MowerId),
            MowerRuntime.ReplicaOrdinal);

    public void AttachConnection(string connectionId, string mowerId)
    {
        var normalizedMowerId = NormalizeMowerId(mowerId);
        if (mowerIdByConnectionId.TryGetValue(connectionId, out var previousMowerId) &&
            !string.Equals(previousMowerId, normalizedMowerId, StringComparison.OrdinalIgnoreCase) &&
            connectionIdsByMowerId.TryGetValue(previousMowerId, out var previousConnections))
        {
            previousConnections.TryRemove(connectionId, out _);
        }

        mowerIdByConnectionId[connectionId] = normalizedMowerId;
        connectionIdsByMowerId
            .GetOrAdd(normalizedMowerId, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase))
            .TryAdd(connectionId, 0);
    }
}

public sealed record MowerRegistration(
    string MowerId,
    string DisplayName,
    string ParkName,
    string? DeviceId,
    string EnrollmentStatus)
{
    public MowerRegistration Normalize() =>
        this with
        {
            MowerId = MowerFleet.NormalizeMowerId(MowerId),
            DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? MowerId : DisplayName.Trim(),
            ParkName = string.IsNullOrWhiteSpace(ParkName) ? "Unknown park" : ParkName.Trim(),
            EnrollmentStatus = string.IsNullOrWhiteSpace(EnrollmentStatus) ? "unknown" : EnrollmentStatus.Trim()
        };
}

public sealed record MowerTelemetry(
    string MowerId,
    string? DeviceId,
    string EnrollmentStatus,
    string Mode,
    bool BladeEnabled,
    double Latitude,
    double Longitude,
    double Heading,
    double BatteryPercent,
    string LastCommand,
    DateTimeOffset Timestamp)
{
    public MowerTelemetry Normalize() =>
        this with
        {
            MowerId = MowerFleet.NormalizeMowerId(MowerId),
            EnrollmentStatus = string.IsNullOrWhiteSpace(EnrollmentStatus) ? "unknown" : EnrollmentStatus.Trim(),
            Mode = string.IsNullOrWhiteSpace(Mode) ? "idle" : Mode.Trim(),
            LastCommand = string.IsNullOrWhiteSpace(LastCommand) ? "none" : LastCommand.Trim(),
            BatteryPercent = Math.Clamp(BatteryPercent, 0, 100)
        };
}

public sealed record MowerSnapshot(
    string MowerId,
    string DisplayName,
    string ParkName,
    string? DeviceId,
    string EnrollmentStatus,
    string Mode,
    bool BladeEnabled,
    double Latitude,
    double Longitude,
    double Heading,
    double BatteryPercent,
    string LastCommand,
    DateTimeOffset LastUpdated,
    int ActiveConnectionCount,
    string BackendReplica);

public sealed record MowerCommand(
    string MowerId,
    string Command,
    string RequestedBy,
    DateTimeOffset Timestamp,
    string BackendReplica);

public static class MowerRuntime
{
    public static string ReplicaOrdinal =>
        Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1";
}
