using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationContainerImageMaterializer(
    ApplicationContainerHostResolver containerHosts,
    ApplicationContainerProcessTracker containerProcesses,
    ILoggerFactory? loggerFactory = null)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ApplicationResourceDefinition>>> _materializations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger dockerHostLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;

    public async Task<ApplicationResourceDefinition> MaterializeAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null,
        bool cacheMaterialization = false)
    {
        if (!string.IsNullOrWhiteSpace(definition.ContainerImage))
        {
            return definition;
        }

        if (!definition.ProjectContainerBuild &&
            string.IsNullOrWhiteSpace(definition.ContainerBuildContext))
        {
            return definition;
        }

        if (resourceManager is null)
        {
            throw new InvalidOperationException(
                $"Container resource '{definition.Name}' requires resource manager context to resolve a container host.");
        }

        if (!cacheMaterialization)
        {
            return await MaterializeCoreAsync(
                definition,
                resourceManager,
                preferredContainerHostId,
                cancellationToken,
                procedureContext);
        }

        var key = CreateMaterializationKey(definition);
        var materialization = _materializations.GetOrAdd(
            key,
            _ => new Lazy<Task<ApplicationResourceDefinition>>(
                () => MaterializeCoreAsync(
                    definition,
                    resourceManager,
                    preferredContainerHostId,
                    cancellationToken,
                    procedureContext),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await materialization.Value;
        }
        catch
        {
            RemoveMaterialization(key);
            throw;
        }
    }

    public void RemoveMaterialization(ApplicationResourceDefinition definition) =>
        RemoveMaterialization(CreateMaterializationKey(definition));

    private async Task<ApplicationResourceDefinition> MaterializeCoreAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        if (string.IsNullOrWhiteSpace(definition.ProjectPath) &&
            string.IsNullOrWhiteSpace(definition.ContainerDockerfile))
        {
            throw new InvalidOperationException(
                $"Container resource '{definition.Name}' cannot be built because it does not specify a project path or Dockerfile.");
        }

        var engine = await ResolveRequiredContainerHostAsync(
            definition,
            resourceManager,
            preferredContainerHostId,
            ContainerHostCapabilityIds.ContainerBuild,
            cancellationToken);
        var log = containerProcesses.GetProcessLog(definition.Id);
        var imageReference = CreateProjectContainerImageReference(definition);

        procedureContext?.AppendProviderEvent(
            ApplicationResourceProviderIds.Applications,
            "application.container.image.building",
            $"Application provider is building project container image '{imageReference.Reference}' for '{definition.Name}' using '{engine.Name}'.");
        if (string.IsNullOrWhiteSpace(definition.ContainerDockerfile))
        {
            if (string.IsNullOrWhiteSpace(definition.ProjectPath))
            {
                throw new InvalidOperationException(
                    $"Container resource '{definition.Name}' cannot be published as a project container because it does not specify a project path.");
            }

            await PublishProjectContainerImageAsync(
                definition,
                imageReference.Repository,
                imageReference.Tag,
                log,
                cancellationToken);
        }
        else
        {
            var buildContext = NormalizeNullable(definition.ContainerBuildContext) ??
                Path.GetDirectoryName(definition.ProjectPath) ??
                ".";
            await BuildDockerfileContainerImageAsync(
                engine,
                imageReference.Reference,
                buildContext,
                definition.ContainerDockerfile,
                log,
                cancellationToken,
                dockerHostLogger);
        }

        procedureContext?.AppendProviderEvent(
            ApplicationResourceProviderIds.Applications,
            "application.container.image.built",
            $"Application provider built project container image '{imageReference.Reference}' for '{definition.Name}'.");
        return definition with
        {
            ContainerImage = imageReference.Reference
        };
    }

    private async Task<ContainerHostDescriptor> ResolveRequiredContainerHostAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore resourceManager,
        string? preferredContainerHostId,
        string requiredCapability,
        CancellationToken cancellationToken) =>
        await containerHosts.ResolveAsync(
            definition.ContainerHostId,
            preferredContainerHostId,
            resourceManager,
            requiredCapability,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Resource '{definition.Name}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set WithContainerHost(...).");

    private async Task PublishProjectContainerImageAsync(
        ApplicationResourceDefinition definition,
        string repository,
        string tag,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(definition.ProjectPath!);
        startInfo.ArgumentList.Add("--os");
        startInfo.ArgumentList.Add("linux");
        startInfo.ArgumentList.Add("--arch");
        startInfo.ArgumentList.Add("x64");
        startInfo.ArgumentList.Add("/t:PublishContainer");
        startInfo.ArgumentList.Add($"-p:ContainerRepository={repository}");
        startInfo.ArgumentList.Add($"-p:ContainerImageTag={tag}");

        var registry = GetImageRegistryAddress(GetEffectiveContainerRegistry(definition));
        if (!IsDockerHubRegistry(registry))
        {
            startInfo.ArgumentList.Add($"-p:ContainerRegistry={registry}");
        }

        log.Append(
            $"Publishing project '{definition.ProjectPath}' as container image '{CreateProjectContainerImageReference(definition).Reference}'.",
            "process",
            "Information");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Project container publish could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await WaitForProcessExitOrKillAsync(process, cancellationToken);
        var output = await outputTask;
        var error = await errorTask;

        if (!string.IsNullOrWhiteSpace(output))
        {
            log.Append(output.Trim(), "process", process.ExitCode == 0 ? "Information" : "Warning");
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            log.Append(error.Trim(), "process", process.ExitCode == 0 ? "Information" : "Warning");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Project container publish failed for '{definition.Name}' with exit code {process.ExitCode.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static async Task BuildDockerfileContainerImageAsync(
        ContainerHostDescriptor engine,
        string imageReference,
        string buildContext,
        string dockerfile,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        log.Append(
            $"Building Dockerfile '{dockerfile}' as container image '{imageReference}'.",
            "process",
            "Information");
        var exitCode = await ApplicationContainerHostCommands.RunAsync(
            engine,
            ["build", "-t", imageReference, "-f", dockerfile, buildContext],
            log,
            cancellationToken,
            logger);
        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"Dockerfile build failed with exit code {exitCode.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    private static string CreateMaterializationKey(ApplicationResourceDefinition definition) =>
        string.Join(
            '|',
            definition.Id,
            definition.ContainerRevision,
            definition.ContainerRegistry,
            definition.ContainerHostId,
            definition.ProjectPath,
            definition.ContainerBuildContext,
            definition.ContainerDockerfile);

    private void RemoveMaterialization(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _materializations.TryRemove(key, out _);
        }
    }

    internal static ProjectContainerImageReference CreateProjectContainerImageReference(
        ApplicationResourceDefinition definition)
    {
        var repository = ApplicationContainerOrchestratorDeploymentFactory.CreateServiceName(definition.Id);
        var tag = GetEffectiveContainerRevision(definition);
        var registry = GetImageRegistryAddress(GetEffectiveContainerRegistry(definition));
        var reference = IsDockerHubRegistry(registry)
            ? $"{repository}:{tag}"
            : $"{registry}/{repository}:{tag}";
        return new ProjectContainerImageReference(reference, repository, tag);
    }

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        NormalizeNullable(application.ContainerRegistry) ?? ContainerRegistryDefaults.Default;

    private static string GetEffectiveContainerRevision(ApplicationResourceDefinition definition) =>
        NormalizeNullable(definition.ContainerRevision) ?? "latest";

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Authority)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private static bool IsDockerHubRegistry(string registry) =>
        string.Equals(registry, ContainerRegistryDefaults.DockerHub, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(registry, "index.docker.io", StringComparison.OrdinalIgnoreCase);

    private static async Task WaitForProcessExitOrKillAsync(
        Process process,
        CancellationToken cancellationToken,
        ILogger? logger = null,
        string? command = null)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger?.LogDebug(
                "Killing canceled process {ProcessId} for command {ProcessCommandLine}.",
                process.Id,
                command ?? "unknown");
            ProcessShutdown.KillProcessTreeAndWait(process);
            logger?.LogDebug(
                "Killed canceled process {ProcessId} for command {ProcessCommandLine}.",
                process.Id,
                command ?? "unknown");
            throw;
        }
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal sealed record ProjectContainerImageReference(
        string Reference,
        string Repository,
        string Tag);
}
