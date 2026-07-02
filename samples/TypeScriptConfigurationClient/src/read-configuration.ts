import {
  ConfigurationStoreClient,
  StaticTokenCredential
} from "@cloudshell/configuration-client";

const token = process.env.CLOUDSHELL_TOKEN ??
  process.env.CLOUDSHELL_CONFIGURATION_TOKEN ??
  process.env.CLOUDSHELL_CONTROL_PLANE_TOKEN;

const client = ConfigurationStoreClient.fromEnvironment({
  serviceName: process.env.CLOUDSHELL_CONFIGURATION_SERVICE_NAME,
  credential: new StaticTokenCredential(token ?? "")
});

const entries = await client.getEntries();
const configuration = await client.toObject();

console.log(JSON.stringify({
  endpoint: client.entriesEndpoint.toString(),
  entryCount: entries.length,
  keys: Object.keys(configuration).sort(),
  configuration
}, null, 2));
