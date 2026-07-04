# Built-in Resource Types

This catalog lists the built-in Resource model types that can be declared in a
`ResourceTemplate`. YAML is the preferred authoring format. Each sample uses
the authoring alias `type`, which maps to `ResourceDefinition.TypeId`.

For the common template envelope and serializer rules, see
[Resource templates](../resource-templates.md). For the generic
`ResourceDefinition` shape, references, and nested attribute grouping, see
[ResourceDefinition structure](../resource-definition-structure.md).

## Authoring Rules

Resource templates contain a `resources` array:

```yaml
resources:
  - type: application.container-app
    name: api
    container:
      image: ghcr.io/acme/api:dev
```

Resource IDs default to `<type>:<name>` when omitted. Use `resourceId` only
when a resource must keep a specific stable ID. Dotted attribute IDs are
authored as nested YAML groups, so `container.image` becomes
`container: { image: ... }`.

Simple dependency references can use compact resource-id form:

```yaml
dependsOn:
  - resourceId: cloudshell.container-host:default
```

Relationship-specific references, such as DNS ownership and target mappings,
should include `relationship` and `addressingMode` explicitly.

Secrets must not be written to ordinary resource attributes. The only built-in
secret value authoring path is the create-only `seed.secrets` attribute on
`secrets.vault`; those values are materialized into provider-owned runtime
state and omitted from accepted graph state and normal exports.

## Type Summary

| Type | Class | Provider id | Authoring status |
| --- | --- | --- | --- |
| `application.executable` | `executable` | `applications.executable` | User-authored local executable. |
| `application.aspnet-core-project` | `project` | `applications.aspnet-core-project` | User-authored ASP.NET Core project. |
| `application.javascript-app` | `project` | `applications.javascript-app` | User-authored JavaScript/Node.js project. |
| `application.java-app` | `project` | `applications.java-app` | User-authored Java/JVM project. |
| `application.go-app` | `project` | `applications.go-app` | User-authored Go project. |
| `application.container-app` | `container` | `applications.container-app` | User-authored deployable container app. |
| `application.sql-server` | `service` | `applications.sql-server` | User-authored local-development SQL Server. |
| `application.sql-database` | `service` | `applications.sql-database` | User-authored SQL database child/resource. |
| `application.rabbitmq` | `service` | `applications.rabbitmq` | User-authored local-development RabbitMQ broker. |
| `cloudshell.service` | `service` | `cloudshell.service` | Optional logical service facade. |
| `configuration.store` | `configuration` | `configuration` | User-authored configuration service. |
| `configuration.host` | `configuration` | `host-configuration` | Usually host/provider-authored configuration source. |
| `secrets.vault` | `secretsVault` | `secrets-vault` | User-authored secrets service. |
| `cloudshell.identity-provisioning` | `infrastructure` | `identity.provisioning` | User-authored identity provisioning integration. |
| `cloudshell.container-host` | `infrastructure` | `container-host.reference` | User-authored generic container host. |
| `docker.host` | `infrastructure` | `docker` | User-authored or provider-projected Docker host. |
| `docker.container` | `container` | `docker` | Low-level Docker container resource; prefer container apps for managed workloads. |
| `cloudshell.storage` | `storage` | `cloudshell.storage` | User-authored storage provider/resource. |
| `storage.volume` | `storage` | `storage.localVolume` | Legacy/simple local volume type. |
| `cloudshell.volume` | `storage` | `cloudshell.storage` | User-authored CloudShell volume. |
| `cloudshell.network` | `network` | `cloudshell.network` | User-authored logical network. |
| `cloudshell.virtualNetwork` | `network` | `cloudshell.network` | User-authored virtual network. |
| `cloudshell.hostNetworking.local` | `infrastructure` | `cloudshell.hostNetworking` | Usually host-authored local host networking provider. |
| `cloudshell.hostNetworking.macos` | `infrastructure` | `cloudshell.hostNetworking` | Usually host-authored macOS host networking provider. |
| `cloudshell.loadBalancer` | `network` | `cloudshell.load-balancer` | User-authored load balancer. |
| `cloudshell.dnsZone` | `network` | `cloudshell.dns` | User-authored DNS/name zone. |
| `cloudshell.nameMapping` | `network` | `cloudshell.dns` | User-authored DNS/name mapping. |

## Relationship Scenarios

