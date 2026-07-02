# CloudShell Configuration Client For TypeScript

This package is an experimental TypeScript client for the CloudShell
Configuration Store service. It is separate from the TypeScript hosting POC:
the hosting package declares resources, while this package is used by a running
application to read configuration entries from a Configuration Store endpoint.

```ts
import {
  ConfigurationStoreClient,
  StaticTokenCredential
} from "@cloudshell/configuration-client";

const client = new ConfigurationStoreClient(
  "http://localhost:5138/api/configuration/stores/configuration.store%3Aapp/entries",
  {
    credential: new StaticTokenCredential(process.env.CLOUDSHELL_TOKEN ?? "")
  });

const entries = await client.getEntries();
const mode = await client.getEntry("Sample:Mode");
```

The client can also discover the first injected endpoint from environment
variables shaped like:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
```

```ts
const client = ConfigurationStoreClient.fromEnvironment({
  credential: new StaticTokenCredential(process.env.CLOUDSHELL_TOKEN ?? "")
});
```

If no credential is supplied, `EnvironmentTokenCredential` checks these
environment variables in order:

```text
CLOUDSHELL_CONFIGURATION_TOKEN
CLOUDSHELL_CONTROL_PLANE_TOKEN
CLOUDSHELL_TOKEN
```

Each service call sends the acquired token as `Authorization: Bearer ...`.
`getEntries()` reads the full entries collection, `getEntry(name)` reads a
single entry, and `toObject()` maps portable `--` setting names to `:` keys for
configuration-style lookup.

Run the package tests:

```bash
npm install
npm test
```
