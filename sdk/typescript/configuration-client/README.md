# CloudShell Configuration Client For TypeScript

This package is an experimental TypeScript client for the CloudShell
Configuration Store service. It is separate from the TypeScript hosting POC:
the hosting package declares resources, while this package is used by a running
application to read configuration settings from a Configuration Store endpoint.

```ts
import {
  ConfigurationStoreClient,
  CloudShellProfileCredential
} from "@cloudshell/configuration-client";

const client = new ConfigurationStoreClient(
  "http://localhost:5138/api/configuration/stores/configuration.store%3Aapp/settings",
  {
    credential: new CloudShellProfileCredential()
  });

const settings = await client.getSettings();
const mode = await client.getSetting("Sample:Mode");
```

The client can also discover the first injected endpoint from environment
variables shaped like:

```text
CLOUDSHELL_CONFIGURATION_<SERVICE_NAME>_ENDPOINT
```

```ts
const client = ConfigurationStoreClient.fromEnvironment({
  credential: new CloudShellProfileCredential()
});
```

If no credential is supplied, `DefaultCloudShellCredential` checks environment
tokens first, then the active CloudShell profile. The environment credential
checks these variables in order:

```text
CLOUDSHELL_CONFIGURATION_TOKEN
CLOUDSHELL_CONTROL_PLANE_TOKEN
CLOUDSHELL_TOKEN
```

The profile credential reads `~/.cloudshell/config.json` by default.
`CLOUDSHELL_CONFIG_DIR` overrides the directory, and `CLOUDSHELL_PROFILE`
selects a profile. The first supported credential kind is `staticBearer`, using
either `accessToken` for short-lived tests or `accessTokenPath` for a local
token file relative to the profile directory.

Each service call sends the acquired token as `Authorization: Bearer ...`.
`getSettings()` reads the full settings collection, `getSetting(name)` reads a
single setting, and `toObject()` maps portable `--` setting names to `:` keys for
configuration-style lookup.

Run the package tests:

```bash
npm install
npm test
```