Some resources are most useful when authored with related resources. These
examples show the common combinations and the resource-model relationship each
one is expressing.

### Container App With Host, Configuration, Secrets, And Storage

A container app can rely on a selected container host, read configuration and
secrets through service clients, and mount a CloudShell volume. The stable
application resource stays `application.container-app`; the host and volume
are placement and storage dependencies.

```yaml
resources:
  - type: docker.host
    name: local

  - type: configuration.store
    name: settings
    seed:
      entries:
        - name: App--Message
          value: Hello from CloudShell

  - type: secrets.vault
    name: secrets
    seed:
      secrets:
        - name: App--ApiKey
          value: local-development-secret

  - type: cloudshell.volume
    name: app-data
    storage:
      volume:
        medium: FileSystem
        accessMode: ReadWriteOnce

  - type: application.container-app
    name: api
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
      - resourceId: configuration.store:settings
        typeId: configuration.store
      - resourceId: secrets.vault:secrets
        typeId: secrets.vault
      - resourceId: cloudshell.volume:app-data
        typeId: cloudshell.volume
    container:
      image: ghcr.io/acme/api:dev
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 5080
          exposure: Local
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:app-data
            targetPath: /var/lib/app
```

Use this pattern when the app needs durable local state, local-development
configuration, or a specific Docker-compatible host. Omit the host dependency
when the graph should use the default resolved container host.

### SQL Server With Durable Storage And Database Resources

SQL Server is a service resource. The volume owns durable storage intent, the
SQL Server owns the running database service, and each SQL database can be a
separate child-style resource that depends on its server.

```yaml
resources:
  - type: cloudshell.volume
    name: sql-data
    storage:
      volume:
        medium: FileSystem
        accessMode: ReadWriteOnce
        persistent: true

  - type: application.sql-server
    name: sql
    dependsOn:
      - resourceId: cloudshell.volume:sql-data
        typeId: cloudshell.volume
    version: "2022"
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:sql-data
            targetPath: /var/opt/mssql

  - type: application.sql-database
    name: app-db
    dependsOn:
      - resourceId: application.sql-server:sql
        typeId: application.sql-server
    database:
      name: app
      ensureCreated: true
```

Use this pattern when the database should be inspectable, grantable, or
reconciled separately from the SQL Server runtime.

### RabbitMQ With Management UI And Durable Storage

RabbitMQ is a service resource. The broker owns AMQP connectivity and the
management endpoint, while the volume owns durable local broker state.

```yaml
resources:
  - type: cloudshell.volume
    name: rabbitmq-data
    storage:
      volume:
        medium: FileSystem
        accessMode: ReadWriteOnce
        persistent: true

  - type: application.rabbitmq
    name: rabbitmq
    dependsOn:
      - resourceId: cloudshell.volume:rabbitmq-data
        typeId: cloudshell.volume
    version: "3"
    endpointRequests:
      - name: amqp
        protocol: tcp
        targetPort: 5672
        port: 5672
        exposure: Local
      - name: management
        protocol: http
        targetPort: 15672
        port: 15672
        exposure: Local
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:rabbitmq-data
            targetPath: /var/lib/rabbitmq
```

Use this pattern when applications need a local message broker. Resource
Manager shows the broker as a managed service, links the management endpoint,
and can list broker-native queues and exchanges through the Management API.
RabbitMQ-native creation and editing workflows for queues, exchanges,
bindings, users, virtual hosts, and policies remain future specialized broker
configuration work.

### Application Exposure With Load Balancer And DNS Name Mapping

A load balancer exposes a route to an application endpoint. A DNS zone owns a
human-facing name boundary, and a name mapping ties a host name to the target
resource endpoint.

```yaml
resources:
  - type: application.container-app
    name: api
    container:
      image: ghcr.io/acme/api:dev
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          exposure: Public

  - type: cloudshell.loadBalancer
    name: public
    dependsOn:
      - resourceId: application.container-app:api
        typeId: application.container-app
    loadBalancer:
      entrypointDefinitions:
        - name: http
          protocol: Http
          port: 80
          exposure: Public
      routeDefinitions:
        - id: public-api
          name: Public API
          kind: Http
          entrypointName: http
          match:
            host: api.local.test
          target:
            resource:
              resourceId: application.container-app:api
            endpointName: http

  - type: cloudshell.dnsZone
    name: local
    dns:
      zone: local.test
      provider: local-hostnames

  - type: cloudshell.nameMapping
    name: api-local
    dependsOn:
      - resourceId: cloudshell.dnsZone:local
        relationship: belongsTo
        addressingMode: resourceId
        typeId: cloudshell.dnsZone
      - resourceId: application.container-app:api
        relationship: reference
        addressingMode: resourceId
        typeId: application.container-app
    nameMapping:
      hostName: api.local.test
      targetEndpointName: http
      exposure: Public
```

