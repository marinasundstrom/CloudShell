namespace CloudShell.Abstractions.Logging;

public static class CloudShellLogCategories
{
    public const string Root = "CloudShell";
    public const string ControlPlane = Root + ".ControlPlane";
    public const string ResourceManager = ControlPlane + ".ResourceManager";
    public const string ResourceHealth = ResourceManager + ".ResourceHealth";
    public const string ResourceHealthPolling = ResourceHealth + ".Polling";
    public const string ResourceHealthProbes = ResourceHealth + ".Probes";
    public const string ResourceHealthProbeHttpClient = "System.Net.Http.HttpClient." + ResourceHealthProbes;
    public const string ResourceRecovery = ResourceManager + ".ResourceRecovery";
    public const string ResourceRecoveryPolling = ResourceRecovery + ".Polling";
    public const string ProgrammaticResourceStartup = ResourceManager + ".ProgrammaticResourceStartup";
    public const string HostScopedResourceShutdown = ResourceManager + ".HostScopedResourceShutdown";
    public const string ResourceLifecycle = ResourceManager + ".Lifecycle";
    public const string LocalProcessLifecycle = ResourceManager + ".LocalProcess";
    public const string DockerHostLifecycle = ResourceManager + ".DockerHost";
}
