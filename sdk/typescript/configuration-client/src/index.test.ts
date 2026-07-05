import test from "node:test";
import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { tmpdir } from "node:os";
import {
  CloudShellIdentityCredential,
  CloudShellProfileCredential,
  ConfigurationStoreClient,
  DefaultCloudShellCredential,
  EnvironmentTokenCredential,
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

test("profile credential reads active profile static bearer token", async () => {
  const directory = await createTemporaryDirectory();
  try {
    await writeFile(
      join(directory, CloudShellProfileCredential.defaultConfigFileName),
      JSON.stringify({
        activeProfile: "local",
        profiles: {
          local: {
            controlPlane: "http://127.0.0.1:5108",
            environment: "local",
            credential: {
              kind: "staticBearer",
              accessToken: "profile-token",
              expiresOn: "2099-01-01T00:00:00Z"
            }
          }
        }
      }));
    const credential = new CloudShellProfileCredential({
      configDirectory: directory
    });

    const token = await credential.getToken(["ControlPlane.Access"]);

    assert.equal(token?.token, "profile-token");
    assert.equal(token?.expiresOnTimestamp, Date.parse("2099-01-01T00:00:00Z"));
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("identity credential requests client credentials token from injected environment", async () => {
  const requests: Request[] = [];
  const credential = new CloudShellIdentityCredential({
    environment: {
      CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT: "http://localhost/token",
      CLOUDSHELL_IDENTITY_CLIENT_ID: "application:api/api-service",
      CLOUDSHELL_IDENTITY_CLIENT_SECRET: "local-development-secret"
    },
    fetch: async (request, init) => {
      requests.push(request instanceof Request ? request : new Request(request, init));
      return Response.json({
        access_token: "identity-token",
        expires_in: 3600
      });
    }
  });

  const token = await credential.getToken(["ControlPlane.Access"]);

  assert.equal(token?.token, "identity-token");
  assert.equal(requests[0]!.url, "http://localhost/token");
  assert.equal(requests[0]!.method, "POST");
  const body = await requests[0]!.text();
  assert.equal(body, "grant_type=client_credentials&client_id=application%3Aapi%2Fapi-service&client_secret=local-development-secret&scope=ControlPlane.Access");
});

test("default credential prefers identity token before static environment token", async () => {
  const credential = new DefaultCloudShellCredential([
    new CloudShellIdentityCredential({
      environment: {
        CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT: "http://localhost/token",
        CLOUDSHELL_IDENTITY_CLIENT_ID: "application:api/api-service",
        CLOUDSHELL_IDENTITY_CLIENT_SECRET: "local-development-secret"
      },
      fetch: async () => Response.json({
        access_token: "identity-token"
      })
    }),
    new EnvironmentTokenCredential(undefined, {
      CLOUDSHELL_TOKEN: "environment-token"
    })
  ]);

  const token = await credential.getToken(["ControlPlane.Access"]);

  assert.deepEqual(token, { token: "identity-token", expiresOnTimestamp: undefined });
});

test("profile credential reads relative token file", async () => {
  const directory = await createTemporaryDirectory();
  try {
    await mkdir(join(directory, "tokens"));
    await writeFile(join(directory, "tokens", "local.token"), "file-token\n");
    await writeFile(
      join(directory, CloudShellProfileCredential.defaultConfigFileName),
      JSON.stringify({
        activeProfile: "local",
        profiles: {
          local: {
            credential: {
              kind: "staticBearer",
              accessTokenPath: "tokens/local.token"
            }
          }
        }
      }));
    const credential = new CloudShellProfileCredential({
      configDirectory: directory
    });

    const token = await credential.getToken(["ControlPlane.Access"]);

    assert.equal(token?.token, "file-token");
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("default credential falls back from environment token to profile token", async () => {
  const directory = await createTemporaryDirectory();
  try {
    await writeFile(
      join(directory, CloudShellProfileCredential.defaultConfigFileName),
      JSON.stringify({
        activeProfile: "local",
        profiles: {
          local: {
            credential: {
              kind: "staticBearer",
              accessToken: "profile-token"
            }
          }
        }
      }));
    const credential = new DefaultCloudShellCredential([
      new EnvironmentTokenCredential(undefined, {}),
      new CloudShellProfileCredential({
        configDirectory: directory
      })
    ]);

    const token = await credential.getToken(["ControlPlane.Access"]);

    assert.deepEqual(token, { token: "profile-token", expiresOnTimestamp: undefined });
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

async function createTemporaryDirectory(): Promise<string> {
  return await mkdtemp(join(tmpdir(), "cloudshell-ts-"));
}
