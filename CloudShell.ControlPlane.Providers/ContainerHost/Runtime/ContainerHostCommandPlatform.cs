using System.Diagnostics;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public interface IContainerHostCommandPlatform
{
    ContainerHostCommandPlan CreatePlan();
}

public interface IContainerHostCommandAdapter
{
    bool CanHandle(ContainerHostDescriptor? host);

    string RuntimeName { get; }

    string ResolveExecutable(ContainerHostDescriptor? host);

    IReadOnlyList<string> AdaptArguments(IReadOnlyList<string> arguments);

    void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor? host);
}

public sealed class ContainerHostCommandPlatform(
    IEnumerable<IContainerHostProvider> containerHostProviders,
    IHostToolResolver hostToolResolver,
    IEnumerable<IContainerHostCommandAdapter>? commandAdapters = null) : IContainerHostCommandPlatform
{
    public const string ExecutableMetadataKey = "cloudshell.executable";

    private readonly IReadOnlyList<IContainerHostCommandAdapter> commandAdapters =
        commandAdapters?.Any() == true
            ? commandAdapters.ToArray()
            : DefaultCommandAdapters;

    private static readonly IContainerHostCommandAdapter[] DefaultCommandAdapters =
    [
        new PodmanContainerHostCommandAdapter(),
        new DockerCompatibleContainerHostCommandAdapter()
    ];

    public ContainerHostCommandPlan CreatePlan()
    {
        var host = containerHostProviders.FirstOrDefault()?.GetDefaultHost();
        var adapter = commandAdapters.FirstOrDefault(candidate => candidate.CanHandle(host));
        if (adapter is null)
        {
            return ContainerHostCommandPlan.Unavailable(
                host,
                string.Empty,
                CreateMissingAdapterReason(host));
        }

        var executable = adapter.ResolveExecutable(host);
        if (hostToolResolver.IsAvailable(executable))
        {
            return ContainerHostCommandPlan.Available(host, executable, adapter);
        }

        return ContainerHostCommandPlan.Unavailable(
            host,
            executable,
            CreateUnavailableReason(host, executable, adapter.RuntimeName));
    }

    internal static string ResolveExecutable(
        ContainerHostDescriptor? host,
        string defaultExecutable) =>
        host?.HostMetadata.TryGetValue(ExecutableMetadataKey, out var executable) == true &&
        !string.IsNullOrWhiteSpace(executable)
            ? executable.Trim()
            : defaultExecutable;

    private static string CreateMissingAdapterReason(ContainerHostDescriptor? host)
    {
        var kind = host?.Kind.ToString() ?? ContainerHostKind.Docker.ToString();
        return
            $"Container host kind '{kind}' does not have a registered command adapter. Register an {nameof(IContainerHostCommandAdapter)} for this runtime before dispatching container commands.";
    }

    private static string CreateUnavailableReason(
        ContainerHostDescriptor? host,
        string executable,
        string runtime)
    {
        if (host?.HostMetadata.ContainsKey(ExecutableMetadataKey) == true)
        {
            return
                $"Configured {runtime} executable '{executable}' is unavailable. Update container host metadata '{ExecutableMetadataKey}' or install the executable on the host PATH.";
        }

        return
            $"{runtime} executable '{executable}' is unavailable. Install {runtime} or configure container host metadata '{ExecutableMetadataKey}' with an executable path.";
    }
}

public sealed record ContainerHostCommandPlan(
    ContainerHostDescriptor? Host,
    string Executable,
    string? UnavailableReason,
    IContainerHostCommandAdapter? Adapter = null)
{
    public bool IsAvailable => UnavailableReason is null;

    public static ContainerHostCommandPlan Available(
        ContainerHostDescriptor? host,
        string executable,
        IContainerHostCommandAdapter adapter) =>
        new(host, executable, null, adapter);

    public static ContainerHostCommandPlan Unavailable(
        ContainerHostDescriptor? host,
        string executable,
        string reason) =>
        new(host, executable, reason);

    public ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(UnavailableReason);
        }

        var startInfo = new ProcessStartInfo(Executable)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        Adapter!.ConfigureEnvironment(startInfo, Host);
        foreach (var argument in Adapter.AdaptArguments(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

public sealed class DockerCompatibleContainerHostCommandAdapter : IContainerHostCommandAdapter
{
    public bool CanHandle(ContainerHostDescriptor? host) =>
        host is null ||
        host.Kind is ContainerHostKind.Docker or ContainerHostKind.DockerCompatible;

    public string RuntimeName => "Docker";

    public string ResolveExecutable(ContainerHostDescriptor? host) =>
        ContainerHostCommandPlatform.ResolveExecutable(host, "docker");

    public IReadOnlyList<string> AdaptArguments(IReadOnlyList<string> arguments) => arguments;

    public void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor? host)
    {
        if (!string.IsNullOrWhiteSpace(host?.Endpoint))
        {
            startInfo.Environment["DOCKER_HOST"] = host.Endpoint;
        }
    }
}

public sealed class PodmanContainerHostCommandAdapter : IContainerHostCommandAdapter
{
    public bool CanHandle(ContainerHostDescriptor? host) =>
        host?.Kind == ContainerHostKind.Podman;

    public string RuntimeName => "Podman";

    public string ResolveExecutable(ContainerHostDescriptor? host) =>
        ContainerHostCommandPlatform.ResolveExecutable(host, "podman");

    public IReadOnlyList<string> AdaptArguments(IReadOnlyList<string> arguments) => arguments;

    public void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor? host)
    {
        if (!string.IsNullOrWhiteSpace(host?.Endpoint))
        {
            startInfo.Environment["CONTAINER_HOST"] = host.Endpoint;
        }
    }
}

public static class ContainerHostCommandPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddContainerHostCommandPlatform(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IHostToolResolver, PathHostToolResolver>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IContainerHostCommandAdapter, PodmanContainerHostCommandAdapter>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IContainerHostCommandAdapter, DockerCompatibleContainerHostCommandAdapter>());
        services.TryAddSingleton<IContainerHostCommandPlatform, ContainerHostCommandPlatform>();
        return services;
    }
}
