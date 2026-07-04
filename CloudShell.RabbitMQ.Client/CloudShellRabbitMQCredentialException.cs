namespace CloudShell.RabbitMQ.Client;

public sealed class CloudShellRabbitMQCredentialException : Exception
{
    public CloudShellRabbitMQCredentialException(string message)
        : base(message)
    {
    }

    public CloudShellRabbitMQCredentialException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
