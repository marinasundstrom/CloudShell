using CloudShell.ResourceModel;

namespace CloudShell.AppHost.Launcher;

public static class CloudShellDistributedApplication
{
    public static CloudShellDistributedApplicationBuilder CreateBuilder(
        string name,
        string[]? args = null) =>
        new(name, args ?? []);
}

public sealed class CloudShellDistributedApplicationBuilder
{
    private readonly string[] _args;

    internal CloudShellDistributedApplicationBuilder(
        string name,
        string[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        _args = args;
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments => _args;

    public ResourceGraphBuilder Resources { get; } = new();

    public CloudShellDistributedApplicationBuilder DefineResources(
        Action<ResourceGraphBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        Resources.DefineResources(configure);
        return this;
    }

    public ResourceTemplate BuildTemplate(
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        Resources.BuildTemplate(Name, environmentId, metadata);

    public Task<string> WriteTemplateAsync(
        string path,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml,
        string? environmentId = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default) =>
        CloudShellHostLauncher.WriteTemplateAsync(
            BuildTemplate(environmentId, metadata),
            path,
            format,
            cancellationToken);

    public Task<CloudShellHostLauncherResult> ApplyAsync(
        CloudShellHostLauncherOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CloudShellHostLauncher.ApplyAsync(
            BuildTemplate(options?.EnvironmentId, options?.Metadata),
            options ?? new CloudShellHostLauncherOptions(),
            cancellationToken);

    public Task<CloudShellHostLauncherResult> RunAsync(
        CloudShellHostLauncherOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveOptions = options ?? new CloudShellHostLauncherOptions();
        effectiveOptions = effectiveOptions with
        {
            StartHost = true
        };
        return ApplyAsync(effectiveOptions, cancellationToken);
    }
}
