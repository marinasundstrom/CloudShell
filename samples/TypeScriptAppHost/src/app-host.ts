import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { cloudshell } from "@cloudshell/local-development";

const sampleHostRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(sampleHostRoot, "..", "..");
const appRoot = resolve(repoRoot, "samples", "JavaScriptApp", "App");
const cliProject = resolve(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj");
const hostProject = process.env.CLOUDSHELL_HOST_PROJECT ??
  resolve(repoRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj");
const stateDir = process.env.CLOUDSHELL_STATE_DIR ??
  resolve(sampleHostRoot, ".cloudshell");
const dataDir = readArgumentValue("--data-dir") ??
  process.env.CLOUDSHELL_DATA_DIR ??
  stateDir;
const settingsServiceEndpoint = "http://localhost:5101";
const settingsResourceId = "configuration.store:typescript-app-settings";
const settingsEntriesEndpoint =
  `${settingsServiceEndpoint}/api/configuration/stores/${encodeURIComponent(settingsResourceId)}/entries`;
const secretsServiceEndpoint = "http://localhost:6101";
const secretsResourceId = "secrets.vault:typescript-app-secrets";

const app = cloudshell("typescript-hosting-poc", {
  metadata: {
    "cloudshell.source": "typescript",
    "cloudshell.sample": "TypeScriptAppHost"
  }
});

const settings = app
  .addConfigurationStore("typescript-app-settings")
  .withDisplayName("TypeScript App Settings")
  .withEndpoint(settingsServiceEndpoint)
  .withSetting("Sample--Message", "Hello from TypeScript launcher seed");

const secrets = app
  .addSecretsVault("typescript-app-secrets")
  .withDisplayName("TypeScript App Secrets")
  .withEndpoint(secretsServiceEndpoint)
  .withSecret("Sample--ApiKey", "typescript-launcher-secret", "v1");

app
  .addJavaScriptApp("typescript-frontend", appRoot)
  .withDisplayName("TypeScript-declared Frontend")
  .withPackageManager("npm")
  .withScript("dev")
  .withServiceDiscovery()
  .withReference(settings)
  .withReference(secrets)
  .dependsOn(settings)
  .dependsOn(secrets)
  .withEnvironmentVariable(
    "CLOUDSHELL_SETTINGS_ENDPOINT",
    settingsEntriesEndpoint)
  .withEnvironmentVariable("Sample__Message", {
    configurationEntryRef: settings.entry("Sample--Message")
  })
  .withEnvironmentVariable("Sample__ApiKey", {
    secretRef: secrets.secret("Sample--ApiKey")
  })
  .withHttpEndpoint({
    host: "localhost",
    port: 5173,
    targetPort: 5173
  })
  .withHttpHealthCheck("/healthz", { endpointName: "http" })
  .withHttpLivenessCheck("/alive", { endpointName: "http" });

if (process.argv.includes("--apply") || process.argv.includes("--start") || process.argv.includes("--run")) {
  const options = {
    cliProject,
    hostProject,
    noBuild: process.argv.includes("--no-build"),
    controlPlaneUrl: process.env.CLOUDSHELL_CONTROL_PLANE_URL,
    url: process.env.CLOUDSHELL_CONTROL_PLANE_URL,
    stateDir,
    dataDir
  };
  const result = process.argv.includes("--run")
    ? await app.run(options)
    : process.argv.includes("--start")
      ? await app.start(options)
      : await app.apply(options);

  process.exitCode = result.exitCode;
} else {
  process.stdout.write(app.toJson());
}

function readArgumentValue(name: string): string | undefined {
  const index = process.argv.findIndex(argument =>
    argument.toLowerCase() === name.toLowerCase());
  return index >= 0 ? process.argv[index + 1] : undefined;
}
