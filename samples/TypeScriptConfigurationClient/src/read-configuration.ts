import {
  ConfigurationStoreClient
} from "@cloudshell/configuration-client";

const client = ConfigurationStoreClient.fromEnvironment({
  serviceName: process.env.CLOUDSHELL_CONFIGURATION_SERVICE_NAME
});

const settings = await client.getSettings();
const configuration = await client.toObject();

console.log(JSON.stringify({
  endpoint: client.settingsEndpoint.toString(),
  settingCount: settings.length,
  keys: Object.keys(configuration).sort(),
  configuration
}, null, 2));
