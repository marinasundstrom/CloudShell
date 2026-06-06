namespace CloudShell.Configuration;

public sealed class CloudShellConfigurationOptions
{
    public string? Endpoint { get; set; }

    public string? Token { get; set; }

    public string? ServiceName { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    public string MetadataPrefix { get; set; } = "CloudShell:Configuration";

    public bool LoadSecretValues { get; set; } = true;
}
