import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { cloudshell, formatHostUrlMessage } from "./index.js";

test("builds a resource template with JavaScript app and configuration store", () => {
  const app = cloudshell("typescript-hosting-poc", {
    metadata: {
      "cloudshell.source": "typescript"
    }
  });

  const settings = app
    .addConfigurationStore("typescript-settings")
    .withDisplayName("TypeScript Settings")
    .withEndpoint("http://localhost:5101")
    .withSeed(seed => seed.setting("Sample--Message", "Hello from TypeScript"));

  const secrets = app
    .addSecretsVault("typescript-secrets")
    .withDisplayName("TypeScript Secrets")
    .withEndpoint("http://localhost:6101")
    .withSeed(seed => seed
      .secret("Sample--ApiKey", "typescript-secret", "v1")
      .certificate("ApiTls", "typescript-certificate", "v1", "application/x-pem-file"));

  const frontendResource = app
    .addJavaScriptApp("typescript-frontend", "samples/TypeScriptAppHost/App")
    .withDisplayName("TypeScript Frontend")
    .withPackageManager("npm")
    .withScript("dev")
    .withServiceDiscovery()
    .withReference(settings)
    .withReference(secrets)
    .withEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT", {
      value: "http://localhost:5101/api/configuration/stores/configuration.store%3Atypescript-settings/settings"
    })
    .withEnvironmentVariable("Sample__Message", {
      configurationSettingRef: settings.setting("Sample--Message")
    })
    .withEnvironmentVariable("Sample__ApiKey", {
      secretRef: secrets.secret("Sample--ApiKey")
    })
    .withHttpEndpoint({
      host: "localhost",
      port: 5173,
      targetPort: 5173
    })
    .withHttpHealthCheck("/healthz", { endpointName: "http" })
    .withHttpLivenessCheck("/alive", { endpointName: "http" })
    .requireIdentity({ name: "typescript-frontend" })
    .provisionIdentityOnStartup();

  settings.allowResourceIdentity(
    frontendResource,
    "CloudShell.Configuration/stores/settings/read/action",
    { identityName: "typescript-frontend" });
  secrets.allowResourceIdentity(
    frontendResource,
    "CloudShell.Secrets/vaults/secrets/read/action",
    { identityName: "typescript-frontend" });

  const defaultNetwork = app.getDefaultNetwork();
  app
    .addLoadBalancer("edge")
    .withDisplayName("Edge")
    .withProvider("traefik")
    .useHost(defaultNetwork)
    .exposeHttps(secrets.certificate("ApiTls"), { port: 4443 })
    .mapHost("frontend.local", frontendResource, { port: 5173, entrypoint: "https" });

  const template = app.buildTemplate();

  assert.equal(template.name, "typescript-hosting-poc");
  assert.equal(template.resources.length, 5);

  const settingsResource = template.resources.find(resource => resource.name === "typescript-settings")!;
  assert.equal(settingsResource.endpoint, "http://localhost:5101");
  assert.deepEqual(settingsResource.attributes, {
    "access.grants": [
      {
        principal: {
          kind: "resourceIdentity",
          id: "application.javascript-app:typescript-frontend/identities/typescript-frontend",
          sourceResourceId: "application.javascript-app:typescript-frontend",
          sourceIdentityName: "typescript-frontend"
        },
        permission: "CloudShell.Configuration/stores/settings/read/action"
      }
    ]
  });
  assert.deepEqual(settingsResource.seed, {
    settings: [
      {
        name: "Sample--Message",
        value: "Hello from TypeScript"
      }
    ]
  });

  const secretsResource = template.resources.find(resource => resource.name === "typescript-secrets")!;
  assert.equal(secretsResource.endpoint, "http://localhost:6101");
  assert.deepEqual(secretsResource.seed, {
    secrets: [
      {
        name: "Sample--ApiKey",
        value: "typescript-secret",
        version: "v1"
      }
    ],
    certificates: [
      {
        name: "ApiTls",
        value: "typescript-certificate",
        version: "v1",
        contentType: "application/x-pem-file"
      }
    ]
  });

  const network = template.resources.find(resource => resource.type === "cloudshell.network")!;
  assert.equal(network.type, "cloudshell.network");
  assert.equal(network.resourceId, "network:host");
  assert.deepEqual(network.network, {
    kind: "Host",
    hostReadiness: "hostReady"
  });

  const frontend = template.resources.find(resource => resource.name === "typescript-frontend")!;
  assert.equal(frontend.type, "application.javascript-app");
  assert.equal(frontend.resourceId, "application.javascript-app:typescript-frontend");
  assert.deepEqual(frontend.attributes, {
    "identity.kind": "required",
    "identity.name": "typescript-frontend",
    "identity.provisionOnStartup": true
  });
  assert.deepEqual(frontend.project, {
    path: "samples/TypeScriptAppHost/App",
    serviceDiscoveryName: "typescript-frontend",
    references: [
      {
        resourceId: "configuration.store:typescript-settings",
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: "configuration.store",
        providerId: "configuration"
      },
      {
        resourceId: "secrets.vault:typescript-secrets",
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: "secrets.vault",
        providerId: "secrets-vault"
      }
    ],
    environmentVariables: {
      CLOUDSHELL_SETTINGS_ENDPOINT: {
        value: "http://localhost:5101/api/configuration/stores/configuration.store%3Atypescript-settings/settings"
      },
      Sample__Message: {
        configurationSettingRef: {
          storeResourceId: "configuration.store:typescript-settings",
          name: "Sample--Message"
        }
      },
      Sample__ApiKey: {
        secretRef: {
          vaultResourceId: "secrets.vault:typescript-secrets",
          name: "Sample--ApiKey"
        }
      }
    }
  });
  assert.deepEqual(frontend.endpoints, [
    {
      name: "http",
      protocol: "http",
      targetPort: 5173,
      host: "localhost",
      port: 5173,
      exposure: "Local",
      network: {
        resourceId: "network:host",
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: "cloudshell.network",
        providerId: "cloudshell.network"
      }
    }
  ]);
  assert.equal(frontend.runtime, "node");
  assert.equal(frontend.packageManager, "npm");
  assert.equal(frontend.script, "dev");
  assert.deepEqual(frontend.health, {
    checks: [
      {
        name: "health",
        type: "health",
        source: {
          kind: "http",
          http: {
            path: "/healthz",
            endpointName: "http"
          }
        }
      },
      {
        name: "alive",
        type: "liveness",
        source: {
          kind: "http",
          http: {
            path: "/alive",
            endpointName: "http"
          }
        }
      }
    ]
  });

  const loadBalancer = template.resources.find(resource => resource.name === "edge")!;
  assert.equal(loadBalancer.type, "cloudshell.loadBalancer");
  assert.equal(loadBalancer.providerId, "cloudshell.load-balancer");
  assert.deepEqual(loadBalancer.loadBalancer, {
    provider: "traefik",
    hostResourceId: "network:host",
    entrypointDefinitions: [
      {
        name: "https",
        protocol: "Https",
        port: 4443,
        exposure: "Public",
        certificateRef: {
          vaultResourceId: "secrets.vault:typescript-secrets",
          name: "ApiTls"
        }
      }
    ],
    routeDefinitions: [
      {
        id: "cloudshell.loadBalancer:edge:route:frontend.local:application.javascript-app:typescript-frontend:5173",
        name: "frontend.local to application.javascript-app:typescript-frontend",
        kind: "Http",
        entrypointName: "https",
        match: {
          host: "frontend.local"
        },
        target: {
          resource: {
            resourceId: "application.javascript-app:typescript-frontend",
            relationship: "reference",
            addressingMode: "resourceId",
            typeId: "application.javascript-app",
            providerId: "applications.javascript-app"
          },
          port: 5173
        }
      }
    ]
  });
});

