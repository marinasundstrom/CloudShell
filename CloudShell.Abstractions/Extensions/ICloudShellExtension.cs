namespace CloudShell.Abstractions.Extensions;

public interface ICloudShellExtension
{
    CloudShellExtensionManifest Manifest { get; }

    void Configure(ICloudShellExtensionBuilder builder);
}
