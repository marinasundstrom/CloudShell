import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { cloudshell } from "@cloudshell/local-development";

const sampleRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const repoRoot = resolve(sampleRoot, "..", "..");
const appRoot = resolve(sampleRoot, "App");
const cliProject = process.env.CLOUDSHELL_CLI_PROJECT ??
  resolve(repoRoot, "CloudShell.Cli", "CloudShell.Cli.csproj");
const hostProject = process.env.CLOUDSHELL_HOST_PROJECT ??
  resolve(repoRoot, "CloudShell.LocalDevelopmentHost", "CloudShell.LocalDevelopmentHost.csproj");
const stateDir = process.env.CLOUDSHELL_STATE_DIR ??
  resolve(sampleRoot, ".cloudshell");
const dataDir = readArgumentValue("--data-dir") ??
  process.env.CLOUDSHELL_DATA_DIR ??
  resolve(stateDir, "data");
const templatePath = process.env.CLOUDSHELL_TEMPLATE_PATH ??
  resolve(stateDir, "resources.json");
const controlPlaneUrl = process.env.CLOUDSHELL_CONTROL_PLANE_URL ??
  "http://127.0.0.1:5114";
const settingsEndpoint = process.env.CLOUDSHELL_SETTINGS_ENDPOINT ??
  "http://localhost:5115";
const secretsEndpoint = process.env.CLOUDSHELL_SECRETS_ENDPOINT ??
  "http://localhost:6115";
const appEndpoint = new URL(process.env.CLOUDSHELL_APP_ENDPOINT ??
  "http://localhost:5192");

const app = cloudshell("typescript-container-app", {
  environmentId: "local",
  metadata: {
    "cloudshell.source": "typescript",
    "cloudshell.sample": "TypeScriptContainerApp"
  }
});

const network = app.getDefaultNetwork();

const settings = app
  .addConfigurationStore("typescript-container-settings")
  .withDisplayName("TypeScript Container Settings")
  .withEndpoint(settingsEndpoint)
  .withSeed(seed => seed
    .setting("Sample--Message", "Hello from TypeScript container app configuration")
    .setting("Sample--Mode", "container"));

const secrets = app
  .addSecretsVault("typescript-container-secrets")
  .withDisplayName("TypeScript Container Secrets")
  .withEndpoint(secretsEndpoint)
  .withSeed(seed => seed.secret("Sample--ApiKey", "typescript-container-secret", "v1"));

const api = app
  .addJavaScriptApp("typescript-container-api", appRoot)
  .withDisplayName("TypeScript Container API")
  .withPackageManager("npm")
  .withScript("start")
  .withServiceDiscovery()
  .withReference(settings)
  .withReference(secrets)
  .dependsOn(settings)
  .dependsOn(secrets)
  .withEnvironmentVariable("PORT", "8080")
  .withEnvironmentVariable("OTEL_SERVICE_NAME", "typescript-container-api")
  .withHttpEndpoint({
    host: appEndpoint.hostname,
    port: appEndpoint.port ? Number(appEndpoint.port) : undefined,
    targetPort: 8080,
    network
  })
  .withHttpHealthCheck("/healthz", { endpointName: "http" })
  .withHttpLivenessCheck("/alive", { endpointName: "http" })
  .asContainerApp({
    tag: "dev",
    buildContext: repoRoot,
    dockerfile: "samples/TypeScriptContainerApp/App/Dockerfile",
    replicas: 2
  })
  .requireIdentity({ name: "typescript-container-api" })
  .provisionIdentityOnStartup();

settings.allowResourceIdentity(
  api,
  "CloudShell.Configuration/stores/settings/read/action",
  { identityName: "typescript-container-api" });
secrets.allowResourceIdentity(
  api,
  "CloudShell.Secrets/vaults/secrets/read/action",
  { identityName: "typescript-container-api" });

if (process.argv.includes("--apply") || process.argv.includes("--start") || process.argv.includes("--run")) {
  const options = {
    cliProject,
    hostProject,
    templatePath,
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

function readArgumentValue(name: string): string | undefined {
  const index = process.argv.findIndex(argument =>
    argument.toLowerCase() === name.toLowerCase());
  return index >= 0 ? process.argv[index + 1] : undefined;
}
