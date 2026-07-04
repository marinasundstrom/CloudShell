namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// Provider-owned RabbitMQ credential material resolved for a CloudShell principal.
/// </summary>
/// <remarks>
/// The password may be short-lived RabbitMQ-native access material and must
/// not be logged, stored in resource metadata, or surfaced in diagnostics.
/// </remarks>
public sealed record CloudShellRabbitMQCredential(
    string Username,
    string Password,
    string VirtualHost,
    DateTimeOffset? ExpiresOn = null);
