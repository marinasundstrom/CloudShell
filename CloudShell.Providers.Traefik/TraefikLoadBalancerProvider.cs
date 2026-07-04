using System.Diagnostics;
using System.Globalization;
using System.Text;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Traefik;

public sealed class TraefikLoadBalancerProvider(TraefikProviderOptions options) :
    ILoadBalancerProvider,
    ILoadBalancerRuntimeProvider
{
    private const string RuntimeCertificateDirectory = "/etc/traefik/certificates";

    public string ProviderName => "traefik";

    public bool CanApply(LoadBalancerProviderContext context) =>
        string.Equals(context.Definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public bool CanManageRuntime(LoadBalancerResourceDefinition definition) =>
        options.ManageRuntimeContainer &&
        string.Equals(definition.Provider, ProviderName, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceProcedureResult> ApplyAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = await WriteDynamicConfigurationAsync(context, cancellationToken);
        return ResourceProcedureResult.Completed(
            $"Applied Traefik configuration for {context.Definition.LoadBalancerRoutes.Count.ToString(CultureInfo.InvariantCulture)} route(s) to {path}.");
    }

    public async Task<ResourceProcedureResult> StartAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = await WriteDynamicConfigurationAsync(
            context,
            useRuntimeContainerPaths: true,
            cancellationToken);
        await StartRuntimeContainerAsync(
            context,
            Path.GetFullPath(path),
            cancellationToken);

        return ResourceProcedureResult.Completed(
            $"Started Traefik container '{CreateContainerName(context.Definition.Id)}' with configuration from {path}.");
    }

    public async Task<ResourceProcedureResult> StopAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await StopRuntimeContainerAsync(context, cancellationToken);
        return ResourceProcedureResult.Completed(
            $"Stopped Traefik container '{CreateContainerName(context.Definition.Id)}'.");
    }

    public async Task<ResourceProcedureResult> DeleteAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await StopRuntimeContainerAsync(context, cancellationToken);
        var path = CreateDynamicConfigurationPath(context.Definition.Id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        DeleteCertificateFiles(context.Definition.Id);

        return ResourceProcedureResult.Completed(
            $"Deleted Traefik runtime container '{CreateContainerName(context.Definition.Id)}' and dynamic configuration.");
    }

    private async Task<string> WriteDynamicConfigurationAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken) =>
        await WriteDynamicConfigurationAsync(
            context,
            useRuntimeContainerPaths: false,
            cancellationToken);

    private async Task<string> WriteDynamicConfigurationAsync(
        LoadBalancerProviderContext context,
        bool useRuntimeContainerPaths,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.DynamicConfigurationDirectory);
        var certificateFiles = await WriteCertificateFilesAsync(
            context,
            useRuntimeContainerPaths,
            cancellationToken);
        var configuration = TraefikDynamicConfigurationWriter.Write(context, certificateFiles);
        var path = CreateDynamicConfigurationPath(context.Definition.Id);
        await File.WriteAllTextAsync(path, configuration, Encoding.UTF8, cancellationToken);
        return path;
    }

    private async Task StartRuntimeContainerAsync(
        LoadBalancerProviderContext context,
        string dynamicConfigurationPath,
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
        arguments.Add($"{dynamicConfigurationPath}:/etc/traefik/dynamic.yml:ro");
        if (context.ResolvedCertificates.Count > 0)
        {
            arguments.Add("-v");
            arguments.Add($"{CreateCertificateDirectory()}:{RuntimeCertificateDirectory}:ro");
        }

        arguments.Add(options.RuntimeContainerImage);
        arguments.Add("--providers.file.filename=/etc/traefik/dynamic.yml");
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

    private Task StopRuntimeContainerAsync(
        LoadBalancerProviderContext context,
        CancellationToken cancellationToken) =>
        RunDockerCommandAsync(
            context.HostResource,
            ["rm", "-f", CreateContainerName(context.Definition.Id)],
            cancellationToken,
            ignoreErrorContaining: "No such container");

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

    private string CreateDynamicConfigurationPath(string resourceId) =>
        Path.Combine(
            options.DynamicConfigurationDirectory,
            $"{CreateFileName(resourceId)}.dynamic.yml");

    private async Task<IReadOnlyDictionary<string, TraefikCertificateFile>> WriteCertificateFilesAsync(
        LoadBalancerProviderContext context,
        bool useRuntimeContainerPaths,
        CancellationToken cancellationToken)
    {
        if (context.ResolvedCertificates.Count == 0)
        {
            return new Dictionary<string, TraefikCertificateFile>(StringComparer.OrdinalIgnoreCase);
        }

        var directory = CreateCertificateDirectory();
        Directory.CreateDirectory(directory);

        var files = new Dictionary<string, TraefikCertificateFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in context.ResolvedCertificates)
        {
            var pem = SplitPemCertificate(certificate);
            var fileName = CreateCertificateFileName(
                context.Definition.Id,
                certificate.EntrypointName);
            var certificatePath = Path.Combine(directory, $"{fileName}.crt");
            var keyPath = Path.Combine(directory, $"{fileName}.key");
            await File.WriteAllTextAsync(certificatePath, pem.Certificate, Encoding.UTF8, cancellationToken);
            await File.WriteAllTextAsync(keyPath, pem.PrivateKey, Encoding.UTF8, cancellationToken);

            var traefikCertificatePath = useRuntimeContainerPaths
                ? $"{RuntimeCertificateDirectory}/{Path.GetFileName(certificatePath)}"
                : Path.GetFullPath(certificatePath);
            var traefikKeyPath = useRuntimeContainerPaths
                ? $"{RuntimeCertificateDirectory}/{Path.GetFileName(keyPath)}"
                : Path.GetFullPath(keyPath);
            files[certificate.EntrypointName] = new TraefikCertificateFile(
                certificate.EntrypointName,
                traefikCertificatePath,
                traefikKeyPath);
        }

        return files;
    }

    private string CreateCertificateDirectory() =>
        Path.Combine(options.DynamicConfigurationDirectory, "certificates");

    private void DeleteCertificateFiles(string resourceId)
    {
        var directory = CreateCertificateDirectory();
        if (!Directory.Exists(directory))
        {
            return;
        }

        var filePrefix = $"{CreateFileName(resourceId)}-";
        foreach (var file in Directory.EnumerateFiles(directory, $"{filePrefix}*.*"))
        {
            File.Delete(file);
        }
    }

    private static (string Certificate, string PrivateKey) SplitPemCertificate(
        LoadBalancerResolvedCertificate certificate)
    {
        var certificates = ExtractPemBlocks(certificate.Value, "CERTIFICATE").ToArray();
        var privateKeys = ExtractPrivateKeyPemBlocks(certificate.Value).ToArray();
        if (certificates.Length == 0 || privateKeys.Length == 0)
        {
            throw new InvalidOperationException(
                $"Certificate '{certificate.Reference.CertificateName}' for load balancer entrypoint '{certificate.EntrypointName}' must contain PEM certificate and private-key blocks before Traefik can materialize TLS.");
        }

        return (
            string.Join(Environment.NewLine, certificates) + Environment.NewLine,
            privateKeys[0] + Environment.NewLine);
    }

    private static IEnumerable<string> ExtractPrivateKeyPemBlocks(string value)
    {
        foreach (var label in new[]
        {
            "PRIVATE KEY",
            "RSA PRIVATE KEY",
            "EC PRIVATE KEY",
            "ENCRYPTED PRIVATE KEY"
        })
        {
            foreach (var block in ExtractPemBlocks(value, label))
            {
                yield return block;
            }
        }
    }

    private static IEnumerable<string> ExtractPemBlocks(
        string value,
        string label)
    {
        var begin = $"-----BEGIN {label}-----";
        var end = $"-----END {label}-----";
        var startIndex = 0;
        while (startIndex < value.Length)
        {
            var beginIndex = value.IndexOf(begin, startIndex, StringComparison.Ordinal);
            if (beginIndex < 0)
            {
                yield break;
            }

            var endIndex = value.IndexOf(end, beginIndex, StringComparison.Ordinal);
            if (endIndex < 0)
            {
                yield break;
            }

            var blockEnd = endIndex + end.Length;
            yield return value[beginIndex..blockEnd].Trim();
            startIndex = blockEnd;
        }
    }

    private static string CreateCertificateFileName(
        string resourceId,
        string entrypointName) =>
        $"{CreateFileName(resourceId)}-{CreateFileName(entrypointName)}";

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