test("rejects duplicate resource ids", () => {
  const app = cloudshell("duplicates");
  app.resource("one", "example.resource", { resourceId: "example:shared" });

  assert.throws(
    () => app.resource("two", "example.resource", { resourceId: "example:shared" }),
    /already defined/);
});

test("matches shared JavaScript app parity fixture", () => {
  const app = cloudshell("launcher-parity-javascript", {
    metadata: {
      "cloudshell.parity": "javascript-app"
    }
  });

  const settings = app
    .addConfigurationStore("settings")
    .withDisplayName("Settings")
    .withEndpoint("http://localhost:5101")
    .withSeed(seed => seed.setting("Sample--Message", "Hello from launcher parity"));

  const secrets = app
    .addSecretsVault("secrets")
    .withDisplayName("Secrets")
    .withEndpoint("http://localhost:6101")
    .withSeed(seed => seed.secret("Sample--ApiKey", "parity-secret", "v1"));

  app
    .addJavaScriptApp("frontend", "samples/LauncherParity/App")
    .withDisplayName("Frontend")
    .withServiceDiscovery()
    .withReference(settings)
    .withReference(secrets)
    .dependsOn(settings)
    .dependsOn(secrets)
    .withEnvironmentVariable("PORT", "5173")
    .withEnvironmentVariable("Sample__Message", {
      configurationSettingRef: settings.setting("Sample--Message")
    })
    .withEnvironmentVariable("Sample__ApiKey", {
      secretRef: secrets.secret("Sample--ApiKey")
    })
    .withHttpEndpoint({
      host: "localhost",
      port: 5173,
      targetPort: 5173
    })
    .withHttpHealthCheck("/healthz", { endpointName: "http" })
    .withHttpLivenessCheck("/alive", { endpointName: "http" });

  assert.deepEqual(
    normalizeTemplate(app.buildTemplate()),
    normalizeTemplate(loadParityFixture("javascript-app-parity.json")));
});

