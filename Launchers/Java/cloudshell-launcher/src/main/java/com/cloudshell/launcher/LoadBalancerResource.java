package com.cloudshell.launcher;

import static com.cloudshell.launcher.JsonSupport.json;
import static com.cloudshell.launcher.JsonSupport.line;
import static com.cloudshell.launcher.JsonSupport.property;

import java.util.ArrayList;
import java.util.List;

public final class LoadBalancerResource extends ResourceBuilder<LoadBalancerResource> {
    private String provider;
    private String hostResourceId;
    private final List<Entrypoint> entrypoints = new ArrayList<>();
    private final List<Route> routes = new ArrayList<>();

    LoadBalancerResource(String name) {
        super(name, "cloudshell.loadBalancer", "cloudshell.load-balancer");
    }

    public LoadBalancerResource withProvider(String provider) {
        this.provider = provider;
        return this;
    }

    public LoadBalancerResource useHost(ResourceBuilder<?> host) {
        hostResourceId = host.resourceId();
        dependsOn(host);
        return this;
    }

    public LoadBalancerResource exposeHttp() {
        return exposeHttp(80);
    }

    public LoadBalancerResource exposeHttp(int port) {
        return addEntrypoint("http", "Http", port, "Public", null);
    }

    public LoadBalancerResource exposeHttps(SecretsVaultResource.CertificateReference certificate) {
        return exposeHttps(certificate, 443);
    }

    public LoadBalancerResource exposeHttps(
        SecretsVaultResource.CertificateReference certificate,
        int port) {
        return addEntrypoint("https", "Https", port, "Public", certificate);
    }

    public LoadBalancerResource exposeTcp(int port) {
        return addEntrypoint("tcp-" + port, "Tcp", port, "Public", null);
    }

    public LoadBalancerResource mapHost(
        String host,
        ResourceBuilder<?> target,
        int port) {
        return mapHost(host, target, port, "http");
    }

    public LoadBalancerResource mapHost(
        String host,
        ResourceBuilder<?> target,
        int port,
        String entrypoint) {
        Target routeTarget = new Target(target, null, port);
        return addRoute(
            "Http",
            createRouteId("Http", host, null, 0, target, routeTarget),
            host + " to " + target.resourceId() + ":" + port,
            entrypoint,
            new Match(host, null, 0),
            routeTarget,
            target);
    }

    public LoadBalancerResource mapPath(
        String host,
        String pathPrefix,
        ResourceBuilder<?> target,
        int port) {
        return mapPath(host, pathPrefix, target, port, "http");
    }

    public LoadBalancerResource mapPath(
        String host,
        String pathPrefix,
        ResourceBuilder<?> target,
        int port,
        String entrypoint) {
        Target routeTarget = new Target(target, null, port);
        return addRoute(
            "Http",
            createRouteId("Http", host, pathPrefix, 0, target, routeTarget),
            host + pathPrefix + " to " + target.resourceId() + ":" + port,
            entrypoint,
            new Match(host, pathPrefix, 0),
            routeTarget,
            target);
    }

    public LoadBalancerResource mapTcp(int port, ResourceBuilder<?> target, int targetPort) {
        Target routeTarget = new Target(target, null, targetPort);
        return addRoute(
            "Tcp",
            createRouteId("Tcp", null, null, port, target, routeTarget),
            "tcp " + port + " to " + target.resourceId() + ":" + targetPort,
            "tcp-" + port,
            new Match(null, null, port),
            routeTarget,
            target);
    }

    @Override
    String toJson(int indent) {
        StringBuilder builder = new StringBuilder();
        line(builder, indent, "{");
        appendCommon(builder, indent + 1);
        line(builder, indent + 1, "\"loadBalancer\": {");
        property(builder, indent + 2, "provider", json(provider), true);
        property(builder, indent + 2, "hostResourceId", json(hostResourceId), true);
        appendEntrypoints(builder, indent + 2, true);
        appendRoutes(builder, indent + 2);
        line(builder, indent + 1, "}");
        line(builder, indent, "}");
        return builder.toString();
    }

    @Override
    protected LoadBalancerResource self() {
        return this;
    }

    private LoadBalancerResource addEntrypoint(
        String name,
        String protocol,
        int port,
        String exposure,
        SecretsVaultResource.CertificateReference certificate) {
        entrypoints.removeIf(entrypoint -> entrypoint.name().equalsIgnoreCase(name));
        entrypoints.add(new Entrypoint(name, protocol, port, exposure, certificate));
        return this;
    }

    private LoadBalancerResource addRoute(
        String kind,
        String id,
        String name,
        String entrypoint,
        Match match,
        Target target,
        ResourceBuilder<?> targetResource) {
        routes.removeIf(route -> route.id().equalsIgnoreCase(id));
        routes.add(new Route(id, name, kind, entrypoint, match, target));
        dependsOn(targetResource);
        return this;
    }

