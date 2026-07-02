package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

public final class NetworkResource extends ResourceBuilder<NetworkResource> {
    private String networkKind = "Host";
    private String hostReadiness = "hostReady";

    NetworkResource(String name) {
        super(name, "cloudshell.network", "cloudshell.network");
    }

    public NetworkResource withNetworkKind(String networkKind) {
        this.networkKind = networkKind;
        return this;
    }

    public NetworkResource withHostReadiness(String hostReadiness) {
        this.hostReadiness = hostReadiness;
        return this;
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        line(builder, indent + 1, "\"network\": {");
        property(builder, indent + 2, "kind", json(networkKind), true);
        property(builder, indent + 2, "hostReadiness", json(hostReadiness), false);
        line(builder, indent + 1, "}");
        line(builder, indent, "}");
        return builder.toString();
    }

    @Override
    protected NetworkResource self() {
        return this;
    }
}
