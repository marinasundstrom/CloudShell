# CloudShell TypeScript Hosting POC

This package is an experimental proof of concept for declaring CloudShell
resource templates from TypeScript. It mirrors a small part of the C#
programmatic resource builder style and emits the same ResourceTemplate JSON
shape consumed by the CloudShell CLI and Control Plane.

The package is intentionally not a stable SDK. The current goal is to prove the
hosting integration pattern: TypeScript code authors a graph, writes a
ResourceTemplate, and can hand that template to `cloudshell template apply`.

```ts
import { cloudshell } from "@cloudshell/local-development";

const app = cloudshell("orders");

const settings = app
  .addConfigurationStore("orders-settings")
  .withEndpoint("http://localhost:5101/api/configuration/stores/orders-settings/entries");

app
  .addJavaScriptApp("orders-web", "src/web")
  .withHttpEndpoint({ port: 5173, targetPort: 5173, host: "localhost" })
  .withReference(settings)
  .withEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT", {
    value: "http://localhost:5101/api/configuration/stores/orders-settings/entries"
  });

console.log(app.toJson());
```

For local integration with the current CLI:

```ts
await app.apply({
  cliProject: "../../CloudShell.Cli/CloudShell.Cli.csproj",
  controlPlaneUrl: "http://127.0.0.1:5097"
});
```
