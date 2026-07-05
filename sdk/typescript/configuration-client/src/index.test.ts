import test from "node:test";
import assert from "node:assert/strict";
import {
  ConfigurationStoreClient,
  StaticTokenCredential
} from "./index.js";

test("sends bearer token and reads settings", async () => {
  const requests: Request[] = [];
  const client = new ConfigurationStoreClient(
    "http://localhost/api/configuration/stores/configuration%3Aapp/settings",
    {
      credential: new StaticTokenCredential("configuration-token"),
      fetch: async (request, init) => {
        requests.push(request instanceof Request ? request : new Request(request, init));
        return Response.json([
          { name: "Sample:Message", value: "Hello" }
        ]);
      }
    });

  const settings = await client.getSettings();

  assert.equal(settings.length, 1);
  assert.equal(settings[0]!.name, "Sample:Message");
  assert.equal(settings[0]!.value, "Hello");
  assert.equal(requests[0]!.headers.get("authorization"), "Bearer configuration-token");
});

test("builds setting endpoint and preserves query string", async () => {
  const requestedUrls: string[] = [];
  const client = new ConfigurationStoreClient(
    "http://localhost/api/configuration/settings?resourceId=configuration%3Aapp",
    {
      credential: "configuration-token",
      fetch: async request => {
        requestedUrls.push(request.toString());
        return Response.json({
          name: "Sample:Mode",
          value: "Development"
        });
      }
    });

  const setting = await client.getSetting("Sample:Mode");

  assert.equal(setting?.value, "Development");
  assert.equal(
    requestedUrls[0],
    "http://localhost/api/configuration/settings/Sample%3AMode?resourceId=configuration%3Aapp");
});

test("returns undefined for missing setting", async () => {
  const client = new ConfigurationStoreClient(
    "http://localhost/api/configuration/stores/configuration%3Aapp/settings",
    {
      credential: "configuration-token",
      fetch: async () => new Response("", { status: 404 })
    });

  assert.equal(await client.getSetting("Missing"), undefined);
});

test("discovers endpoint from environment by service name", () => {
  const client = ConfigurationStoreClient.fromEnvironment({
    serviceName: "app-settings",
    credential: "configuration-token",
    environment: {
      CLOUDSHELL_CONFIGURATION_OTHER_ENDPOINT: "http://localhost/other",
      CLOUDSHELL_CONFIGURATION_APP_SETTINGS_ENDPOINT: "http://localhost/app-settings"
    }
  });

  assert.equal(client.settingsEndpoint.toString(), "http://localhost/app-settings");
});

test("maps portable hierarchy separator to configuration keys", async () => {
  const client = new ConfigurationStoreClient(
    "http://localhost/api/configuration/stores/configuration%3Aapp/settings",
    {
      credential: "configuration-token",
      fetch: async () => Response.json([
        { name: "Orders--Api--BaseUrl", value: "http://localhost:5080" }
      ])
    });

  assert.deepEqual(await client.toObject(), {
    "Orders:Api:BaseUrl": "http://localhost:5080"
  });
});
