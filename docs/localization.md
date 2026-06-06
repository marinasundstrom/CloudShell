# Localization

CloudShell uses ASP.NET Core localization for host-owned UI. The selected UI language is stored in the standard request-culture cookie, so the choice persists across browser sessions.

## Configure available languages

The language picker shows the cultures listed in `CloudShell.Host/appsettings.json`:

```json
{
  "Localization": {
    "DefaultCulture": "en",
    "SupportedCultures": [ "en" ]
  }
}
```

Add a culture code to `SupportedCultures` only after the host has resources for that culture. For example, add `sv-SE` after creating the corresponding host resource file.

## Add host translations

Host-owned strings use `IStringLocalizer<SharedResource>`. Add translations under `CloudShell.Host/Resources` using the shared resource name:

```text
CloudShell.Host/Resources/SharedResource.sv-SE.resx
```

Use the English source text as the resource key. If a key is missing, ASP.NET Core falls back to the key text.

## Extension localization

Extensions own their own localization. That includes extension pages, registration components, resource type labels, provider action labels, logs, and any extension-contributed navigation or view text. The host renders extension-provided labels as supplied and only localizes its own shell, account, resource manager, logs, extension catalog, and shared chrome.

If an extension supports multiple languages, it should add its own resources and localizers in its assembly, then emit localized labels from its own components and services.
