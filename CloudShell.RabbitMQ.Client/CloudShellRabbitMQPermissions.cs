namespace CloudShell.RabbitMQ.Client;

public static class CloudShellRabbitMQPermissions
{
    public const string Publish =
        "CloudShell.Messaging/rabbitMQ/publish/action";

    public const string Consume =
        "CloudShell.Messaging/rabbitMQ/consume/action";

    public const string Configure =
        "CloudShell.Messaging/rabbitMQ/configure/action";
}
