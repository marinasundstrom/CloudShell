using RabbitMQ.Client;

namespace CloudShell.RabbitMQ.Client;

/// <summary>
/// Creates RabbitMQ client connections whose credentials are resolved by CloudShell.
/// </summary>
/// <remarks>
/// Experimental API. This keeps ordinary RabbitMQ clients supported while
/// moving CloudShell credential exchange behind a small client helper.
/// </remarks>
public sealed class CloudShellRabbitMQConnectionFactory
{
    private readonly ICloudShellRabbitMQCredentialResolver resolver;
    private readonly CloudShellRabbitMQClientOptions options;

    public CloudShellRabbitMQConnectionFactory(
        ICloudShellRabbitMQCredentialResolver resolver,
        CloudShellRabbitMQClientOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        this.resolver = resolver;
        this.options = options ?? new CloudShellRabbitMQClientOptions();
    }

    public async ValueTask<ConnectionFactory> CreateConnectionFactoryAsync(
        CancellationToken cancellationToken = default) =>
        await CreateConnectionFactoryAsync(
            CreateRequestFromOptions(),
            cancellationToken);

    public async ValueTask<ConnectionFactory> CreateConnectionFactoryAsync(
        CloudShellRabbitMQConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credential = await resolver.ResolveCredentialAsync(
            new CloudShellRabbitMQCredentialRequest(
                request.RabbitMQResourceName,
                request.Permission),
            cancellationToken);

        return new ConnectionFactory
        {
            HostName = request.HostName,
            Port = request.Port,
            UserName = credential.Username,
            Password = credential.Password,
            VirtualHost = credential.VirtualHost
        };
    }

    public async ValueTask<IConnection> CreateConnectionAsync(
        CancellationToken cancellationToken = default) =>
        await CreateConnectionAsync(
            CreateRequestFromOptions(),
            cancellationToken);

    public async ValueTask<IConnection> CreateConnectionAsync(
        CloudShellRabbitMQConnectionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var factory = await CreateConnectionFactoryAsync(request, cancellationToken);
        return string.IsNullOrWhiteSpace(request.ClientProvidedName)
            ? factory.CreateConnection()
            : factory.CreateConnection(request.ClientProvidedName);
    }

    private CloudShellRabbitMQConnectionRequest CreateRequestFromOptions()
    {
        if (string.IsNullOrWhiteSpace(options.RabbitMQResourceName))
        {
            throw new CloudShellRabbitMQCredentialException(
                "CloudShell RabbitMQ connection factory requires a RabbitMQ resource name.");
        }

        if (string.IsNullOrWhiteSpace(options.HostName))
        {
            throw new CloudShellRabbitMQCredentialException(
                "CloudShell RabbitMQ connection factory requires a RabbitMQ host name.");
        }

        return new CloudShellRabbitMQConnectionRequest(
            options.RabbitMQResourceName,
            options.HostName,
            options.Port ?? 5672,
            options.Permission,
            options.ClientProvidedName);
    }
}
