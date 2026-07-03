# RabbitMQ Messaging Sample

This sample uses a C# launcher AppHost to declare a RabbitMQ broker plus two
application resources:

- `.NET Publisher` uses the .NET `RabbitMQ.Client` package.
- `Java Publisher` uses the RabbitMQ Java client.
- `RabbitMQ` exposes AMQP locally and the RabbitMQ management UI.

Both apps publish JSON events to a fan-out exchange. Each app has its own queue
bound to that exchange, so a message published by either app is delivered to
both app queues.

## Run

```bash
./cloudshell.sh run
```

In another terminal:

```bash
./cloudshell.sh start-apps
curl "http://localhost:5281/publish?message=hello-from-dotnet"
curl "http://localhost:5282/publish?message=hello-from-java"
curl "http://localhost:5281/messages"
curl "http://localhost:5282/messages"
```

The RabbitMQ management UI is available at `http://localhost:15678` with the
sample credentials `guest` / `guest`.

The AppHost `appsettings.json` also configures two local development identity
principals, `rabbitmq-operator` and `rabbitmq-reader`, under
`ResourceIdentity:BuiltIn:Users`. `CloudShell.LocalDevelopmentHost` picks those
up through delegated host settings and shows them in Resource Manager Access
control views without putting user accounts in the resource template.
