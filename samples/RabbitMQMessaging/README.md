# RabbitMQ Messaging Sample

This sample uses a C# launcher AppHost to declare a RabbitMQ broker plus two
application resources:

- `.NET Publisher` uses the .NET `RabbitMQ.Client` package.
- `Java Publisher` uses the RabbitMQ Java client.
- `RabbitMQ` exposes AMQP locally and the RabbitMQ management UI.

Both apps publish JSON events to a fan-out exchange. Each app has its own queue
bound to that exchange, so a message published by either app is delivered to
both app queues.

The sample also emits CloudShell trace spans for HTTP publish requests,
RabbitMQ publish operations, and RabbitMQ consume operations. Trace context is
propagated through RabbitMQ message headers, so Resource Manager can show the
publisher and both consumers as part of the same trace.

The AppHost also uses the standard resource builder identity APIs:
`.WithIdentity(...)`, `.ProvisionIdentityOnStartup()`, and `.Allow(...)`. The
two application resources receive CloudShell identities, and the broker grants
those identities RabbitMQ configure, publish, and consume permissions. The
generated template emits that intent as portable `identity.*` and
`access.grants` attributes so other launcher languages can produce the same
model.

The broker virtual host is RabbitMQ provider configuration declared through
the RabbitMQ resource. The sample opts into CloudShell-managed RabbitMQ
bootstrap credentials and does not pass those credentials to the apps.
RabbitMQ-native workload users and virtual-host permissions remain
provider-owned runtime state reconciled by the RabbitMQ provider from
CloudShell resource-identity grants.

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

The RabbitMQ management UI is available at `http://localhost:15678`. The
sample declares the `cloudshell_sample` virtual host in `AppHost/appsettings.json`.
`./cloudshell.sh start-apps` starts the broker and then starts both workload
apps. The .NET workload uses `CloudShell.RabbitMQ.Client` to resolve
RabbitMQ-native credentials from its CloudShell resource identity before
creating a normal `RabbitMQ.Client` connection. The Java workload follows the
same protocol directly by requesting a CloudShell resource identity token and
calling the RabbitMQ credential endpoint itself. CloudShell checks the
RabbitMQ grants, materializes the matching broker-native user if needed,
records the request, and returns the username, password, and virtual host
needed by each native RabbitMQ client.

After publishing messages, open the Resource Manager **Traces** view for the
.NET or Java resource to inspect the correlated HTTP, publish, and consume
spans.

The AppHost `appsettings.json` also configures two local development identity
principals, `rabbitmq-operator` and `rabbitmq-reader`, under
`ResourceIdentity:BuiltIn:Users`. `CloudShell.LocalDevelopmentHost` picks those
up through delegated host settings and shows them in Resource Manager Access
control views without putting user accounts in the resource template.
