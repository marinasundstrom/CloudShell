namespace CloudShell.Providers.Docker;

public sealed class DockerProviderOptions
{
    public Uri? Endpoint { get; set; }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(3);

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(10);

    internal Uri ResolveEndpoint()
    {
        if (Endpoint is not null)
        {
            return Endpoint;
        }

        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (Uri.TryCreate(dockerHost, UriKind.Absolute, out var configuredEndpoint))
        {
            return configuredEndpoint;
        }

        if (OperatingSystem.IsWindows())
        {
            return new Uri("npipe://./pipe/docker_engine");
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".docker",
                "run",
                "docker.sock"),
            Path.Combine(
                Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") ?? string.Empty,
                "docker.sock"),
            "/var/run/docker.sock"
        };

        var socketPath = candidates.FirstOrDefault(path =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(path));

        return new Uri($"unix://{socketPath ?? "/var/run/docker.sock"}");
    }
}
