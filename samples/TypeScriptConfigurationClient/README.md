# TypeScript Configuration Client Sample

This sample uses the experimental `@cloudshell/configuration-client` package
to read Configuration Store entries from a running CloudShell Configuration
Store service.

The client package is intentionally separate from the TypeScript hosting
package. Hosting declares resources; this client is used by application code at
runtime.

Run against an injected or explicit endpoint:

```bash
npm install
CLOUDSHELL_CONFIGURATION_APP_SETTINGS_ENDPOINT=http://localhost:5138/api/configuration/stores/configuration.store%3Aapp-settings/entries \
CLOUDSHELL_TOKEN=<token> \
npm run read
```