Use this pattern when the application needs a stable inbound name or when
ingress configuration should be visible as platform resources instead of being
hidden inside the app resource.

### Virtual Network With Endpoint Mapping Intent

A virtual network can own endpoint contracts and source-to-target mapping
intent. This is useful when the network, rather than an application, owns the
front door address or internal route.

```yaml
resources:
  - type: application.container-app
    name: api
    container:
      image: ghcr.io/acme/api:dev
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080

  - type: cloudshell.virtualNetwork
    name: app-net
    dependsOn:
      - resourceId: application.container-app:api
        typeId: application.container-app
    network:
      default: true
      endpoints:
        - name: public-http
          protocol: http
          targetPort: 80
          exposure: Public
      endpointMappings:
        - source:
            resource:
              resourceId: cloudshell.virtualNetwork:app-net
            endpointName: public-http
          target:
            resource:
              resourceId: application.container-app:api
            endpointName: http
          name: Public HTTP to API
```

Use this pattern when a network resource should own the source endpoint and
route it to a target resource endpoint through a selected networking provider.

## Application Resources

### `application.executable`

Use for a local command or executable managed by CloudShell.

Required authoring:

- `path`

Common optional attributes:

- `command.arguments`
- `command.workingDirectory`
- `storage.volume.mounts` for volume mounts

```yaml
resources:
  - type: application.executable
    name: worker
    path: ./tools/worker
    command:
      arguments: --watch
      workingDirectory: ./src/Worker
```

### `application.aspnet-core-project`

Use for a local ASP.NET Core project launched through the project runtime.

Required authoring:

- `project.path`

Common optional attributes:

- `project.arguments`
- `project.hotReload`
- `project.useLaunchSettings`
- `project.endpointRequests`
- `project.environmentVariables`
- `project.serviceDiscoveryName`
- `project.references`
- `storage.volume.mounts`

```yaml
resources:
  - type: application.aspnet-core-project
    name: api
    project:
      path: ./src/Api/Api.csproj
      hotReload: true
      useLaunchSettings: true
      serviceDiscoveryName: api
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 5080
          exposure: Local
      environmentVariables:
        ASPNETCORE_ENVIRONMENT:
          value: Development
```

### `application.javascript-app`

Use for a JavaScript or Node.js project launched through the local package
manager runtime.

Required authoring:

- `project.path`

Common optional attributes:

- `javascript.engine`
- `javascript.packageManager`
- `javascript.script`
- `javascript.arguments`
- `project.endpointRequests`
- `project.environmentVariables`
- `project.serviceDiscoveryName`
- `project.references`
- `container.*` attributes when the app is projected as a container app
- `storage.volume.mounts`

```yaml
resources:
  - type: application.javascript-app
    name: frontend
    project:
      path: ./web
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 5173
          port: 5173
          exposure: Local
    javascript:
      engine: node
      packageManager: npm
      script: dev
```

### `application.java-app`

Use for a Java or JVM project launched through the local Java runtime.

Required authoring:

- `project.path`
- one of `java.artifactPath` or `java.mainClass`

Common optional attributes:

- `java.command`
- `java.classPath`
- `java.jvmArguments`
- `java.arguments`
- `project.endpointRequests`
- `project.environmentVariables`
- `project.serviceDiscoveryName`
- `project.references`
- `container.*` attributes when the app is projected as a container app
- `storage.volume.mounts`

```yaml
resources:
  - type: application.java-app
    name: java-api
    project:
      path: ./app
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 8080
          exposure: Local
    java:
      command: java
      artifactPath: ./target/app.jar
      jvmArguments: -Xmx512m
```

### `application.go-app`

Use for a Go project launched through the local Go runtime or through a
prebuilt binary.

Required authoring:

- `project.path`

Common optional attributes:

- `go.command`
- `go.packagePath`
- `go.binaryPath`
- `go.arguments`
- `project.endpointRequests`
- `project.environmentVariables`
- `project.serviceDiscoveryName`
- `project.references`
- `container.*` attributes when the app is projected as a container app
- `storage.volume.mounts`

