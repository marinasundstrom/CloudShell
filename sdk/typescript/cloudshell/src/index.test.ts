import test from "node:test";
import assert from "node:assert/strict";
import { cloudshell } from "./index.js";

test("builds a resource template with JavaScript app and configuration store", () => {
  const app = cloudshell("typescript-hosting-poc", {
    metadata: {
      "cloudshell.source": "typescript"
    }
  });

  const settings = app
    .addConfigurationStore("typescript-settings")
    .withDisplayName("TypeScript Settings")
    .withEndpoint("http://localhost:5101");

  app
    .addJavaScriptApp("typescript-frontend", "samples/TypeScriptAppHost/App")
    .withDisplayName("TypeScript Frontend")
    .withPackageManager("npm")
    .withScript("dev")
    .withServiceDiscovery()
    .withReference(settings)
    .withEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT", {
      value: "http://localhost:5101/api/configuration/stores/configuration.store%3Atypescript-settings/entries"
    })
    .withEnvironmentVariable("Sample__Message", {
      configurationEntryRef: settings.entry("Sample--Message")
    })
    .withHttpEndpoint({
      host: "localhost",
      port: 5173,
      targetPort: 5173
    })
    .withHttpHealthCheck("/healthz", { endpointName: "http" })
    .withHttpLivenessCheck("/alive", { endpointName: "http" });

  const template = app.buildTemplate();

  assert.equal(template.name, "typescript-hosting-poc");
  assert.equal(template.resources.length, 3);

  const settingsResource = template.resources.find(resource => resource.name === "typescript-settings")!;
  assert.deepEqual(settingsResource.attributes, {
    configuration: {
      endpoint: "http://localhost:5101"
    }
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
  assert.deepEqual(frontend.javascript, {
    engine: "node",
    packageManager: "npm",
    script: "dev"
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
      }
    ],
    environmentVariables: {
      CLOUDSHELL_SETTINGS_ENDPOINT: {
        value: "http://localhost:5101/api/configuration/stores/configuration.store%3Atypescript-settings/entries"
      },
      Sample__Message: {
        configurationEntryRef: {
          storeResourceId: "configuration.store:typescript-settings",
          name: "Sample--Message"
        }
      }
    },
    endpointRequests: [
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
    ]
  });
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
});

test("rejects duplicate resource ids", () => {
  const app = cloudshell("duplicates");
  app.resource("one", "example.resource", { resourceId: "example:shared" });

  assert.throws(
    () => app.resource("two", "example.resource", { resourceId: "example:shared" }),
    /already defined/);
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
  assert.deepEqual(java.java, {
    command: "java",
    artifactPath: "target/cloudshell-java-app-sample.jar",
    jvmArguments: "-Xmx256m",
    arguments: "--sample"
  });
  assert.deepEqual(java.project, {
    path: "samples/JavaApp/App",
    serviceDiscoveryName: "java-api",
    endpointRequests: [
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
    ]
  });
});
