import http from "node:http";

const port = Number.parseInt(process.env.PORT ?? "8080", 10);
const host = process.env.HOST ?? "0.0.0.0";
const settingsEndpoint = process.env.CLOUDSHELL_SETTINGS_ENDPOINT ?? null;
const serviceName = process.env.OTEL_SERVICE_NAME ?? "javascript-container-frontend";

const server = http.createServer((request, response) => {
  if (request.url === "/healthz" || request.url === "/alive") {
    writeJson(response, 200, { status: "ok", serviceName });
    return;
  }

  writeJson(response, 200, {
    message: "CloudShell JavaScript container app sample",
    serviceName,
    settingsEndpoint,
  });
});

server.listen(port, host, () => {
  console.log(`JavaScript container app sample listening on http://${host}:${port}`);
  if (settingsEndpoint) {
    console.log(`Configured settings endpoint: ${settingsEndpoint}`);
  }
});

function writeJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8",
  });
  response.end(JSON.stringify(body));
}