```yaml
resources:
  - type: application.go-app
    name: go-api
    project:
      path: ./app
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 8080
          exposure: Local
    go:
      command: go
      packagePath: .
```

### `application.container-app`

Use for the stable deployable workload abstraction for image-backed services.
This is the preferred type for managed container workloads.

Required authoring:

- `container.image`

Common optional attributes:

- `container.registry`
- `container.buildContext`
- `container.dockerfile`
- `container.replicas`
- `container.endpointRequests`
- `container.routing.sessionAffinity.*`
- `project.path` when the runtime builds from a local project
- `storage.volume.mounts`
- `dependsOn` for an explicit `cloudshell.container-host` or `docker.host`

```yaml
resources:
  - type: application.container-app
    name: api
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
    container:
      image: ghcr.io/acme/api:dev
      registry: ghcr.io
      replicas: 2
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 5080
          exposure: Public
      routing:
        sessionAffinity:
          mode: None
```

### `application.sql-server`

Use for a local-development SQL Server service, typically materialized through
a container host.

Required authoring:

- `version` defaults to `2022` and must not be empty

Common optional attributes:

- `edition`
- `databases`
- `endpointRequests`
- `storage.volume.mounts`
- `dependsOn` for an explicit `cloudshell.container-host` or `docker.host`

```yaml
resources:
  - type: application.sql-server
    name: sql
    dependsOn:
      - resourceId: cloudshell.container-host:default
        typeId: cloudshell.container-host
    version: "2022"
    edition: Developer
    endpointRequests:
      - name: tds
        protocol: tcp
        targetPort: 1433
        port: 1433
        exposure: Local
    databases:
      - name: app
        displayName: Application database
        ensureCreated: true
```

### `application.sql-database`

Use for a SQL database resource owned by a SQL Server resource.

Required authoring:

- `database.name`
- `dependsOn` reference to the owning `application.sql-server`

Common optional attributes:

- `database.ensureCreated`
- `database.source`

Do not author `database.server`; it is provider-managed.

```yaml
resources:
  - type: application.sql-database
    name: app-db
    dependsOn:
      - resourceId: application.sql-server:sql
        typeId: application.sql-server
    database:
      name: app
      ensureCreated: true
```

### `application.rabbitmq`

Use for a local-development RabbitMQ broker service, typically materialized
through a container host.

Required authoring:

- `version` defaults to `3` and must not be empty

Common optional attributes:

- `rabbitmq.managementUi`
- `endpointRequests`
- `storage.volume.mounts`
- `dependsOn` for an explicit `cloudshell.container-host` or `docker.host`

```yaml
resources:
  - type: application.rabbitmq
    name: rabbitmq
    dependsOn:
      - resourceId: cloudshell.container-host:default
        typeId: cloudshell.container-host
    version: "3"
    rabbitmq:
      managementUi: true
    endpointRequests:
      - name: amqp
        protocol: tcp
        targetPort: 5672
        port: 5672
        exposure: Local
      - name: management
        protocol: http
        targetPort: 15672
        port: 15672
        exposure: Local
```

### `cloudshell.service`

Use for an explicit logical service facade only when a separate service
resource is needed. Normal container apps and project apps do not need a
separate service resource.

Common optional attributes:

- `service.kind`
- `service.routingMode`
- `dependsOn` references to a target resource and, when relevant, a network

```yaml
resources:
  - type: cloudshell.service
    name: public-api
    dependsOn:
      - resourceId: application.container-app:api
        typeId: application.container-app
      - resourceId: cloudshell.network:public
        typeId: cloudshell.network
    service:
      kind: service
      routingMode: logical
```

## Configuration, Secrets, And Identity

### `configuration.store`

Use for a Configuration Store service. Seed entries are create-only and are
not retained as accepted graph state.

Common optional attributes:

- `kind`
- `endpoint`
- `seed.entries`

Do not author `entryCount`; it is provider-managed.

```yaml
resources:
  - type: configuration.store
    name: settings
    endpoint: http://localhost:5101
    seed:
      entries:
        - name: App--Message
          value: Hello from CloudShell
```

### `configuration.host`

Use for a host configuration source when the host wants to project
configuration entries into the resource graph. This is usually host-authored
or provider-authored.

Common optional attributes:

