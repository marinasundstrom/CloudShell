package com.example.fulfillment;

import OrderFulfillment.Contracts.OrderActivity;
import com.myservicebus.ConsumeContext;
import com.myservicebus.Consumer;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CopyOnWriteArrayList;

public final class OrderActivityConsumer implements Consumer<OrderActivity> {
    private static final CopyOnWriteArrayList<OrderActivity> RECEIVED = new CopyOnWriteArrayList<>();

    public static List<OrderActivity> received() {
        return List.copyOf(RECEIVED);
    }

    @Override
    public CompletableFuture<Void> consume(ConsumeContext<OrderActivity> context) {
        OrderActivity activity = context.getMessage();
        RECEIVED.add(activity);
        System.out.printf(
                "Java replica %s observed %s for order %s from %s%n",
                System.getenv().getOrDefault("HOSTNAME", "local"),
                activity.getKind(),
                activity.getOrderId(),
                activity.getOrigin());
        return CompletableFuture.completedFuture(null);
    }
}