    private String createRouteId(
        String kind,
        String host,
        String pathPrefix,
        int port,
        ResourceBuilder<?> target,
        Target routeTarget) {
        String source;
        if ("Tcp".equalsIgnoreCase(kind)) {
            source = "tcp-" + port;
        } else {
            source = ((host == null ? "" : host) + "-" + (pathPrefix == null ? "" : pathPrefix))
                .replace("/", "-")
                .replaceAll("^-+|-+$", "");
        }

        if (source.isBlank()) {
            source = "route";
        }

        String targetPart = routeTarget.endpointName();
        if (targetPart == null && routeTarget.port() > 0) {
            targetPart = Integer.toString(routeTarget.port());
        }
        if (targetPart == null || targetPart.isBlank()) {
            targetPart = "target";
        }

        return resourceId() + ":route:" + source + ":" + target.resourceId() + ":" + targetPart;
    }

    private void appendEntrypoints(StringBuilder builder, int indent, boolean trailingComma) {
        line(builder, indent, "\"entrypointDefinitions\": [");
        for (int index = 0; index < entrypoints.size(); index++) {
            Entrypoint entrypoint = entrypoints.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "name", json(entrypoint.name()), true);
            property(builder, indent + 2, "protocol", json(entrypoint.protocol()), true);
            property(builder, indent + 2, "port", Integer.toString(entrypoint.port()), true);
            property(builder, indent + 2, "exposure", json(entrypoint.exposure()), entrypoint.certificate() != null);
            if (entrypoint.certificate() != null) {
                appendCertificateReference(builder, indent + 2, entrypoint.certificate());
            }

            line(builder, indent + 1, "}" + (index < entrypoints.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]" + (trailingComma ? "," : ""));
    }

    private void appendRoutes(StringBuilder builder, int indent) {
        line(builder, indent, "\"routeDefinitions\": [");
        for (int index = 0; index < routes.size(); index++) {
            Route route = routes.get(index);
            line(builder, indent + 1, "{");
            property(builder, indent + 2, "id", json(route.id()), true);
            property(builder, indent + 2, "name", json(route.name()), true);
            property(builder, indent + 2, "kind", json(route.kind()), true);
            property(builder, indent + 2, "entrypointName", json(route.entrypoint()), true);
            appendMatch(builder, indent + 2, route.match(), true);
            appendTarget(builder, indent + 2, route.target());
            line(builder, indent + 1, "}" + (index < routes.size() - 1 ? "," : ""));
        }

        line(builder, indent, "]");
    }

    private void appendCertificateReference(
        StringBuilder builder,
        int indent,
        SecretsVaultResource.CertificateReference certificate) {
        line(builder, indent, "\"certificateRef\": {");
        property(builder, indent + 1, "vaultResourceId", json(certificate.vaultResourceId()), true);
        property(builder, indent + 1, "name", json(certificate.name()), certificate.version() != null);
        if (certificate.version() != null) {
            property(builder, indent + 1, "version", json(certificate.version()), false);
        }

        line(builder, indent, "}");
    }

    private void appendMatch(StringBuilder builder, int indent, Match match, boolean trailingComma) {
        line(builder, indent, "\"match\": {");
        boolean hasHost = match.host() != null;
        boolean hasPath = match.pathPrefix() != null;
        boolean hasPort = match.port() > 0;
        if (hasHost) {
            property(builder, indent + 1, "host", json(match.host()), hasPath || hasPort);
        }

        if (hasPath) {
            property(builder, indent + 1, "pathPrefix", json(match.pathPrefix()), hasPort);
        }

        if (hasPort) {
            property(builder, indent + 1, "port", Integer.toString(match.port()), false);
        }

        line(builder, indent, "}" + (trailingComma ? "," : ""));
    }

    private void appendTarget(StringBuilder builder, int indent, Target target) {
        line(builder, indent, "\"target\": {");
        line(builder, indent + 1, "\"resource\": {");
        property(builder, indent + 2, "resourceId", json(target.resource().resourceId()), true);
        property(builder, indent + 2, "relationship", json("reference"), true);
        property(builder, indent + 2, "addressingMode", json("resourceId"), true);
        property(builder, indent + 2, "typeId", json(target.resource().type()), true);
        property(builder, indent + 2, "providerId", json(target.resource().providerId()), false);
        line(builder, indent + 1, "}" + (target.endpointName() != null || target.port() > 0 ? "," : ""));
        if (target.endpointName() != null) {
            property(builder, indent + 1, "endpointName", json(target.endpointName()), target.port() > 0);
        }

        if (target.port() > 0) {
            property(builder, indent + 1, "port", Integer.toString(target.port()), false);
        }

        line(builder, indent, "}");
    }

    private record Entrypoint(
        String name,
        String protocol,
        int port,
        String exposure,
        SecretsVaultResource.CertificateReference certificate) {
    }

    private record Match(String host, String pathPrefix, int port) {
    }

    private record Target(ResourceBuilder<?> resource, String endpointName, int port) {
    }

    private record Route(
        String id,
        String name,
        String kind,
        String entrypoint,
        Match match,
        Target target) {
    }
}