- `configuration.kind`
- `configuration.source`

Do not author `configuration.entries.count`; it is provider-managed.

```yaml
resources:
  - type: configuration.host
    name: host
    configuration:
      kind: host
      source: host
```

### `secrets.vault`

Use for a Secrets Vault service. Seed secrets are create-only and are not
retained as accepted graph state or emitted by default export. Seed
certificates follow the same rule: certificate payloads are materialized into
provider-owned vault state and stripped before graph state is accepted.
Runtime certificates can also be uploaded, pasted, or generated from the
Secrets Vault Certificates tab when the UI host has access to the provider
runtime manager.

Common optional attributes:

- `kind`
- `endpoint`
- `seed.secrets`
- `seed.certificates`

Do not author `secretCount` or `certificateCount`; they are provider-managed.

```yaml
resources:
  - type: secrets.vault
    name: secrets
    endpoint: http://localhost:6101
    seed:
      secrets:
        - name: App--ApiKey
          value: local-development-secret
          version: v1
      certificates:
        - name: AppTls
          value: local-development-pem-or-pfx
          version: v1
          contentType: application/x-pem-file
```

### `cloudshell.identity-provisioning`

Use for an identity provisioning integration resource.

Required authoring:

- `identity.provider`

Common optional attributes:

- `identity.providerId`
- `identity.providerKind`

```yaml
resources:
  - type: cloudshell.identity-provisioning
    name: local-identity
    identity:
      provider: built-in
      providerId: identity:built-in
      providerKind: oidc
```

## Container Infrastructure

### `cloudshell.container-host`

Use for a generic container host placement boundary. This lets container apps,
SQL Server, and other container-backed resources select a host without
depending directly on Docker-specific resource types.

Required authoring:

- `container.host.kind` defaults to `Docker` and must not be empty
- `container.host.endpoint` defaults to `unix:///var/run/docker.sock` and must
  not be empty

Common optional attributes:

- `container.registry`
- `container.host.default`

```yaml
resources:
  - type: cloudshell.container-host
    name: default
    displayName: Default container host
    container:
      host:
        kind: Docker
        endpoint: unix:///var/run/docker.sock
        default: true
      registry: docker.io
```

### `docker.host`

Use for a Docker-specific host resource when Docker-native behavior needs to
be explicit.

Required authoring:

- `docker.host.kind` defaults to `local` and must not be empty
- `docker.host.endpoint` defaults to `unix:///var/run/docker.sock` and must
  not be empty

Common optional attributes:

- `container.registry`
- `docker.host.default`

```yaml
resources:
  - type: docker.host
    name: local
    docker:
      host:
        kind: local
        endpoint: unix:///var/run/docker.sock
        default: true
    container:
      registry: docker.io
```

### `docker.container`

Use for low-level Docker container resources. Prefer
`application.container-app` for managed application workloads.

Required authoring:

- `container.image`

Common optional attributes:

- `container.registry`
- `container.replicas`
- `workload.kind`

Do not author `endpoints.count`; it is provider-managed.

```yaml
resources:
  - type: docker.container
    name: redis
    workload:
      kind: ContainerImage
    container:
      image: redis:7
      registry: docker.io
      replicas: 1
```

## Storage

### `cloudshell.storage`

Use for a storage provider/resource that can own storage placement and volume
materialization.

Required authoring:

- `storage.provider` defaults to `local` and must not be empty
- `storage.medium` defaults to `FileSystem` and must not be empty

Common optional attributes:

- `storage.location`

```yaml
resources:
  - type: cloudshell.storage
    name: local
    storage:
      provider: local
      medium: FileSystem
      location: ./.cloudshell/storage
```

### `storage.volume`

Use for the simple local volume type. Prefer `cloudshell.volume` for the newer
CloudShell-owned volume shape.

Required authoring:

- `storage.medium` defaults to `local` and must not be empty

```yaml
resources:
  - type: storage.volume
    name: scratch
    storage:
      medium: local
```

### `cloudshell.volume`

Use for a CloudShell volume that can be mounted by application, container app,
and SQL Server resources.

Required authoring:

- `storage.volume.medium` defaults to the local file-system medium and must
  not be empty
- `storage.volume.accessMode` defaults to `ReadWriteOnce` and must not be
  empty

Common optional attributes:

