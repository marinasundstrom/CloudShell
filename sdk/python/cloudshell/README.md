# CloudShell Python SDK

This experimental Python SDK provides runtime clients for Python workloads
that consume CloudShell-managed services. The first clients cover Configuration
Store and Secrets Vault. The package targets Python 3.10 or later.

Launcher authoring remains separate under `Launchers/Python/cloudshell`; this
package is for code running inside an app or container.

```python
from cloudshell_sdk import ConfigurationStoreClient, SecretsVaultClient

configuration = ConfigurationStoreClient.from_environment()
message = configuration.get_setting("Sample--Message")

secrets = SecretsVaultClient.from_environment()
api_key = secrets.get_secret("Sample--ApiKey")
```

By default, clients use `DefaultCloudShellCredential`. It checks injected
`CLOUDSHELL_IDENTITY_*` workload identity variables first, then environment
bearer tokens, then the active CloudShell profile from
`~/.cloudshell/config.json`.

Run the package tests with the standard library `unittest` runner:

```bash
./sdk/python/cloudshell/test.sh
```
