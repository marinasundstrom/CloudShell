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

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => Results.Redirect("/health"));
app.MapGet("/health", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("healthy")));
app.MapGet("/alive", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("alive")));
app.MapGet("/replica", () => Results.Ok(SignalRReplicaRuntime.CreateSnapshot("current")));
app.MapHub<ReplicaHub>("/hubs/replicas");

app.Run();

public sealed class ReplicaHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync(
            "ReplicaConnected",
            CreateMessage("Connected to SignalR backend."));
        await base.OnConnectedAsync();
    }

    public async Task SendMessage(string text)
    {
        await Clients.All.SendAsync(
            "ReplicaMessage",
            CreateMessage(string.IsNullOrWhiteSpace(text) ? "Ping" : text.Trim()));
    }

    private SignalRReplicaMessage CreateMessage(string text) =>
        new(
            text,
            SignalRReplicaRuntime.GetReplicaOrdinal(),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application.container-app:signalr-api",
            Environment.MachineName,
            Context.ConnectionId,
            DateTimeOffset.UtcNow);
}

internal static class SignalRReplicaRuntime
{
    public static ReplicaSnapshot CreateSnapshot(string status) =>
        new(
            status,
            GetReplicaOrdinal(),
            Environment.GetEnvironmentVariable("CLOUDSHELL_RESOURCE_ID") ?? "application.container-app:signalr-api",
            Environment.MachineName,
            DateTimeOffset.UtcNow);

    public static string GetReplicaOrdinal() =>
        Environment.GetEnvironmentVariable("CLOUDSHELL_REPLICA_ORDINAL") ?? "1";
}

public sealed record ReplicaSnapshot(
    string Status,
    string Replica,
    string Resource,
    string Machine,
    DateTimeOffset Timestamp);

public sealed record SignalRReplicaMessage(
    string Text,
    string Replica,
    string Resource,
    string Machine,
    string ConnectionId,
    DateTimeOffset Timestamp);