- `storage.volume.provider`
- `storage.volume.location`
- `storage.volume.subPath`
- `storage.volume.persistent`
- `storage.volume.maxSizeBytes`
- `storage.volume.maxSizeEnforcement`
- `dependsOn` reference to a `cloudshell.storage` resource

```yaml
resources:
  - type: cloudshell.storage
    name: local
    storage:
      provider: local
      medium: FileSystem
      location: ./.cloudshell/storage

  - type: cloudshell.volume
    name: sql-data
    dependsOn:
      - resourceId: cloudshell.storage:local
        typeId: cloudshell.storage
    storage:
      volume:
        provider: local
        medium: FileSystem
        subPath: sql-data
        accessMode: ReadWriteOnce
        persistent: true
        maxSizeBytes: 10737418240
        maxSizeEnforcement: advisory
```

Volume consumers use the `storage.volume` capability attribute:

```yaml
resources:
  - type: application.container-app
    name: api
    container:
      image: ghcr.io/acme/api:dev
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:sql-data
            targetPath: /var/lib/app
            readOnly: false
```

## Networking

### `cloudshell.network`

Use for a logical network boundary.

Common optional attributes:

- `network.kind`
- `network.hostReadiness`
- `network.mappingProviders`

```yaml
resources:
  - type: cloudshell.network
    name: public
    network:
      kind: Logical
      hostReadiness: logicalOnly
      mappingProviders: cloudshell.loadBalancer:public
```

### `cloudshell.virtualNetwork`

Use for a virtual network boundary with optional endpoint and mapping
definitions.

Common optional attributes:

- `network.kind`
- `network.default`
- `network.hostReadiness`
- `network.mappingProviders`
- `network.endpoints`
- `network.endpointNetworkMappings`
- `network.endpointMappings`

```yaml
resources:
  - type: cloudshell.virtualNetwork
    name: app-net
    network:
      kind: Virtual
      default: true
      hostReadiness: logicalOnly
      endpoints:
        - name: http
          protocol: http
          targetPort: 80
          exposure: Public
```

### `cloudshell.hostNetworking.local`

Use for a local host networking provider resource. This is usually created by
the host profile or a networking provider.

Common optional attributes:

- `infrastructure.kind`
- `network.hostReadiness`
- `host.os`
- `networking.mode`

```yaml
resources:
  - type: cloudshell.hostNetworking.local
    name: local
    infrastructure:
      kind: hostNetworking
    network:
      hostReadiness: ready
    host:
      os: cross-platform
    networking:
      mode: localProxy
```

### `cloudshell.hostNetworking.macos`

Use for the macOS host networking provider resource. This is usually created
by the host profile or a networking provider.

Common optional attributes:

- `infrastructure.kind`
- `network.hostReadiness`
- `host.os`
- `networking.mode`

```yaml
resources:
  - type: cloudshell.hostNetworking.macos
    name: macos
    infrastructure:
      kind: hostNetworking
    network:
      hostReadiness: ready
    host:
      os: macos
    networking:
      mode: localProxy
```

### `cloudshell.loadBalancer`

Use for a load balancer resource with frontend entrypoints and backend routes.
HTTPS entrypoints can include a `certificateRef` that points at a
`secrets.vault` certificate by vault resource ID, certificate name, and
optional version. The certificate payload remains in the vault.

Common optional attributes:

- `loadBalancer.provider`
- `loadBalancer.hostResourceId`
- `loadBalancer.entrypointDefinitions`
- `loadBalancer.routeDefinitions`
- provider-managed route and endpoint counts

Do not author count attributes unless you intentionally want a provider to
validate them; they are normally provider-managed.

```yaml
resources:
  - type: cloudshell.loadBalancer
    name: public
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
      - resourceId: application.container-app:api
        typeId: application.container-app
    loadBalancer:
      provider: logical
      hostResourceId: docker.host:local
      entrypointDefinitions:
        - name: https
          protocol: Https
          port: 443
          exposure: Public
          certificateRef:
            vaultResourceId: secrets.vault:secrets
            name: AppTls
      routeDefinitions:
        - id: public-api
          name: Public API
          kind: Http
          entrypointName: https
          match:
            host: api.local.test
          target:
            resource:
              resourceId: application.container-app:api
            endpointName: http
```

### `cloudshell.dnsZone`

Use for a DNS or local name zone.

Required authoring:

- `dns.zone`

Common optional attributes:

- `dns.provider`

