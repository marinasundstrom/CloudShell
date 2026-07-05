import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { cloudshell } from "../../../../Launchers/TypeScript/cloudshell/src/index.ts";

const appHostRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const sampleRoot = resolve(appHostRoot, "..");
const repoRoot = resolve(sampleRoot, "..", "..");
const frontendRoot = resolve(sampleRoot, "App");
const backendRoot = resolve(sampleRoot, "Backend");
const cliProject = pathFromEnv(
  "CLOUDSHELL_CLI_PROJECT",
  resolve(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj"));
const hostProject = pathFromEnv(
  "CLOUDSHELL_HOST_PROJECT",
  resolve(repoRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj"));
const stateDir = pathFromEnv("CLOUDSHELL_STATE_DIR", resolve(sampleRoot, ".cloudshell"));
const dataDir = readArgumentValue("--data-dir") ??
  pathFromEnv("CLOUDSHELL_DATA_DIR", stateDir);
const controlPlaneUrl = pathFromEnv(
  "CLOUDSHELL_CONTROL_PLANE_URL",
  "http://127.0.0.1:5110");
const frontendEndpoint = endpointFromEnv(
  "ReactTypeScriptApp__FrontendEndpoint",
  "http://localhost:5175");
const backendEndpoint = endpointFromEnv(
  "ReactTypeScriptApp__BackendEndpoint",
  "http://localhost:5185");
const settingsServiceEndpoint = pathFromEnv(
  "ReactTypeScriptApp__ConfigurationServiceEndpoint",
  "http://localhost:5111");
const edgePort = numberFromEnv("ReactTypeScriptApp__EdgeHttpPort", 8088);
const settingsResourceId = "configuration.store:react-typescript-settings";
const settingsApiEndpoint =
  `${settingsServiceEndpoint.replace(/\/$/, "")}/api/configuration/stores/${encodeURIComponent(settingsResourceId)}/settings`;

const app = cloudshell("react-typescript-app", {
  metadata: {
    "cloudshell.source": "typescript",
    "cloudshell.sample": "ReactTypeScriptApp"
  }
});

const settings = app
  .addConfigurationStore("react-typescript-settings")
  .withDisplayName("React TypeScript Settings")
  .withEndpoint(settingsServiceEndpoint)
  .withSeed(seed => seed
    .setting("Sample--Message", "Hello from the CloudShell Configuration Store")
    .setting("Sample--Mode", "react-with-backend"));

const backend = app
  .addJavaScriptApp("react-api", backendRoot)
  .withDisplayName("React Sample Backend")
  .withPackageManager("npm")
  .withScript("dev")
  .withServiceDiscovery()
  .withReference(settings)
  .dependsOn(settings)
  .withEnvironmentVariable("PORT", backendEndpoint.port.toString())
  .withEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT", settingsApiEndpoint)
  .withEnvironmentVariable("OTEL_SERVICE_NAME", "react-sample-api")
  .withEnvironmentVariable("Sample__Message", {
    configurationSettingRef: settings.setting("Sample--Message")
  })
  .withEnvironmentVariable("Sample__Mode", {
    configurationSettingRef: settings.setting("Sample--Mode")
  })
  .withHttpEndpoint({
    host: backendEndpoint.hostname,
    port: numberFromEndpoint(backendEndpoint),
    targetPort: numberFromEndpoint(backendEndpoint)
  })
  .withHttpHealthCheck("/healthz", { endpointName: "http" })
  .withHttpLivenessCheck("/alive", { endpointName: "http" });

const frontend = app
  .addJavaScriptApp("react-frontend", frontendRoot)
  .withDisplayName("React TypeScript Frontend")
  .withPackageManager("npm")
  .withScript("dev")
  .withServiceDiscovery()
  .withReference(backend)
  .dependsOn(backend)
  .withEnvironmentVariable("PORT", frontendEndpoint.port.toString())
  .withEnvironmentVariable("VITE_BACKEND_URL", backendEndpoint.toString().replace(/\/$/, ""))
  .withEnvironmentVariable("VITE_SAMPLE_TITLE", "CloudShell React TypeScript")
  .withHttpEndpoint({
    host: frontendEndpoint.hostname,
    port: numberFromEndpoint(frontendEndpoint),
    targetPort: numberFromEndpoint(frontendEndpoint)
  })
  .withHttpHealthCheck("/", { endpointName: "http" });

app
  .addLoadBalancer("react-edge")
  .withDisplayName("React Sample Edge")
  .withProvider("traefik")
  .exposeHttp({ port: edgePort })
  .mapPath("react.localhost", "/api", backend, { endpoint: "http" })
  .mapHost("react.localhost", frontend, { endpoint: "http" });

if (process.argv.includes("--apply") || process.argv.includes("--start") || process.argv.includes("--run")) {
  const options = {
    cliProject,
    hostProject,
    noBuild: process.argv.includes("--no-build"),
    controlPlaneUrl,
    url: controlPlaneUrl,
    stateDir,
    dataDir,
    cwd: repoRoot
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

function pathFromEnv(name: string, fallback: string): string {
  return process.env[name] && process.env[name]!.trim().length > 0
    ? process.env[name]!
    : fallback;
}

function endpointFromEnv(name: string, fallback: string): URL {
  return new URL(pathFromEnv(name, fallback));
}

function numberFromEnv(name: string, fallback: number): number {
  const value = process.env[name];
  if (!value || value.trim().length === 0) {
    return fallback;
  }

  return Number.parseInt(value, 10);
}

function numberFromEndpoint(endpoint: URL): number {
  return Number.parseInt(endpoint.port, 10);
}

function readArgumentValue(name: string): string | undefined {
  const index = process.argv.findIndex(argument =>
    argument.toLowerCase() === name.toLowerCase());
  return index >= 0 ? process.argv[index + 1] : undefined;
}
