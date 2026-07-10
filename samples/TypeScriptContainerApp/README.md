# TypeScript Container App Sample

This sample demonstrates a TypeScript launcher declaring a JavaScript/Node.js
container app with Configuration Store, Secrets Vault, service discovery, and
managed resource identity grants.

The launched app is a Node.js HTTP service. It uses
`sdk/typescript/configuration-client` to resolve Configuration Store and
Secrets Vault endpoints from the container runtime environment and exposes the
result at `/configuration` without returning secret values.

## Tooling

- TypeScript 5.9.3 for the launcher app host manifest and lockfile.
- Node.js 22 for the container runtime image.
- The runtime app consumes `@cloudshell/configuration-client` through a local
  `file:` dependency.

## Commands

```bash
./cloudshell.sh template
./cloudshell.sh run
./cloudshell.sh start-app
curl http://localhost:5192/configuration
```
