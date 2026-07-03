namespace CloudShell.ControlPlane.Providers;

public static class RabbitMQResourceDefaults
{
    public const string ContainerImage = "rabbitmq:3-management";
    public const string DataPath = "/var/lib/rabbitmq";
    public const string DefaultUsername = "guest";
    public const string DefaultPassword = "guest";
    public const string UsernameConfigurationKey = "CloudShell:RabbitMQ:Username";
    public const string PasswordConfigurationKey = "CloudShell:RabbitMQ:Password";
}
