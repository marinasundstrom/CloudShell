# Resource Identity Credential

This sample demonstrates the public-preview CloudShell resource credential
chain. It uses `DefaultCloudShellResourceCredential` from
`CloudShell.Abstractions` to acquire a bearer token for the current resource
identity.

The first credential source reads the environment contract injected by
CloudShell resource providers:

```text
CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT
CLOUDSHELL_IDENTITY_CLIENT_ID
CLOUDSHELL_IDENTITY_CLIENT_SECRET
CLOUDSHELL_IDENTITY_SCOPE
```

Run it from a resource process that has those variables, or set them manually
against a development identity provider:

```bash
dotnet run --project samples/ResourceIdentityCredential/CloudShell.ResourceIdentityCredential.csproj -- ControlPlane.Access
```

The sample prints only token metadata, not the token value.
