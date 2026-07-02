import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { cloudshell } from "@cloudshell/local-development";

const sampleHostRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(sampleHostRoot, "..", "..");
const appRoot = resolve(repoRoot, "samples", "JavaScriptApp", "App");
const cliProject = resolve(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj");
const hostProject = resolve(repoRoot, "samples", "JavaScriptApp", "Host", "CloudShell.JavaScriptAppHost.csproj");
const settingsServiceEndpoint = "http://localhost:5101";
const settingsResourceId = "configuration.store:typescript-app-settings";
const settingsEntriesEndpoint =
  `${settingsServiceEndpoint}/api/configuration/stores/${encodeURIComponent(settingsResourceId)}/entries`;

const app = cloudshell("typescript-hosting-poc", {
  metadata: {
    "cloudshell.source": "typescript",
    "cloudshell.sample": "TypeScriptAppHost"
  }
});

const settings = app
  .addConfigurationStore("typescript-app-settings")
  .withDisplayName("TypeScript App Settings")
  .withEndpoint(settingsServiceEndpoint);

app
  .addJavaScriptApp("typescript-frontend", appRoot)
  .withDisplayName("TypeScript-declared Frontend")
  .withPackageManager("npm")
  .withScript("dev")
  .withServiceDiscovery()
  .withReference(settings)
  .withEnvironmentVariable(
    "CLOUDSHELL_SETTINGS_ENDPOINT",
    settingsEntriesEndpoint)
  .withEnvironmentVariable("Sample__Message", {
    configurationEntryRef: settings.entry("Sample--Message")
  })
  .withHttpEndpoint({
    host: "localhost",
    port: 5173,
    targetPort: 5173
  })
  .withHttpHealthCheck("/healthz", { endpointName: "http" })
  .withHttpLivenessCheck("/alive", { endpointName: "http" });

if (process.argv.includes("--apply")) {
  const result = await app.apply({
    cliProject,
    hostProject,
    start: process.argv.includes("--start"),
    noBuild: process.argv.includes("--no-build"),
    controlPlaneUrl: process.env.CLOUDSHELL_CONTROL_PLANE_URL,
    stateDir: resolve(sampleHostRoot, ".cloudshell")
  });

  process.exitCode = result.exitCode;
} else {
  process.stdout.write(app.toJson());
}
