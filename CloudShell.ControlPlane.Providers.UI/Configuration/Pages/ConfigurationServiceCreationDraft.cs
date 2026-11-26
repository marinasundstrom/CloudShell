using CloudShell.Components;

namespace CloudShell.ControlPlane.Providers.UI.Configuration.Pages;

/// <summary>Non-secret, tab-local creation state for Configuration Store and Secrets Vault.</summary>
public sealed class ConfigurationServiceCreationDraft
{
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string GroupId { get; set; } = "";
    public ResourceRegistrationGroupDraft Group { get; set; } = new();

    public string? ValidateBasics() => string.IsNullOrWhiteSpace(Name) ? "Name is required." : Group.Validate();

    public string? ValidateEndpoint()
    {
        if (string.IsNullOrWhiteSpace(Endpoint)) return "Service endpoint is required.";
        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
            return "Enter an absolute HTTP or HTTPS service endpoint.";
        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            return "Use a service endpoint without credentials, query parameters, or a fragment.";
        return null;
    }
}