```yaml
resources:
  - type: cloudshell.dnsZone
    name: local
    dns:
      zone: local.test
      provider: local-hostnames
```

### `cloudshell.nameMapping`

Use for a host/name mapping from a DNS zone to a target resource endpoint.

Required authoring:

- `nameMapping.hostName`
- `dependsOn` `belongsTo` reference to a `cloudshell.dnsZone`
- `dependsOn` `reference` reference to the target resource

Common optional attributes:

- `nameMapping.targetEndpointName`
- `nameMapping.exposure`

Do not author `nameMapping.materializationStatus`; it is provider-managed.

```yaml
resources:
  - type: cloudshell.nameMapping
    name: api-local
    dependsOn:
      - resourceId: cloudshell.dnsZone:local
        relationship: belongsTo
        addressingMode: resourceId
        typeId: cloudshell.dnsZone
      - resourceId: application.container-app:api
        relationship: reference
        addressingMode: resourceId
        typeId: application.container-app
    nameMapping:
      hostName: api.local.test
      targetEndpointName: http
      exposure: Public
```

## Complete Local Topology Example

This example combines common user-authored resources: a Docker host,
configuration store, secrets vault, container app, SQL Server and database,
volume, load balancer, DNS zone, and name mapping.

```yaml
resources:
  - type: docker.host
    name: local
    docker:
      host:
        kind: local
        endpoint: unix:///var/run/docker.sock
        default: true

  - type: cloudshell.storage
    name: local
    storage:
      provider: local
      medium: FileSystem
      location: ./.cloudshell/storage

  - type: cloudshell.volume
    name: sql-data
    dependsOn:
      - resourceId: cloudshell.storage:local
        typeId: cloudshell.storage
    storage:
      volume:
        medium: FileSystem
        subPath: sql-data
        accessMode: ReadWriteOnce
        persistent: true

  - type: configuration.store
    name: settings
    endpoint: http://localhost:5101
    seed:
      entries:
        - name: App--Message
          value: Hello from CloudShell

  - type: secrets.vault
    name: secrets
    endpoint: http://localhost:6101
    seed:
      secrets:
        - name: App--ApiKey
          value: local-development-secret
          version: v1

  - type: application.sql-server
    name: sql
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
      - resourceId: cloudshell.volume:sql-data
        typeId: cloudshell.volume
    version: "2022"
    endpointRequests:
      - name: tds
        protocol: tcp
        targetPort: 1433
        port: 1433
        exposure: Local
    storage:
      volume:
        mounts:
          - volume: cloudshell.volume:sql-data
            targetPath: /var/opt/mssql
            readOnly: false

  - type: application.sql-database
    name: app-db
    dependsOn:
      - resourceId: application.sql-server:sql
        typeId: application.sql-server
    database:
      name: app
      ensureCreated: true

  - type: application.container-app
    name: api
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
      - resourceId: configuration.store:settings
        typeId: configuration.store
      - resourceId: secrets.vault:secrets
        typeId: secrets.vault
      - resourceId: application.sql-database:app-db
        typeId: application.sql-database
    container:
      image: ghcr.io/acme/api:dev
      replicas: 2
      endpointRequests:
        - name: http
          protocol: http
          targetPort: 8080
          port: 5080
          exposure: Public

  - type: cloudshell.loadBalancer
    name: public
    dependsOn:
      - resourceId: docker.host:local
        typeId: docker.host
      - resourceId: application.container-app:api
        typeId: application.container-app
    loadBalancer:
      provider: logical
      hostResourceId: docker.host:local
      entrypointDefinitions:
        - name: http
          protocol: Http
          port: 80
          exposure: Public
      routeDefinitions:
        - id: public-api
          name: Public API
          kind: Http
          entrypointName: http
          match:
            host: api.local.test
          target:
            resource:
              resourceId: application.container-app:api
            endpointName: http

  - type: cloudshell.dnsZone
    name: local
    dns:
      zone: local.test
      provider: local-hostnames

  - type: cloudshell.nameMapping
    name: api-local
    dependsOn:
      - resourceId: cloudshell.dnsZone:local
        relationship: belongsTo
        addressingMode: resourceId
        typeId: cloudshell.dnsZone
      - resourceId: application.container-app:api
        relationship: reference
        addressingMode: resourceId
        typeId: application.container-app
    nameMapping:
      hostName: api.local.test
      targetEndpointName: http
      exposure: Public
```
