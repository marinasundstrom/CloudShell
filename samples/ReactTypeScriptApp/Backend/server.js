import http from "node:http";

const port = Number.parseInt(process.env.PORT ?? "5185", 10);
const settingsEndpoint = process.env.CLOUDSHELL_SETTINGS_ENDPOINT ?? null;
const serviceName = process.env.OTEL_SERVICE_NAME ?? "react-sample-api";

const server = http.createServer((request, response) => {
  if (request.url === "/healthz" || request.url === "/alive") {
    writeJson(response, 200, { status: "ok", serviceName });
    return;
  }

  if (request.url === "/api/status") {
    writeCorsHeaders(response);
    writeJson(response, 200, {
      service: serviceName,
      message: process.env.Sample__Message ?? "No CloudShell setting was loaded.",
      mode: process.env.Sample__Mode ?? "unknown",
      settingsEndpoint,
      timestamp: new Date().toISOString()
    });
    return;
  }

  if (request.method === "OPTIONS") {
    writeCorsHeaders(response);
    response.writeHead(204);
    response.end();
    return;
  }

  writeJson(response, 404, { error: "not found" });
});

server.listen(port, "127.0.0.1", () => {
  console.log(`React sample backend listening on http://127.0.0.1:${port}`);
});

function writeCorsHeaders(response) {
  response.setHeader("access-control-allow-origin", "*");
  response.setHeader("access-control-allow-methods", "GET, OPTIONS");
  response.setHeader("access-control-allow-headers", "content-type");
}

function writeJson(response, statusCode, body) {
  response.writeHead(statusCode, {
    "content-type": "application/json; charset=utf-8"
  });
  response.end(JSON.stringify(body));
}
