using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.DockerCompose;

public sealed partial class DockerComposeResourceOrchestrator(
    DockerComposeOrchestratorOptions options,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IEnumerable<IContainerEngineProvider> containerEngineProviders) : IResourceOrchestrator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IContainerEngineProvider> containerEngineProviders =
        containerEngineProviders.ToArray();

    public string Id => "docker-compose";

    public string DisplayName => "Docker Compose";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        options.Enabled &&
        HasComposeConfiguration(context) &&
        action.Kind is ResourceActionKind.Run or ResourceActionKind.Stop or ResourceActionKind.Restart;

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
            ResourceActionKind.Run => ["compose", .. GetBaseArguments(), "up", "-d", serviceName],
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
        var selectedEngineId = FirstNonEmpty(
            workload.ContainerEngineId,
            options.ContainerEngineId,
            context.PreferredContainerEngineId);
        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            var selected = await ResolveContainerEngineAsync(selectedEngineId, context, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Container engine '{selectedEngineId}' is not registered.");
            return selected.Endpoint;
        }

        var defaultEngine = await ResolveDefaultContainerEngineAsync(context, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Resource '{context.Resource.Name}' is container-backed but no default container engine is registered. Use UseDocker(), UseContainerEngine(...), or set WithContainerEngine(...).");
        return defaultEngine.Endpoint;
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

        var services = descriptors
            .Select(TryReadService)
            .Where(service => service is not null)
            .Select(service => service!)
            .ToArray();
        var networks = descriptors
            .Select(TryReadNetwork)
            .Where(network => network is not null)
            .Select(network => network!)
            .ToArray();

        return RenderComposeDocument(workloads, services, networks);
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

    private async Task<ContainerEngineResourceDefinition?> ResolveContainerEngineAsync(
        string engineId,
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerEngines()
            .FirstOrDefault(engine => string.Equals(engine.Id, engineId, StringComparison.OrdinalIgnoreCase));
        if (engine is not null)
        {
            return engine;
        }

        var resource = context.ResourceManager.GetResource(engineId);
        if (resource is null)
        {
            return null;
        }

        var descriptor = await TryDescribeAsync(resource, context, cancellationToken);
        return descriptor is null ? null : TryReadContainerEngine(descriptor);
    }

    private async Task<ContainerEngineResourceDefinition?> ResolveDefaultContainerEngineAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerEngines()
            .Where(engine => engine.IsDefault)
            .OrderBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (engine is not null)
        {
            return engine;
        }

        foreach (var resource in context.ResourceManager.GetResources())
        {
            var descriptor = await TryDescribeAsync(resource, context, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            engine = TryReadContainerEngine(descriptor);
            if (engine?.IsDefault == true)
            {
                return engine;
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
        if (!descriptor.ResourceType.Equals("cloudshell.network", StringComparison.OrdinalIgnoreCase))
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

    private static ContainerEngineResourceDefinition? TryReadContainerEngine(
        ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals(ContainerEngineResourceTypes.ContainerEngine, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ContainerEngineResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string RenderComposeDocument(
        IReadOnlyDictionary<string, (ResourceOrchestrationDescriptor Descriptor, ResourceWorkloadConfiguration? Workload)> workloads,
        IReadOnlyList<ServiceResourceDefinition> serviceDefinitions,
        IReadOnlyList<NetworkResourceDefinition> networkDefinitions)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"name: {QuoteYaml(string.IsNullOrWhiteSpace(options.ProjectName) ? "cloudshell" : options.ProjectName.Trim())}");
        builder.AppendLine("services:");

        foreach (var (resourceId, item) in workloads.OrderBy(item => ToComposeServiceName(item.Key), StringComparer.OrdinalIgnoreCase))
        {
            var workload = item.Workload!;
            var serviceName = ToComposeServiceName(resourceId);
            builder.AppendLine($"  {serviceName}:");

            if (workload.Kind == ResourceWorkloadKind.ContainerImage)
            {
                builder.AppendLine($"    image: {QuoteYaml(workload.Image ?? serviceName)}");
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

            var dependsOn = item.Descriptor.DependsOn
                .Where(workloads.ContainsKey)
                .Select(ToComposeServiceName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (dependsOn.Length > 0)
            {
                builder.AppendLine("    depends_on:");
                foreach (var dependency in dependsOn)
                {
                    builder.AppendLine($"      - {dependency}");
                }
            }

            var serviceDefinitionsForWorkload = serviceDefinitions
                .Where(service => service.Targets.Any(target =>
                    string.Equals(target.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var ports = serviceDefinitionsForWorkload
                .SelectMany(service => service.Ports)
                .Concat(workload.WorkloadPorts)
                .GroupBy(
                    port => $"{port.Name}:{port.TargetPort}:{port.Port?.ToString() ?? string.Empty}:{port.Protocol}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
            if (ports.Length > 0)
            {
                builder.AppendLine("    ports:");
                foreach (var port in ports)
                {
                    builder.AppendLine("      - target: " + port.TargetPort);
                    if (port.Port is not null)
                    {
                        builder.AppendLine("        published: " + port.Port.Value);
                    }

                    builder.AppendLine($"        protocol: {QuoteYaml(port.Protocol)}");
                }
            }

            var networks = serviceDefinitionsForWorkload
                .SelectMany(service => service.NetworkIds)
                .Concat(item.Descriptor.Networks)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (networks.Length > 0)
            {
                builder.AppendLine("    networks:");
                foreach (var network in networks)
                {
                    builder.AppendLine($"      - {ToComposeServiceName(network)}");
                }
            }

            if (workload.Replicas > 1)
            {
                builder.AppendLine("    deploy:");
                builder.AppendLine($"      replicas: {workload.Replicas}");
            }
        }

        var networkIds = networkDefinitions
            .Select(network => network.Id)
            .Concat(serviceDefinitions.SelectMany(service => service.NetworkIds))
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

    private IReadOnlyList<ContainerEngineResourceDefinition> GetContainerEngines() =>
        containerEngineProviders
            .Select(provider => provider.GetContainerEngine())
            .Where(engine => !string.IsNullOrWhiteSpace(engine.Id))
            .GroupBy(engine => engine.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    [GeneratedRegex("[^a-z0-9_.-]+")]
    private static partial Regex ComposeServiceNamePattern();
}
