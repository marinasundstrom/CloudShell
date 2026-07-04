namespace CloudShell.RabbitMQ.Client;

public interface ICloudShellRabbitMQCredentialResolver
{
    ValueTask<CloudShellRabbitMQCredential> ResolveCredentialAsync(
        CloudShellRabbitMQCredentialRequest request,
        CancellationToken cancellationToken = default);
}
