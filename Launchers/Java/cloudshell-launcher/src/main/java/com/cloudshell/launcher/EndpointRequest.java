package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

final class EndpointRequest {
    private final String name;
    private final String protocol;
    private final String host;
    private final int port;
    private final int targetPort;
    private final NetworkResource network;

    EndpointRequest(String name, String protocol, String host, int port, int targetPort, NetworkResource network) {
        this.name = name;
        this.protocol = protocol;
        this.host = host;
        this.port = port;
        this.targetPort = targetPort;
        this.network = network;
    }

    void appendJson(StringBuilder builder, int indent) {
        line(builder, indent, "\"endpointRequests\": [");
        line(builder, indent + 1, "{");
        property(builder, indent + 2, "name", json(name), true);
        property(builder, indent + 2, "protocol", json(protocol), true);
        property(builder, indent + 2, "targetPort", Integer.toString(targetPort), true);
        property(builder, indent + 2, "host", json(host), true);
        property(builder, indent + 2, "port", Integer.toString(port), true);
        property(builder, indent + 2, "exposure", json("Local"), true);
        line(builder, indent + 2, "\"network\": {");
        property(builder, indent + 3, "resourceId", json(network.resourceId()), true);
        property(builder, indent + 3, "relationship", json("reference"), true);
        property(builder, indent + 3, "addressingMode", json("resourceId"), true);
        property(builder, indent + 3, "typeId", json(network.type()), true);
        property(builder, indent + 3, "providerId", json(network.providerId()), false);
        line(builder, indent + 2, "}");
        line(builder, indent + 1, "}");
        line(builder, indent, "]");
    }
}
