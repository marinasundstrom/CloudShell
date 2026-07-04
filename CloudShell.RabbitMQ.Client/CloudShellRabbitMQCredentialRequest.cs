namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// Request to resolve RabbitMQ broker access for the current CloudShell principal.
/// </summary>
/// <param name="RabbitMQResourceName">
/// The CloudShell RabbitMQ resource name or stable resource identifier.
/// </param>
/// <param name="Permission">
/// Optional broker permission requested by the workload. When omitted, the
/// provider uses its default configure policy for the target resource.
/// </param>
public sealed record CloudShellRabbitMQCredentialRequest(
    string RabbitMQResourceName,
    string? Permission = null)
{
    public string RabbitMQResourceName { get; init; } =
        string.IsNullOrWhiteSpace(RabbitMQResourceName)
            ? throw new ArgumentException(
                "RabbitMQ resource name is required.",
                nameof(RabbitMQResourceName))
            : RabbitMQResourceName;
}
