# Settings and Secrets

This sample declares a Web API resource programmatically and assigns runtime
environment variables from references:

- `SAMPLE_MESSAGE` and `SAMPLE_MODE` come from a `configuration.store`
  resource through `settings.Entry(...)`.
- `SAMPLE_API_KEY` comes from a `secrets.vault` resource through
  `secrets.Secret(...)`.

The application resource stores references, not copied values. CloudShell
resolves those references when the resource is started.
