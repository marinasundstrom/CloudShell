import http from "node:http";

const port = Number.parseInt(process.env.PORT ?? "5173", 10);
const settingsEndpoint = process.env.CLOUDSHELL_SETTINGS_ENDPOINT ?? null;
const serviceName = process.env.OTEL_SERVICE_NAME ?? "javascript-frontend";

const server = http.createServer((request, response) => {
  if (request.url === "/healthz" || request.url === "/alive") {
    writeJson(response, 200, { status: "ok", serviceName });
    return;
  }

  writeJson(response, 200, {
    message: "CloudShell JavaScript app sample",
    serviceName,
    settingsEndpoint,
  });
});

server.listen(port, "127.0.0.1", () => {
  console.log(`JavaScript app sample listening on http://127.0.0.1:${port}`);
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
