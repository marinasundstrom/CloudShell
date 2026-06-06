# Configuration Services

CloudShell includes a configuration provider that contributes `configuration.store`
resources. Each resource is a separate local configuration service with its own
entries, endpoint, access token, and resource group assignment.

Use separate configuration services when different projects or resource groups
need different settings or secrets. For example, a frontend/API group can depend
on one configuration service while a worker group depends on another.

## Resource Model

A configuration service is added from `/resources/add` by choosing
**Configuration service**. It can be assigned to any resource group, or left
ungrouped.

Each service stores key-value entries:

- `Name`: the setting name.
- `Value`: the stored value.
- `Secret`: marks the entry as sensitive in UI and template export behavior.

Provider-owned state is persisted in:

```text
CloudShell.Host/Data/configuration-stores.json
```

The core CloudShell database still stores only platform metadata such as the
resource registration and group assignment.

## Application Access

Executable applications receive configuration service connection details through
resource dependencies. If an application depends on a configuration service,
CloudShell injects environment variables when the process starts:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_STORE_ID
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_TOKEN
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_STORE_ID
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_ENDPOINT
CLOUDSHELL_CONFIGURATION_<RESOURCE_ID>_TOKEN
```

`<SERVICE_NAME>` and `<RESOURCE_ID>` are uppercased and normalized for
environment variable names. The resource-ID variables avoid collisions when two
groups use similarly named services.

Applications fetch settings from:

```text
GET /api/configuration/entries?resourceId=<resource-id>
GET /api/configuration/entries/{name}?resourceId=<resource-id>
```

Pass the token with either:

```text
Authorization: Bearer <token>
X-CloudShell-Configuration-Token: <token>
```

The configuration API is anonymous at the ASP.NET authentication layer because it
uses the resource token as its own authentication boundary. Missing tokens return
`401`; invalid tokens and missing services return `404`.

## Sample

The host registers an initial `Example Configuration` service and the
`Example Web API` application depends on it by default. When the sample app is
started from Resource Manager, open:

```text
http://localhost:5127/configuration
```

The sample calls the CloudShell configuration endpoint with the injected token
and masks secret values in its response.

## Templates

Configuration services support resource group templates. Export includes
non-secret entry values. Secret entries are exported as placeholders with an
empty value so templates do not leak secrets by default. Import creates a new
configuration service and generates a fresh access token.
