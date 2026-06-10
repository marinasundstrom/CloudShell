using System.Diagnostics;
using System.Globalization;
using System.Text;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Traefik;

public sealed class TraefikLoadBalancerProvider(TraefikProviderOptions options) : ILoadBalancerProvider
{
    public string ProviderName => "traefik";

    public bool CanApply(LoadBalancerProviderContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ApplyAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var configuration = TraefikDynamicConfigurationWriter.Write(context);
        Directory.CreateDirectory(options.DynamicConfigurationDirectory);
        var path = Path.Combine(
            options.DynamicConfigurationDirectory,
            $"{CreateFileName(context.Definition.Id)}.dynamic.yml");
        await File.WriteAllTextAsync(path, configuration, Encoding.UTF8, cancellationToken);

        if (options.ManageRuntimeContainer)
        {
            await StartRuntimeContainerAsync(
                context,
                Path.GetFullPath(options.DynamicConfigurationDirectory),
                cancellationToken);

            return ResourceProcedureResult.Completed(
                $"Applied Traefik configuration for {context.Definition.LoadBalancerRoutes.Count.ToString(CultureInfo.InvariantCulture)} route(s) to {path} and started Traefik container '{CreateContainerName(context.Definition.Id)}'.");
        }

        return ResourceProcedureResult.Completed(
            $"Applied Traefik configuration for {context.Definition.LoadBalancerRoutes.Count.ToString(CultureInfo.InvariantCulture)} route(s) to {path}.");
    }

    private async Task StartRuntimeContainerAsync(
        LoadBalancerProviderContext context,
        string dynamicConfigurationDirectory,
        CancellationToken cancellationToken)
    {
        var containerName = CreateContainerName(context.Definition.Id);
        await RunDockerCommandAsync(
            context.HostResource,
            ["network", "create", options.RuntimeContainerNetwork],
            cancellationToken,
            ignoreErrorContaining: "already exists");
        await RunDockerCommandAsync(
            context.HostResource,
            ["rm", "-f", containerName],
            cancellationToken,
            ignoreErrorContaining: "No such container");

        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            containerName,
            "--network",
            options.RuntimeContainerNetwork
        };
        foreach (var entrypoint in context.Definition.LoadBalancerEntrypoints)
        {
            arguments.Add("-p");
            arguments.Add($"{entrypoint.Port.ToString(CultureInfo.InvariantCulture)}:{entrypoint.Port.ToString(CultureInfo.InvariantCulture)}/{GetDockerPublishProtocol(entrypoint.Protocol)}");
        }

        arguments.Add("-v");
        arguments.Add($"{dynamicConfigurationDirectory}:/etc/traefik/dynamic:ro");
        arguments.Add(options.RuntimeContainerImage);
        arguments.Add("--providers.file.directory=/etc/traefik/dynamic");
        arguments.Add("--providers.file.watch=true");
        foreach (var entrypoint in context.Definition.LoadBalancerEntrypoints)
        {
            arguments.Add($"--entrypoints.{entrypoint.Name}.address=:{entrypoint.Port.ToString(CultureInfo.InvariantCulture)}");
        }

        await RunDockerCommandAsync(
            context.HostResource,
            arguments,
            cancellationToken);
    }

    private static async Task RunDockerCommandAsync(
        Resource? hostResource,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? ignoreErrorContaining = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var endpoint = ResolveDockerHostEndpoint(hostResource);
        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            startInfo.Environment["DOCKER_HOST"] = endpoint;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Docker command could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode == 0 ||
            (!string.IsNullOrWhiteSpace(ignoreErrorContaining) &&
                error.Contains(ignoreErrorContaining, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var message = string.IsNullOrWhiteSpace(error) ? output : error;
        throw new InvalidOperationException(
            $"Docker command failed while applying Traefik load balancer runtime: {message.Trim()}");
    }

    private static string? ResolveDockerHostEndpoint(Resource? hostResource)
    {
        if (hostResource?.ResourceAttributes.TryGetValue("docker.host.endpoint", out var endpoint) == true &&
            !string.IsNullOrWhiteSpace(endpoint))
        {
            return endpoint;
        }

        return null;
    }

    private static string GetDockerPublishProtocol(ResourceEndpointProtocol protocol) =>
        protocol == ResourceEndpointProtocol.Udp ? "udp" : "tcp";

    private static string CreateContainerName(string resourceId) =>
        $"cloudshell-{CreateFileName(resourceId)}";

    private static string CreateFileName(string resourceId)
    {
        var builder = new StringBuilder(resourceId.Length);
        foreach (var character in resourceId.Trim())
        {
            builder.Append(char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        }

        var fileName = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(fileName) ? "load-balancer" : fileName;
    }
}
