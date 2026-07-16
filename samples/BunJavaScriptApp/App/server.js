const port = Number.parseInt(Bun.env.PORT ?? "5174", 10);
const settingsEndpoint = Bun.env.CLOUDSHELL_SETTINGS_ENDPOINT ?? null;
const serviceName = Bun.env.OTEL_SERVICE_NAME ?? "bun-javascript-frontend";

Bun.serve({
  hostname: "127.0.0.1",
  port,
  fetch(request) {
    const url = new URL(request.url);
    if (url.pathname === "/healthz" || url.pathname === "/alive") {
      return Response.json({ status: "ok", serviceName });
    }

    return Response.json({
      message: "CloudShell Bun JavaScript app sample",
      configuredMessage: Bun.env.Sample__Message ?? "No CloudShell configuration value was loaded.",
      serviceName,
      settingsEndpoint,
      runtime: "bun",
    });
  },
});

console.log(`Bun JavaScript app sample listening on http://127.0.0.1:${port}`);
if (settingsEndpoint) {
  console.log(`Configured settings endpoint: ${settingsEndpoint}`);
}
