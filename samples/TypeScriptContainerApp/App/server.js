import http from "node:http";
import {
  ConfigurationStoreClient,
  SecretsVaultClient
} from "@cloudshell/configuration-client";

const port = Number.parseInt(process.env.PORT ?? "8080", 10);
const host = process.env.HOST ?? "0.0.0.0";
const serviceName = process.env.OTEL_SERVICE_NAME ?? "typescript-container-api";

const server = http.createServer(async (request, response) => {
  if (request.url === "/healthz" || request.url === "/alive") {
    writeJson(response, 200, { status: "ok", serviceName });
    return;
  }

  if (request.url === "/configuration") {
    await writeConfiguration(response);
    return;
  }

  writeJson(response, 200, {
    message: "CloudShell TypeScript launcher container app sample",
    serviceName,
    configuration: "/configuration"
  });
});

server.listen(port, host, () => {
  console.log(`TypeScript launcher container app sample listening on http://${host}:${port}`);
});

async function writeConfiguration(response) {
  try {
    const settings = ConfigurationStoreClient.fromEnvironment({
      serviceName: process.env.CLOUDSHELL_CONFIGURATION_SERVICE_NAME
    });
    const secrets = SecretsVaultClient.fromEnvironment({
      vaultName: process.env.CLOUDSHELL_SECRETS_VAULT_NAME
    });
    const message = await settings.getSetting("Sample--Message");
    const mode = await settings.getSetting("Sample--Mode");
    const apiKey = await secrets.getSecret("Sample--ApiKey");

    writeJson(response, 200, {
      source: "cloudshell-sdk",
      message: message?.value ?? "",
      mode: mode?.value ?? "",
      hasApiKey: Boolean(apiKey?.value),
      secretName: apiKey?.name ?? "",
      resourceId: process.env.CLOUDSHELL_RESOURCE_ID ?? "",
      resourceName: process.env.CLOUDSHELL_RESOURCE_NAME ?? ""
    });
  } catch (error) {
    writeJson(response, 503, {
      source: "cloudshell-sdk",
      sdkError: error instanceof Error ? error.message : String(error)
    });
  }
}

function writeJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8"
  });
  response.end(JSON.stringify(body));
}
