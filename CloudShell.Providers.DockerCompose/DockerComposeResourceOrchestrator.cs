using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.DockerCompose;

public sealed partial class DockerComposeResourceOrchestrator(
    DockerComposeOrchestratorOptions options,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IContainerHostResolver containerHostResolver) : IResourceOrchestrator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string Id => "docker-compose";

    public string DisplayName => "Docker Compose";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        options.Enabled &&
        HasComposeConfiguration(context) &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop or ResourceActionKind.Restart;

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!CanExecute(context, action))
        {
            throw new NotSupportedException(
                $"Docker Compose cannot execute action '{action.DisplayName}' for resource '{context.Resource.Name}'.");
        }

        await EnsureGeneratedComposeFileAsync(context, cancellationToken);
        var serviceName = await ResolveComposeServiceNameAsync(context, cancellationToken);
        IReadOnlyList<string> arguments = action.Kind switch
        {
            ResourceActionKind.Start => ["compose", .. GetBaseArguments(), "up", "-d", serviceName],
            ResourceActionKind.Stop => ["compose", .. GetBaseArguments(), "stop", serviceName],
            ResourceActionKind.Restart => ["compose", .. GetBaseArguments(), "restart", serviceName],
            _ => throw new NotSupportedException(
                $"Docker Compose does not support action '{action.DisplayName}'.")
        };

        var result = await RunDockerAsync(
            arguments,
            await ResolveDockerHostAsync(context, cancellationToken),
            cancellationToken);

        return ResourceProcedureResult.Completed(
            string.IsNullOrWhiteSpace(result)
                ? $"{action.DisplayName} requested for {context.Resource.Name} through Docker Compose."
                : result);
    }

    public bool CanDelete(ResourceOrchestrationContext context) => false;

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Docker Compose does not delete CloudShell resource registrations.");

    private bool HasComposeConfiguration(ResourceOrchestrationContext context) =>
        !string.IsNullOrWhiteSpace(options.ComposeFilePath) ||
        File.Exists(Path.Combine(ResolveWorkingDirectory(), "compose.yaml")) ||
        File.Exists(Path.Combine(ResolveWorkingDirectory(), "docker-compose.yml")) ||
        (options.GenerateComposeFile && CanDescribe(context.Resource));

    private IEnumerable<string> GetBaseArguments()
    {
        if (!string.IsNullOrWhiteSpace(options.ProjectName))
        {
            yield return "--project-name";
            yield return options.ProjectName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(options.ComposeFilePath))
        {
            yield return "--file";
            yield return options.ComposeFilePath.Trim();
        }

        if (options.GenerateComposeFile)
        {
            var generatedFile = ResolveGeneratedComposeFilePath();
            if (File.Exists(generatedFile))
            {
                yield return "--file";
                yield return generatedFile;
            }
        }
    }

    private async Task<string> RunDockerAsync(
        IReadOnlyList<string> arguments,
        string? dockerHost,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.CommandTimeout);

        var output = new StringBuilder();
        var error = new StringBuilder();
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            WorkingDirectory = ResolveWorkingDirectory(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(dockerHost))
        {
            startInfo.Environment["DOCKER_HOST"] = dockerHost;
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                output.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                error.AppendLine(args.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException("Docker CLI could not be started.", exception);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(timeout.Token);

        if (process.ExitCode != 0)
        {
            var message = error.Length > 0 ? error.ToString().Trim() : output.ToString().Trim();
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(message)
                    ? $"Docker Compose exited with code {process.ExitCode}."
                    : message);
        }

        return output.ToString().Trim();
    }

    private string ResolveWorkingDirectory() =>
        string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(options.WorkingDirectory);

    private string ResolveGeneratedComposeFilePath() =>
        Path.IsPathRooted(options.GeneratedComposeFilePath)
            ? options.GeneratedComposeFilePath
            : Path.GetFullPath(options.GeneratedComposeFilePath, ResolveWorkingDirectory());

    private async Task<string?> ResolveDockerHostAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var workload = await ResolveExecutionWorkloadAsync(context, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Docker Compose could not resolve a container workload for resource '{context.Resource.Name}'.");
        var explicitHostId = FirstNonEmpty(
            workload.ContainerHostId,
            options.ContainerHostId);
        var result = await containerHostResolver.ResolveAsync(
            new ContainerHostResolutionRequest(
                context.Resource.Id,
                context.ResourceGroup?.Id,
                explicitHostId,
                context.PreferredContainerHostId),
            cancellationToken);
        if (result.IsResolved)
        {
            return result.Host!.Endpoint;
        }

        throw new InvalidOperationException(
            result.ErrorMessage ??
            $"Resource '{context.Resource.Name}' is container-backed but no container host is registered. Use UseDocker() or select an explicit container host for the workload.");
    }

    private async Task EnsureGeneratedComposeFileAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        if (!options.GenerateComposeFile)
        {
            return;
        }

        var compose = await CreateComposeDocumentAsync(context, cancellationToken);
        if (compose is null)
        {
            return;
        }

        var path = ResolveGeneratedComposeFilePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, compose, cancellationToken);
    }

    private async Task<string?> CreateComposeDocumentAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var resources = context.ResourceManager.GetResources()
            .Where(resource => IsSameGroup(context, resource))
            .ToArray();
        var descriptors = new List<ResourceOrchestrationDescriptor>();

        foreach (var resource in resources)
        {
            var provider = descriptorProviders.FirstOrDefault(provider => provider.CanDescribe(resource));
            if (provider is null)
            {
                continue;
            }

            descriptors.Add(await provider.DescribeAsync(
                resource,
                new ResourceOrchestrationDescriptorContext(
                    context.Registrations.GetRegistration(resource.Id),
                    context.ResourceManager.GetGroupForResource(resource.Id),
                    context.ResourceManager),
                cancellationToken));
        }

        var workloads = descriptors
            .Select(descriptor => (Descriptor: descriptor, Workload: TryReadWorkload(descriptor)))
            .Where(item => item.Workload is not null)
            .ToDictionary(
                item => item.Descriptor.ResourceId,
                item => item,
                StringComparer.OrdinalIgnoreCase);
        if (workloads.Count == 0)
        {
            return null;
        }

        var platformServices = descriptors
            .Select(TryReadService)
            .Where(service => service is not null)
            .Select(service => service!)
            .ToArray();
        var networks = descriptors
            .Select(TryReadNetwork)
            .Where(network => network is not null)
            .Select(network => network!)
            .ToArray();

        var orchestratorServices = CreateOrchestratorServices(workloads, platformServices);
        return RenderComposeDocument(orchestratorServices, platformServices, networks);
    }

    private static bool IsSameGroup(
        ResourceOrchestrationContext context,
        Resource resource)
    {
        var group = context.ResourceManager.GetGroupForResource(resource.Id);
        return string.Equals(group?.Id, context.ResourceGroup?.Id, StringComparison.OrdinalIgnoreCase);
    }

    private bool CanDescribe(Resource resource) =>
        descriptorProviders.Any(provider => provider.CanDescribe(resource));

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeAsync(
        Resource resource,
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var provider = descriptorProviders.FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        return await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(
                context.Registrations.GetRegistration(resource.Id),
                context.ResourceManager.GetGroupForResource(resource.Id),
                context.ResourceManager),
            cancellationToken);
    }

    private async Task<ResourceWorkloadConfiguration?> ResolveExecutionWorkloadAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var descriptor = await TryDescribeAsync(context.Resource, context, cancellationToken);
        if (descriptor is not null)
        {
            var workload = TryReadWorkload(descriptor);
            if (workload is not null)
            {
                return workload;
            }

            var service = TryReadService(descriptor);
            var target = service?.Targets.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(target?.ResourceId))
            {
                var targetResource = context.ResourceManager.GetResource(target.ResourceId);
                if (targetResource is not null)
                {
                    var targetDescriptor = await TryDescribeAsync(targetResource, context, cancellationToken);
                    return targetDescriptor is null ? null : TryReadWorkload(targetDescriptor);
                }
            }
        }

        return null;
    }

    private static ResourceWorkloadConfiguration? TryReadWorkload(
        ResourceOrchestrationDescriptor descriptor)
    {
        try
        {
            var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(SerializerOptions);
            return workload?.Kind is ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild
                ? workload
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ServiceResourceDefinition? TryReadService(ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals("cloudshell.service", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ServiceResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static NetworkResourceDefinition? TryReadNetwork(ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals("cloudshell.network", StringComparison.OrdinalIgnoreCase) &&
            !descriptor.ResourceType.Equals("cloudshell.virtualNetwork", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<NetworkResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<ResourceOrchestratorService> CreateOrchestratorServices(
        IReadOnlyDictionary<string, (ResourceOrchestrationDescriptor Descriptor, ResourceWorkloadConfiguration? Workload)> workloads,
        IReadOnlyList<ServiceResourceDefinition> platformServices) =>
        workloads
            .Select(item =>
            {
                var platformServicesForWorkload = platformServices
                    .Where(service => service.Targets.Any(target =>
                        string.Equals(target.ResourceId, item.Key, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                var ports = platformServicesForWorkload
                    .SelectMany(service => service.Ports)
                    .Concat(item.Value.Workload!.WorkloadPorts)
                    .GroupBy(
                        port => $"{port.Name}:{port.TargetPort}:{port.Port?.ToString() ?? string.Empty}:{port.Protocol}",
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.Last())
                    .ToArray();
                var networks = platformServicesForWorkload
                    .SelectMany(service => service.NetworkIds)
                    .Concat(item.Value.Descriptor.Networks)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var dependencies = item.Value.Descriptor.DependsOn
                    .Where(workloads.ContainsKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                return new ResourceOrchestratorService(
                    item.Key,
                    ToComposeServiceName(item.Key),
                    item.Value.Workload!,
                    dependencies,
                    networks,
                    ports);
            })
            .ToArray();

    private string RenderComposeDocument(
        IReadOnlyList<ResourceOrchestratorService> orchestratorServices,
        IReadOnlyList<ServiceResourceDefinition> platformServices,
        IReadOnlyList<NetworkResourceDefinition> networkDefinitions)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"name: {QuoteYaml(string.IsNullOrWhiteSpace(options.ProjectName) ? "cloudshell" : options.ProjectName.Trim())}");
        builder.AppendLine("services:");

        foreach (var service in orchestratorServices.OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase))
        {
            var workload = service.Workload;
            var serviceName = service.Name;
            var usesIngress = ShouldUseComposeIngress(service);
            var directPorts = service.ServicePorts
                .Where(port => !usesIngress || !IsComposeIngressPort(port))
                .ToArray();
            builder.AppendLine($"  {serviceName}:");

            if (workload.Kind == ResourceWorkloadKind.ContainerImage)
            {
                builder.AppendLine($"    image: {QuoteYaml(CreateRegistryImageReference(workload.Registry, workload.Image ?? serviceName))}");
            }
            else
            {
                builder.AppendLine("    build:");
                builder.AppendLine($"      context: {QuoteYaml(workload.BuildContext ?? ".")}");
                if (!string.IsNullOrWhiteSpace(workload.Dockerfile))
                {
                    builder.AppendLine($"      dockerfile: {QuoteYaml(workload.Dockerfile)}");
                }
            }

            if (workload.WorkloadEnvironmentVariables.Count > 0)
            {
                builder.AppendLine("    environment:");
                foreach (var variable in workload.WorkloadEnvironmentVariables)
                {
                    builder.AppendLine($"      {variable.Name}: {QuoteYaml(variable.Value)}");
                }
            }

            if (usesIngress)
            {
                builder.AppendLine("    labels:");
                builder.AppendLine("      - \"traefik.enable=true\"");
                foreach (var port in service.ServicePorts.Where(IsComposeIngressPort))
                {
                    foreach (var label in CreateComposeIngressLabels(service, port))
                    {
                        builder.AppendLine($"      - {QuoteYaml(label)}");
                    }
                }
            }

            if (service.ServiceDependencies.Count > 0)
            {
                builder.AppendLine("    depends_on:");
                foreach (var dependency in service.ServiceDependencies.Select(ToComposeServiceName))
                {
                    builder.AppendLine($"      - {dependency}");
                }
            }

            if (directPorts.Length > 0)
            {
                builder.AppendLine("    ports:");
                foreach (var port in directPorts)
                {
                    builder.AppendLine("      - target: " + port.TargetPort);
                    if (port.Port is not null)
                    {
                        builder.AppendLine("        published: " + port.Port.Value);
                    }

                    builder.AppendLine($"        protocol: {QuoteYaml(port.Protocol)}");
                }
            }

            if (service.ServiceNetworks.Count > 0)
            {
                builder.AppendLine("    networks:");
                foreach (var network in service.ServiceNetworks)
                {
                    builder.AppendLine($"      - {ToComposeServiceName(network)}");
                }
            }

            if (service.Replicas > 1)
            {
                builder.AppendLine("    deploy:");
                builder.AppendLine($"      replicas: {service.Replicas}");
            }
        }

        foreach (var service in orchestratorServices
                     .Where(ShouldUseComposeIngress)
                     .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase))
        {
            RenderComposeIngressService(builder, service);
        }

        var networkIds = networkDefinitions
            .Select(network => network.Id)
            .Concat(platformServices.SelectMany(service => service.NetworkIds))
            .Concat(orchestratorServices.SelectMany(service => service.ServiceNetworks))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ToComposeServiceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (networkIds.Length > 0)
        {
            builder.AppendLine("networks:");
            foreach (var networkId in networkIds)
            {
                builder.AppendLine($"  {ToComposeServiceName(networkId)}: {{}}");
            }
        }

        return builder.ToString();
    }

    private void RenderComposeIngressService(
        StringBuilder builder,
        ResourceOrchestratorService service)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsComposeIngressPort)
            .ToArray();
        var ingressServiceName = $"{service.Name}-ingress";

        builder.AppendLine($"  {ingressServiceName}:");
        builder.AppendLine($"    image: {QuoteYaml(options.ReplicatedContainerAppIngressImage)}");
        builder.AppendLine("    command:");
        builder.AppendLine("      - \"--providers.docker=true\"");
        builder.AppendLine("      - \"--providers.docker.exposedbydefault=false\"");
        foreach (var port in ingressPorts)
        {
            builder.AppendLine($"      - {QuoteYaml($"--entrypoints.{CreateComposeIngressEntrypoint(service, port)}.address=:{ResolveComposePublishedPort(port).ToString(CultureInfo.InvariantCulture)}")}");
        }

        builder.AppendLine("    ports:");
        foreach (var port in ingressPorts)
        {
            var publishedPort = ResolveComposePublishedPort(port);
            builder.AppendLine("      - target: " + publishedPort.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("        published: " + publishedPort.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("        protocol: \"tcp\"");
        }

        builder.AppendLine("    volumes:");
        builder.AppendLine("      - \"/var/run/docker.sock:/var/run/docker.sock:ro\"");

        if (service.ServiceNetworks.Count > 0)
        {
            builder.AppendLine("    networks:");
            foreach (var network in service.ServiceNetworks)
            {
                builder.AppendLine($"      - {ToComposeServiceName(network)}");
            }
        }
    }

    private bool ShouldUseComposeIngress(ResourceOrchestratorService service) =>
        options.EnableReplicatedContainerAppIngress &&
        service.Replicas > 1 &&
        service.ServicePorts.Any(IsComposeIngressPort);

    private static bool IsComposeIngressPort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "tcp";

    private static IEnumerable<string> CreateComposeIngressLabels(
        ResourceOrchestratorService service,
        ServicePort port)
    {
        var routeId = CreateComposeIngressRouteId(service, port);
        var entrypoint = CreateComposeIngressEntrypoint(service, port);
        if (NormalizeProtocol(port.Protocol) == "tcp")
        {
            yield return $"traefik.tcp.routers.{routeId}.rule=HostSNI(`*`)";
            yield return $"traefik.tcp.routers.{routeId}.entrypoints={entrypoint}";
            yield return $"traefik.tcp.routers.{routeId}.service={routeId}";
            yield return $"traefik.tcp.services.{routeId}.loadbalancer.server.port={port.TargetPort.ToString(CultureInfo.InvariantCulture)}";
            yield break;
        }

        yield return $"traefik.http.routers.{routeId}.rule=PathPrefix(`/`)";
        yield return $"traefik.http.routers.{routeId}.entrypoints={entrypoint}";
        yield return $"traefik.http.routers.{routeId}.service={routeId}";
        yield return $"traefik.http.services.{routeId}.loadbalancer.server.port={port.TargetPort.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string CreateComposeIngressEntrypoint(
        ResourceOrchestratorService service,
        ServicePort port) =>
        CreateStableIdentifier($"{service.Name}-{port.Name}-{ResolveComposePublishedPort(port).ToString(CultureInfo.InvariantCulture)}");

    private static string CreateComposeIngressRouteId(
        ResourceOrchestratorService service,
        ServicePort port) =>
        CreateStableIdentifier($"{service.Name}-{port.Name}-{port.TargetPort.ToString(CultureInfo.InvariantCulture)}");

    private static int ResolveComposePublishedPort(ServicePort port) =>
        port.Port ?? port.TargetPort;

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string CreateStableIdentifier(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var identifier = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(identifier) ? "cloudshell" : identifier;
    }

    private static string CreateRegistryImageReference(string? registry, string image)
    {
        var normalizedRegistry = string.IsNullOrWhiteSpace(registry)
            ? ContainerRegistryDefaults.Local
            : registry.Trim();
        var imageRegistry = GetImageRegistryAddress(normalizedRegistry);
        if (image.StartsWith($"{imageRegistry}/", StringComparison.OrdinalIgnoreCase))
        {
            return image;
        }

        return $"{imageRegistry}/{image}";
    }

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private async Task<string> ResolveComposeServiceNameAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        if (context.Resource.EffectiveTypeId.Equals("cloudshell.service", StringComparison.OrdinalIgnoreCase))
        {
            var descriptorProvider = descriptorProviders.FirstOrDefault(provider =>
                provider.CanDescribe(context.Resource));
            if (descriptorProvider is not null)
            {
                var descriptor = await descriptorProvider.DescribeAsync(
                    context.Resource,
                    new ResourceOrchestrationDescriptorContext(
                        context.Registration,
                        context.ResourceGroup,
                        context.ResourceManager),
                    cancellationToken);
                var service = TryReadService(descriptor);
                var firstTarget = service?.Targets.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstTarget?.ResourceId))
                {
                    return ToComposeServiceName(firstTarget.ResourceId);
                }
            }
        }

        return ToComposeServiceName(context.Resource);
    }

    private static string ToComposeServiceName(Resource resource)
    {
        var candidate = resource.Id.Contains(':', StringComparison.Ordinal)
            ? resource.Id[(resource.Id.LastIndexOf(':') + 1)..]
            : resource.Name;
        var normalized = ComposeServiceNamePattern()
            .Replace(candidate.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized)
            ? resource.Name.Trim()
            : normalized;
    }

    private static string ToComposeServiceName(string resourceId)
    {
        var candidate = resourceId.Contains(':', StringComparison.Ordinal)
            ? resourceId[(resourceId.LastIndexOf(':') + 1)..]
            : resourceId;
        var normalized = ComposeServiceNamePattern()
            .Replace(candidate.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(normalized)
            ? candidate.Trim()
            : normalized;
    }

    private static string QuoteYaml(string value) =>
        JsonSerializer.Serialize(value);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    [GeneratedRegex("[^a-z0-9_.-]+")]
    private static partial Regex ComposeServiceNamePattern();
}
