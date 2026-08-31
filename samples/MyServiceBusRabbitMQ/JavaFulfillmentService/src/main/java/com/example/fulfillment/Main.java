package com.example.fulfillment;

import OrderFulfillment.Contracts.OrderActivity;
import com.myservicebus.MessageBus;
import com.myservicebus.MessageBusServices;
import com.myservicebus.PublishEndpoint;
import com.myservicebus.TransportFactory;
import com.myservicebus.di.ServiceCollection;
import com.myservicebus.di.ServiceProvider;
import com.myservicebus.di.ServiceScope;
import com.myservicebus.inspection.BusInspectionProvider;
import com.myservicebus.inspection.InspectionServices;
import com.myservicebus.monitoring.MonitoringExporter;
import com.myservicebus.monitoring.MonitoringExporterOptions;
import com.myservicebus.monitoring.MonitoringServices;
import com.myservicebus.persistence.OutboxDeliveryService;
import com.myservicebus.persistence.OutboxSession;
import com.myservicebus.persistence.postgresql.PostgreSqlOutboxDelivery;
import com.myservicebus.persistence.postgresql.PostgreSqlOutboxSession;
import com.myservicebus.persistence.postgresql.PostgreSqlSchema;
import com.myservicebus.rabbitmq.RabbitMqFactoryConfigurator;
import io.javalin.Javalin;
import java.sql.Connection;
import java.sql.PreparedStatement;
import java.sql.Statement;
import java.time.Instant;
import java.util.Map;
import java.util.UUID;
import org.postgresql.ds.PGSimpleDataSource;

public final class Main {
    private static final String SERVICE_NAME = "fulfillment";

    private Main() {
    }

    public static void main(String[] args) throws Exception {
        PGSimpleDataSource dataSource = createDataSource();
        PostgreSqlSchema.ensureCreated(dataSource);
        ensureApplicationSchema(dataSource);

        ServiceCollection services = ServiceCollection.create();
        services.from(InspectionServices.class).addInspection();
        MonitoringExporterOptions monitoringOptions = new MonitoringExporterOptions();
        monitoringOptions.setServiceAddress(java.net.URI.create(env("MONITORING_SERVICE_URL", "http://localhost:5310")));
        monitoringOptions.setApplicationName("Fulfillment");
        monitoringOptions.setInstanceId(env("HOSTNAME", UUID.randomUUID().toString()));
        monitoringOptions.getLabels().put("group", "order-fulfillment");
        monitoringOptions.getLabels().put("language", "java");
        MonitoringExporter monitoring = MonitoringServices.addMonitoring(services, monitoringOptions);

        services.from(MessageBusServices.class).addServiceBus(configurator -> {
            configurator.useBusOutbox();
            configurator.addConsumer(
                    OrderActivityConsumer.class,
                    OrderActivity.class,
                    "fulfillment-order-activity",
                    null);
            configurator.using(RabbitMqFactoryConfigurator.class, (context, rabbit) -> {
                rabbit.host(env("RABBITMQ_HOST", "localhost"), Integer.parseInt(env("RABBITMQ_PORT", "5672")), credentials -> {
                    credentials.username(env("RABBITMQ_USER", "myservicebus"));
                    credentials.password(env("RABBITMQ_PASSWORD", "local-development-only"));
                });
                rabbit.configureEndpoints(context);
            });
        });

        ServiceProvider provider = services.buildServiceProvider();
        MessageBus bus = provider.getRequiredService(MessageBus.class);
        TransportFactory transport = provider.getRequiredService(TransportFactory.class);
        OutboxDeliveryService delivery = PostgreSqlOutboxDelivery.create(
                dataSource,
                transport,
                SERVICE_NAME,
                options -> {
                    options.setOwnerId("java-" + env("HOSTNAME", UUID.randomUUID().toString()));
                    options.setPollInterval(java.time.Duration.ofMillis(250));
                });

        bus.start();
        monitoring.start(provider.getRequiredService(BusInspectionProvider.class));
        delivery.start();

        Javalin app = Javalin.create().start(Integer.parseInt(env("HTTP_PORT", "8080")));
        app.post("/fulfill/{orderId}", context -> {
            OrderActivity activity = new OrderActivity(
                    UUID.randomUUID().toString(),
                    context.pathParam("orderId"),
                    "OrderFulfilled",
                    "java",
                    Instant.now().toString());

            try (Connection connection = dataSource.getConnection()) {
                connection.setAutoCommit(false);
                try (PreparedStatement statement = connection.prepareStatement(
                        "INSERT INTO order_fulfillment_event (event_id, order_id, kind, origin, occurred_at_utc) VALUES (?, ?, ?, ?, ?::timestamptz)")) {
                    statement.setString(1, activity.getEventId());
                    statement.setString(2, activity.getOrderId());
                    statement.setString(3, activity.getKind());
                    statement.setString(4, activity.getOrigin());
                    statement.setString(5, activity.getOccurredAtUtc());
                    statement.executeUpdate();
                }

                try (ServiceScope scope = provider.createScope();
                        OutboxSession.Registration ignored = PostgreSqlOutboxSession.useTransaction(
                                scope.getServiceProvider().getRequiredService(OutboxSession.class),
                                connection,
                                SERVICE_NAME)) {
                    scope.getServiceProvider().getRequiredService(PublishEndpoint.class)
                            .publish(activity)
                            .join();
                }
                connection.commit();
            }
            context.status(202).json(activity);
        });
        app.get("/received", context -> context.json(OrderActivityConsumer.received()));
        app.get("/health/live", context -> context.json(Map.of(
                "status", "live",
                "language", "Java",
                "replica", env("HOSTNAME", "local"))));

        Runtime.getRuntime().addShutdownHook(new Thread(() -> {
            app.stop();
            delivery.close();
            monitoring.close();
            try {
                bus.stop();
            } catch (Exception exception) {
                throw new IllegalStateException("Failed to stop MyServiceBus", exception);
            }
        }));
    }

    private static PGSimpleDataSource createDataSource() {
        PGSimpleDataSource dataSource = new PGSimpleDataSource();
        dataSource.setServerNames(new String[] { env("POSTGRES_HOST", "localhost") });
        dataSource.setPortNumbers(new int[] { 5432 });
        dataSource.setDatabaseName("myservicebus");
        dataSource.setUser("postgres");
        return dataSource;
    }

    private static void ensureApplicationSchema(PGSimpleDataSource dataSource) throws Exception {
        try (Connection connection = dataSource.getConnection(); Statement statement = connection.createStatement()) {
            statement.execute("""
                    CREATE TABLE IF NOT EXISTS order_fulfillment_event (
                        event_id text PRIMARY KEY,
                        order_id text NOT NULL,
                        kind text NOT NULL,
                        origin text NOT NULL,
                        occurred_at_utc timestamptz NOT NULL
                    )
                    """);
        }
    }

    private static String env(String name, String fallback) {
        return System.getenv().getOrDefault(name, fallback);
    }
}
