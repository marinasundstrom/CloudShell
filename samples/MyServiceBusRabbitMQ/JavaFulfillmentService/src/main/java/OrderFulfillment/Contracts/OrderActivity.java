package OrderFulfillment.Contracts;

public final class OrderActivity {
    private String eventId;
    private String orderId;
    private String kind;
    private String origin;
    private String occurredAtUtc;

    public OrderActivity() {
    }

    public OrderActivity(String eventId, String orderId, String kind, String origin, String occurredAtUtc) {
        this.eventId = eventId;
        this.orderId = orderId;
        this.kind = kind;
        this.origin = origin;
        this.occurredAtUtc = occurredAtUtc;
    }

    public String getEventId() { return eventId; }
    public void setEventId(String value) { eventId = value; }
    public String getOrderId() { return orderId; }
    public void setOrderId(String value) { orderId = value; }
    public String getKind() { return kind; }
    public void setKind(String value) { kind = value; }
    public String getOrigin() { return origin; }
    public void setOrigin(String value) { origin = value; }
    public String getOccurredAtUtc() { return occurredAtUtc; }
    public void setOccurredAtUtc(String value) { occurredAtUtc = value; }
}
