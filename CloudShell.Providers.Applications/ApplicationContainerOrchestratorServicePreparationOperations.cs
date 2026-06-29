using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace CloudShell.Providers.Applications;

internal sealed class ApplicationContainerOrchestratorServicePreparationOperations(
    ApplicationResourceStore store,
    ApplicationContainerHostResolver containerHosts,
    ApplicationContainerProcessTracker containerProcesses,
    ILoggerFactory? loggerFactory = null,
    ContainerApplicationIngressOperations? ingress = null) :
    IApplicationContainerOrchestratorServicePreparationOperations
{
    private readonly ILogger dockerHostLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;
    private readonly ContainerApplicationIngressOperations _ingress =
        ingress ?? throw new ArgumentNullException(nameof(ingress));

    public async Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var application = GetContainerBackedApplication(context.ResourceContext.Resource.Id);

        if (action.Kind is ResourceActionKind.Stop && _ingress.ShouldUseIngress(context.Service))
        {
            var stopEngine = await ResolveRequiredContainerHostAsync(
                application,
                context.ResourceContext.ResourceManager,
                context.ResourceContext.PreferredContainerHostId,
                ContainerHostCapabilityIds.ContainerImage,
                cancellationToken);
            await _ingress.StopAsync(
                application,
                context.Service,
                stopEngine,
                containerProcesses.GetProcessLog(application.Id),
                ApplicationResourceProviderIds.Applications,
                cancellationToken,
                context.ResourceContext);
            return;
        }

        if (action.Kind != ResourceActionKind.Start)
        {
            return;
        }

        var engine = await ResolveRequiredContainerHostAsync(
            application,
            context.ResourceContext.ResourceManager,
            context.ResourceContext.PreferredContainerHostId,
            ContainerHostCapabilityIds.ContainerImage,
            cancellationToken);
        var processLog = containerProcesses.GetProcessLog(application.Id);

        await LoginToContainerRegistryAsync(
            engine,
            GetEffectiveContainerRegistry(application),
            application.ContainerRegistryCredentials,
            processLog,
            cancellationToken,
            dockerHostLogger);

        foreach (var network in context.Service.ServiceNetworks
                     .Where(network => !string.IsNullOrWhiteSpace(network))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await EnsureContainerNetworkAsync(
                engine,
                network,
                processLog,
                cancellationToken,
                dockerHostLogger);
        }
    }

    private ApplicationResourceDefinition GetContainerBackedApplication(string resourceId)
    {
        var application = store.GetApplication(resourceId)
            ?? throw new InvalidOperationException(
                $"Container resource '{resourceId}' is not configured.");
        if (!ApplicationResourceProjectionSupport.IsContainerBacked(application))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is not container-backed.");
        }

        return application;
    }

    private async Task<ContainerHostDescriptor> ResolveRequiredContainerHostAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        string requiredCapability,
        CancellationToken cancellationToken)
    {
        if (resourceManager is null)
        {
            throw new InvalidOperationException(
                $"Container resource '{definition.Name}' requires resource manager context to resolve a container host.");
        }

        return await containerHosts.ResolveAsync(
            definition.ContainerHostId,
            preferredContainerHostId,
            resourceManager,
            requiredCapability,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Resource '{definition.Name}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set WithContainerHost(...).");
    }

    private static async Task EnsureContainerNetworkAsync(
        ContainerHostDescriptor engine,
        string network,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        await ApplicationContainerHostCommands.RunAsync(
            engine,
            ["network", "create", network],
            log,
            cancellationToken,
            logger);
    }

    private static async Task LoginToContainerRegistryAsync(
        ContainerHostDescriptor engine,
        string registry,
        ContainerRegistryCredentials? credentials,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;
        credentials = ContainerRegistryCredentials.Normalize(credentials);
        if (credentials is null)
        {
            return;
        }

        var registryAddress = GetImageRegistryAddress(registry);
        var password = credentials.ResolvePassword();
        var startInfo = new ProcessStartInfo
        {
            FileName = ApplicationContainerHostCommands.GetExecutable(engine),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ApplicationContainerHostCommands.ConfigureEnvironment(startInfo, engine);
        startInfo.ArgumentList.Add("login");
        startInfo.ArgumentList.Add(registryAddress);
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(credentials.Username);
        startInfo.ArgumentList.Add("--password-stdin");
        var command = "login";
        var commandLine = ApplicationContainerHostCommands.FormatCommandLine(
            startInfo.ArgumentList.Select(argument => argument).ToArray());

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Container registry login could not be started.");
        try
        {
            ApplicationContainerHostCommands.LogStarted(logger, process, engine, command, commandLine);
            await process.StandardInput.WriteLineAsync(password.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await ApplicationContainerHostCommands.WaitForExitOrKillAsync(process, engine, cancellationToken, logger, command, commandLine);
            ApplicationContainerHostCommands.LogExited(logger, process, engine, command, commandLine);
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
                    $"Container registry login failed for '{registryAddress}'.");
            }
        }
        catch (OperationCanceledException)
        {
            ApplicationContainerHostCommands.KillIfRunning(logger, process, engine, command, commandLine);
            throw;
        }
        finally
        {
            ApplicationContainerHostCommands.LogReleased(logger, process, engine, command, commandLine);
            process.Dispose();
        }
    }

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        NormalizeNullable(application.ContainerRegistry) ?? ContainerRegistryDefaults.Default;

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Authority)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
