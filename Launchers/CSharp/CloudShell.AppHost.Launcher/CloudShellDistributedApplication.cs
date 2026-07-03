using CloudShell.ResourceModel;
using Microsoft.Extensions.Configuration;

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
    private readonly Dictionary<string, string> _metadata = [];

    internal CloudShellDistributedApplicationBuilder(
        string name,
        string[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        _args = args;
        AppHostDirectory = FindAppHostDirectory(AppContext.BaseDirectory)
            ?? Environment.CurrentDirectory;
        Configuration = CreateConfiguration(AppHostDirectory, _args);
    }

    public string Name { get; }

    public IReadOnlyList<string> Arguments => _args;

    public string AppHostDirectory { get; }

    public IConfiguration Configuration { get; }

    public ResourceGraphBuilder Resources { get; } = new();

    public CloudShellDistributedApplicationBuilder WithMetadata(
        string name,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _metadata[name.Trim()] = value.Trim();
        return this;
    }

    public string ResolvePath(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.GetFullPath(Path.Combine([AppHostDirectory, .. paths]));
    }

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
        Resources.BuildTemplate(Name, environmentId, MergeMetadata(metadata));

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
        CancellationToken cancellationToken = default) =>
        CloudShellHostLauncher.RunAsync(
            BuildTemplate(options?.EnvironmentId, options?.Metadata),
            options ?? new CloudShellHostLauncherOptions(),
            cancellationToken);

    public async Task<int> LaunchAsync(
        CloudShellHostLauncherOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var launcherOptions = (options ?? CloudShellHostLauncherOptions.FromArguments(
            _args,
            AppHostDirectory,
            Configuration)) with
        {
            Metadata = MergeMetadata(options?.Metadata)
        };

        if (CloudShellHostLauncherOptions.HasArgument(_args, "--run"))
        {
            var result = await RunAsync(launcherOptions, cancellationToken);
            return result.ExitCode;
        }

        if (CloudShellHostLauncherOptions.HasArgument(_args, "--apply") ||
            CloudShellHostLauncherOptions.HasArgument(_args, "--start"))
        {
            var result = await ApplyAsync(launcherOptions, cancellationToken);
            return result.ExitCode;
        }

        var template = BuildTemplate(launcherOptions.EnvironmentId, launcherOptions.Metadata);
        Console.Write(ResourceTemplateSerializer.SerializeTemplate(template, launcherOptions.TemplateFormat));
        return 0;
    }

    private IReadOnlyDictionary<string, string>? MergeMetadata(
        IReadOnlyDictionary<string, string>? metadata)
    {
        if (_metadata.Count == 0)
        {
            return metadata;
        }

        var merged = new Dictionary<string, string>(_metadata, StringComparer.Ordinal);
        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    private static string? FindAppHostDirectory(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IConfigurationRoot CreateConfiguration(
        string appHostDirectory,
        string[] args)
    {
        var environmentName =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ??
            Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        var builder = new ConfigurationBuilder()
            .SetBasePath(appHostDirectory)
            .AddJsonFile("appsettings.json", optional: true);

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            builder.AddJsonFile(
                $"appsettings.{environmentName}.json",
                optional: true);
        }

        builder
            .AddEnvironmentVariables()
            .AddCommandLine(args);

        return builder.Build();
    }
}
