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

`CLOUDSHELL_TOKEN` is only the sample's explicit token input. CloudShell-hosted
applications should use the resource identity credential flow when it is
available and let the client acquire a bearer token from that credential.

The sample prints the endpoint it selected, the number of entries, the setting
keys returned by the service, and a configuration object with portable `--`
setting names mapped to `:` keys. A minimal successful output looks like:

```json
{
  "entryCount": 2,
  "keys": [
    "Orders:Api:BaseUrl",
    "Sample:Message"
  ]
}
```

The local smoke test for this sample used a small Node.js HTTP service that
returned Configuration Store entries only when the request included
`Authorization: Bearer configuration-token`, then ran `npm run read` with
`CLOUDSHELL_CONFIGURATION_APP_SETTINGS_ENDPOINT` pointing at that service and
`CLOUDSHELL_TOKEN=configuration-token`.