test("formats host URL message", () => {
  assert.equal(
    formatHostUrlMessage("http://127.0.0.1:5100/"),
    "CloudShell UI: http://127.0.0.1:5100");
});

test("builds a Java app resource template", () => {
  const app = cloudshell("java-hosting-poc");

  app
    .addJavaApp("java-api", "samples/JavaApp/App", "target/cloudshell-java-app-sample.jar")
    .withDisplayName("Java API")
    .withJvmArguments("-Xmx256m")
    .withArguments("--sample")
    .withServiceDiscovery()
    .withHttpEndpoint({
      host: "localhost",
      port: 5185,
      targetPort: 5185
    })
    .withHttpHealthCheck("/healthz", { endpointName: "http" });

  const template = app.buildTemplate();
  const java = template.resources.find(resource => resource.name === "java-api")!;

  assert.equal(java.type, "application.java-app");
  assert.equal(java.resourceId, "application.java-app:java-api");
  assert.equal(java.command, "java");
  assert.equal(java.artifactPath, "target/cloudshell-java-app-sample.jar");
  assert.equal(java.jvmArguments, "-Xmx256m");
  assert.equal(java.arguments, "--sample");
  assert.equal(java.java, undefined);
  assert.deepEqual(java.project, {
    path: "samples/JavaApp/App",
    serviceDiscoveryName: "java-api"
  });
  assert.deepEqual(java.endpoints, [
    {
      name: "http",
      protocol: "http",
      targetPort: 5185,
      host: "localhost",
      port: 5185,
      exposure: "Local",
      network: {
        resourceId: "network:host",
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: "cloudshell.network",
        providerId: "cloudshell.network"
      }
    }
  ]);
});

test("builds Java Maven app as a container app", () => {
  const app = cloudshell("java-container-hosting-poc");

  app
    .addJavaMavenApp(
      "java-api",
      "samples/JavaApp/App",
      "target/cloudshell-java-app-sample.jar",
      "clean package -DskipTests")
    .withHttpEndpoint({
      host: "localhost",
      port: 5185,
      targetPort: 8080
    })
    .asContainerApp({
      tag: "dev",
      dockerfile: "Dockerfile"
    });

  const template = app.buildTemplate();
  const java = template.resources.find(resource => resource.name === "java-api")!;

  assert.equal(java.type, "application.container-app");
  assert.equal(java.providerId, "applications.container-app");
  assert.equal(java.resourceId, "application.container-app:java-api");
  assert.equal(java.command, "java");
  assert.equal(java.artifactPath, "target/cloudshell-java-app-sample.jar");
  assert.equal(java.buildTool, "maven");
  assert.equal(java.buildArguments, "clean package -DskipTests");
  assert.equal(java.java, undefined);
  assert.deepEqual(java.container, {
    image: "cloudshell-java-java-api:dev",
    replicas: 1,
    buildContext: "samples/JavaApp/App",
    dockerfile: "Dockerfile"
  });
  assert.deepEqual(java.endpoints, [
    {
      name: "http",
      protocol: "http",
      targetPort: 8080,
      host: "localhost",
      port: 5185,
      exposure: "Local",
      network: {
        resourceId: "network:host",
        relationship: "reference",
        addressingMode: "resourceId",
        typeId: "cloudshell.network",
        providerId: "cloudshell.network"
      }
    }
  ]);
  assert.equal((java.project as { endpointRequests?: unknown }).endpointRequests, undefined);
  assert.equal((java.container as { endpointRequests?: unknown }).endpointRequests, undefined);
});

function loadParityFixture(name: string): unknown {
  const testdataPath = join(
    dirname(fileURLToPath(import.meta.url)),
    "..",
    "..",
    "..",
    "testdata",
    name);
  return JSON.parse(readFileSync(testdataPath, "utf8")) as unknown;
}

function normalizeTemplate(value: unknown): unknown {
  const normalized = normalizeValue(value);
  if (!isRecord(normalized) || !Array.isArray(normalized.resources)) {
    return normalized;
  }

  return {
    ...normalized,
    resources: [...normalized.resources].sort((left, right) =>
      resourceId(left).localeCompare(resourceId(right)))
  };
}

function normalizeValue(value: unknown): unknown {
  if (Array.isArray(value)) {
    return value.map(item => normalizeValue(item));
  }

  if (isRecord(value)) {
    if ("resourceId" in value && !("name" in value) && !("type" in value)) {
      return { resourceId: value.resourceId };
    }

    return Object.fromEntries(
      Object.entries(value).map(([key, item]) => [key, normalizeValue(item)]));
  }

  return value;
}

function resourceId(value: unknown): string {
  return isRecord(value) && typeof value.resourceId === "string"
    ? value.resourceId
    : "";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
