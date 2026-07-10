namespace CloudShell.ControlPlane.DeploymentArtifacts;

public sealed class DeploymentArtifactOptions
{
    public const string SectionName = "DeploymentArtifacts";

    public DeploymentArtifactStoreOptions Store { get; set; } = new();
}

public sealed class DeploymentArtifactStoreOptions
{
    public string Kind { get; set; } = DeploymentArtifactStoreKinds.Disabled;

    public string RootPath { get; set; } = Path.Combine("Data", "deployment-artifacts");

    public long MaxUploadBytes { get; set; } = 256L * 1024L * 1024L;

    public string[] AllowedPackageKinds { get; set; } = ["zip", "tar.gz"];

    public int UploadSessionTimeoutMinutes { get; set; } = 60;
}

public static class DeploymentArtifactStoreKinds
{
    public const string Disabled = "Disabled";
    public const string FileSystem = "FileSystem";
}
