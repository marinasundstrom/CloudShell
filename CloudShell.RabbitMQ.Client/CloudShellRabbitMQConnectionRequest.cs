namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// Request to create a RabbitMQ connection for the current CloudShell principal.
/// </summary>
/// <param name="RabbitMQResourceName">
/// The CloudShell RabbitMQ resource name or stable resource identifier.
/// </param>
/// <param name="HostName">The RabbitMQ broker host name.</param>
/// <param name="Port">The RabbitMQ AMQP port.</param>
/// <param name="Permission">
/// Optional broker permission requested by the workload. When omitted, the
/// provider uses its default configure policy for the target resource.
/// </param>
/// <param name="ClientProvidedName">Optional RabbitMQ client connection name.</param>
public sealed record CloudShellRabbitMQConnectionRequest(
    string RabbitMQResourceName,
    string HostName,
    int Port = 5672,
    string? Permission = null,
    string? ClientProvidedName = null)
{
    public string RabbitMQResourceName { get; init; } =
        string.IsNullOrWhiteSpace(RabbitMQResourceName)
            ? throw new ArgumentException(
                "RabbitMQ resource name is required.",
                nameof(RabbitMQResourceName))
            : RabbitMQResourceName;

    public string HostName { get; init; } =
        string.IsNullOrWhiteSpace(HostName)
            ? throw new ArgumentException(
                "RabbitMQ host name is required.",
                nameof(HostName))
            : HostName;

    public int Port { get; init; } = Port > 0
        ? Port
        : throw new ArgumentOutOfRangeException(
            nameof(Port),
            "RabbitMQ port must be greater than zero.");
}
