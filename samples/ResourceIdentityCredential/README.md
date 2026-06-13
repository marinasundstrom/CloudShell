# Resource Identity Credential

This sample demonstrates the public-preview CloudShell resource credential
chain and the SDK-style Control Plane client authentication flow. It uses
`DefaultCloudShellResourceCredential` from `CloudShell.Abstractions` to acquire
a bearer token for the current resource identity and, when a Control Plane base
address is configured, supplies that credential to `RemoteControlPlane`.

The first credential source reads the environment contract injected by
CloudShell resource providers:

```text
CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT
CLOUDSHELL_IDENTITY_CLIENT_ID
CLOUDSHELL_IDENTITY_CLIENT_SECRET
CLOUDSHELL_IDENTITY_SCOPE
```

To call the Control Plane API, also configure one of:

```text
CloudShell__ControlPlane__BaseAddress
CLOUDSHELL_CONTROL_PLANE_ENDPOINT
```

Run it from a resource process that has those variables, or set them manually
against a development identity provider:

```bash
dotnet run --project samples/ResourceIdentityCredential/CloudShell.ResourceIdentityCredential.csproj -- ControlPlane.Access http://localhost:5227
```

The sample prints only token metadata and Control Plane call metadata, not the
token value.
