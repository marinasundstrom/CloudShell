using System.Diagnostics;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public interface IContainerHostCommandPlatform
{
    ContainerHostCommandPlan CreatePlan();
}

public sealed class ContainerHostCommandPlatform(
    IEnumerable<IContainerHostProvider> containerHostProviders,
    IHostToolResolver hostToolResolver) : IContainerHostCommandPlatform
{
    public const string ExecutableMetadataKey = "cloudshell.executable";

    public ContainerHostCommandPlan CreatePlan()
    {
        var host = containerHostProviders.FirstOrDefault()?.GetDefaultHost();
        var executable = ResolveExecutable(host);
        if (hostToolResolver.IsAvailable(executable))
        {
            return ContainerHostCommandPlan.Available(host, executable);
        }

        return ContainerHostCommandPlan.Unavailable(
            host,
            executable,
            CreateUnavailableReason(host, executable));
    }

    public static string ResolveExecutable(ContainerHostDescriptor? host) =>
        host?.HostMetadata.TryGetValue(ExecutableMetadataKey, out var executable) == true &&
        !string.IsNullOrWhiteSpace(executable)
            ? executable.Trim()
            : host?.Kind == ContainerHostKind.Podman ? "podman" : "docker";

    private static string CreateUnavailableReason(
        ContainerHostDescriptor? host,
        string executable)
    {
        var runtime = host?.Kind == ContainerHostKind.Podman ? "Podman" : "Docker";
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
    string? UnavailableReason)
{
    public bool IsAvailable => UnavailableReason is null;

    public static ContainerHostCommandPlan Available(
        ContainerHostDescriptor? host,
        string executable) =>
        new(host, executable, null);

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
        ConfigureEnvironment(startInfo, Host);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void ConfigureEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor? host)
    {
        if (host is null ||
            string.IsNullOrWhiteSpace(host.Endpoint))
        {
            return;
        }

        if (host.Kind == ContainerHostKind.Podman)
        {
            startInfo.Environment["CONTAINER_HOST"] = host.Endpoint;
            return;
        }

        startInfo.Environment["DOCKER_HOST"] = host.Endpoint;
    }
}

public static class ContainerHostCommandPlatformServiceCollectionExtensions
{
    public static IServiceCollection AddContainerHostCommandPlatform(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IHostToolResolver, PathHostToolResolver>();
        services.TryAddSingleton<IContainerHostCommandPlatform, ContainerHostCommandPlatform>();
        return services;
    }
}
