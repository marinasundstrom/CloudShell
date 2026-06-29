using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceRuntimeOperations(
    ApplicationResourceStore store,
    ApplicationRuntimeStateStore runtimeStates,
    ApplicationContainerDeploymentStore containerDeployments,
    LocalProcessRunner localProcesses,
    ApplicationProviderOptions options,
    IHostEnvironment environment,
    IServiceProvider serviceProvider,
    IEnumerable<IResourceIdentityCredentialEnvironmentProvider> identityCredentialEnvironmentProviders,
    IEnumerable<IResourceEnvironmentVariableProvider> environmentVariableProviders,
    ResourceDeclarationStore declarations,
    ApplicationContainerHostResolver? containerHosts = null,
    ILoggerFactory? loggerFactory = null,
    ApplicationResourceDefinitionNormalizer? definitionNormalizer = null,
    ApplicationResourceDefinitionRegistrationService? applicationDefinitionRegistrations = null,
    ApplicationWorkloadConfigurationProvider? workloadConfigurations = null,
    ApplicationResourceSettingResolver? settingResolver = null,
    ApplicationResourceEnvironmentVariableResolver? environmentVariables = null,
    IApplicationContainerOrchestratorServicePreparationOperations? containerServicePreparation = null) :
    IApplicationResourceProcedureOperations,
    IContainerApplicationOrchestrationOperations,
    IDisposable
{
    private static readonly TimeSpan StartingStateTimeout = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim AspNetCoreProjectBuildLock = new(1, 1);
    private static readonly HttpClient ContainerReadinessHttpClient = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    public const string HiddenResourceEnvironmentVariable = "CloudShell__ResourceManager__Hidden";

    private readonly ILogger dockerHostLogger =
        loggerFactory?.CreateLogger(CloudShellLogCategories.DockerHostLifecycle) ??
        NullLogger.Instance;
    private readonly ApplicationResourceDefinitionNormalizer _definitionNormalizer =
        definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment);
    private readonly ApplicationResourceDefinitionRegistrationService _applicationDefinitionRegistrations =
        applicationDefinitionRegistrations ?? new ApplicationResourceDefinitionRegistrationService(
            store,
            definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment));
    private readonly ApplicationContainerHostResolver _containerHosts =
        containerHosts ?? new ApplicationContainerHostResolver(serviceProvider);
    private readonly ApplicationResourcePortResolver _ports = new(options);
    private readonly ApplicationResourceSettingResolver _settingResolver =
        settingResolver ?? serviceProvider.GetService<ApplicationResourceSettingResolver>() ?? new ApplicationResourceSettingResolver(
            declarations,
            serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
            serviceProvider.GetServices<ISecretReferenceResolver>());
    private readonly ApplicationResourceEnvironmentVariableResolver _environmentVariables =
        environmentVariables ?? serviceProvider.GetService<ApplicationResourceEnvironmentVariableResolver>() ?? new ApplicationResourceEnvironmentVariableResolver(
            options,
            declarations,
            settingResolver ?? serviceProvider.GetService<ApplicationResourceSettingResolver>() ?? new ApplicationResourceSettingResolver(
                declarations,
                serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
                serviceProvider.GetServices<ISecretReferenceResolver>()),
            identityCredentialEnvironmentProviders,
            environmentVariableProviders);
    private readonly ApplicationWorkloadConfigurationProvider _workloadConfigurations =
        workloadConfigurations ?? serviceProvider.GetService<ApplicationWorkloadConfigurationProvider>() ??
        new ApplicationWorkloadConfigurationProvider(
            environmentVariables ?? serviceProvider.GetService<ApplicationResourceEnvironmentVariableResolver>() ?? new ApplicationResourceEnvironmentVariableResolver(
                options,
                declarations,
                settingResolver ?? serviceProvider.GetService<ApplicationResourceSettingResolver>() ?? new ApplicationResourceSettingResolver(
                    declarations,
                    serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
                    serviceProvider.GetServices<ISecretReferenceResolver>()),
                identityCredentialEnvironmentProviders,
                environmentVariableProviders));
    private readonly IApplicationContainerOrchestratorServicePreparationOperations _containerServicePreparation =
        containerServicePreparation ?? serviceProvider.GetService<IApplicationContainerOrchestratorServicePreparationOperations>() ??
        new ApplicationContainerOrchestratorServicePreparationOperations(
            store,
            containerHosts ?? new ApplicationContainerHostResolver(serviceProvider),
            serviceProvider.GetService<ApplicationContainerProcessTracker>() ?? new ApplicationContainerProcessTracker(
                runtimeStates,
                options,
                environment,
                loggerFactory),
            loggerFactory,
            serviceProvider.GetService<ContainerApplicationIngressOperations>() ??
                new ContainerApplicationIngressOperations(options, environment, loggerFactory));
    private readonly ApplicationContainerProcessTracker _containerProcesses =
        serviceProvider.GetService<ApplicationContainerProcessTracker>() ?? new ApplicationContainerProcessTracker(
            runtimeStates,
            options,
            environment,
            loggerFactory);
    private readonly ApplicationContainerImageMaterializer _containerImageMaterializer =
        serviceProvider.GetService<ApplicationContainerImageMaterializer>() ??
        new ApplicationContainerImageMaterializer(
            containerHosts ?? new ApplicationContainerHostResolver(serviceProvider),
            serviceProvider.GetService<ApplicationContainerProcessTracker>() ?? new ApplicationContainerProcessTracker(
                runtimeStates,
                options,
                environment,
                loggerFactory),
            loggerFactory);
    private readonly ContainerApplicationIngressOperations _containerIngress =
        serviceProvider.GetService<ContainerApplicationIngressOperations>() ??
        new ContainerApplicationIngressOperations(options, environment, loggerFactory);
    private readonly ContainerApplicationContainerRunCommandFactory _containerRunCommands =
        serviceProvider.GetService<ContainerApplicationContainerRunCommandFactory>() ??
        new ContainerApplicationContainerRunCommandFactory(options);
    private readonly ApplicationResourceProjectionSource _projectionSource =
        serviceProvider.GetService<ApplicationResourceProjectionSource>() ?? new ApplicationResourceProjectionSource(
            new ApplicationResourceDefinitionSource(
                store,
                definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment)),
            runtimeStates,
            serviceProvider.GetService<IApplicationResourceRunningStateOperations>() ??
                new ApplicationResourceRunningStateOperations(
                    new ApplicationResourceDefinitionSource(
                        store,
                        definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment)),
                    localProcesses,
                    new ApplicationContainerProcessTracker(
                        runtimeStates,
                        options,
                        environment,
                        loggerFactory)),
            workloadConfigurations ?? serviceProvider.GetService<ApplicationWorkloadConfigurationProvider>() ??
                new ApplicationWorkloadConfigurationProvider(
                    environmentVariables ?? serviceProvider.GetService<ApplicationResourceEnvironmentVariableResolver>() ?? new ApplicationResourceEnvironmentVariableResolver(
                        options,
                        declarations,
                        settingResolver ?? serviceProvider.GetService<ApplicationResourceSettingResolver>() ?? new ApplicationResourceSettingResolver(
                            declarations,
                            serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
                            serviceProvider.GetServices<ISecretReferenceResolver>()),
                        identityCredentialEnvironmentProviders,
                        environmentVariableProviders)),
            containerDeployments,
            options);

    public string Id => ApplicationResourceProviderIds.Applications;

    public string DisplayName => "Applications";

    public IReadOnlyList<Resource> GetResources() =>
        _projectionSource.GetResources();

    internal IReadOnlyList<Resource> GetResources(ApplicationResourceProjection projection) =>
        _projectionSource.GetResources(projection);

    public Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        _applicationDefinitionRegistrations.SetupApplicationAsync(
            definition,
            resourceGroupId,
            registrations,
            cancellationToken);

    public Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default) =>
        _applicationDefinitionRegistrations.UpdateApplicationAsync(
            definition,
            resourceGroupId,
            registrations,
            cancellationToken);

    public async Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        await StopApplicationAsync(
            context.Resource.Id,
            force: true,
            context.ResourceManager,
            context.PreferredContainerHostId,
            cancellationToken);
        store.Remove(context.Resource.Id);
        runtimeStates.Remove(context.Resource.Id);
        containerDeployments.RemoveApplication(context.Resource.Id);
        await context.Registrations.RemoveAsync(context.Resource.Id, cancellationToken);
        return ResourceProcedureResult.Completed("Application registration removed.");
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (!ApplicationResourceTypes.IsApplication(context.Resource.EffectiveTypeId))
        {
            throw new InvalidOperationException(
                $"The application provider cannot execute action '{action.Id}' on resource '{context.Resource.Id}'.");
        }

        switch (action.Kind)
        {
            case ResourceActionKind.Custom when string.Equals(
                action.Id,
                ApplicationResourceActionIds.ReconcileSqlServerAccess,
                StringComparison.OrdinalIgnoreCase):
                var count = await ReconcileSqlServerDatabasesAsync(
                    context.Resource.Id,
                    cancellationToken,
                    context);
                return ResourceProcedureResult.Completed(
                    count == 0
                        ? "No SQL Server database grants are currently declared. Provider-owned SQL access artifacts were reconciled."
                        : $"Reconciled {count.ToString(CultureInfo.InvariantCulture)} SQL Server database grant{Pluralize(count)}.");
            case ResourceActionKind.Start:
                await StartApplicationAsync(
                    context.Resource.Id,
                    context.Resource.DependsOn,
                    context.ResourceGroupId,
                    context.Registrations,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken,
                    context);
                return ResourceProcedureResult.Completed($"Started {context.Resource.Name}.");
            case ResourceActionKind.Stop:
                await StopApplicationAsync(
                    context.Resource.Id,
                    force: true,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken,
                    context);
                return ResourceProcedureResult.Completed($"Stopped {context.Resource.Name}.");
            case ResourceActionKind.Restart:
                await StopApplicationAsync(
                    context.Resource.Id,
                    force: true,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken,
                    context);
                await StartApplicationAsync(
                    context.Resource.Id,
                    context.Resource.DependsOn,
                    context.ResourceGroupId,
                    context.Registrations,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken,
                    context);
                return ResourceProcedureResult.Completed($"Restarted {context.Resource.Name}.");
            default:
                throw new NotSupportedException(
                    $"Applications do not support action '{action.DisplayName}'.");
        }
    }

    public async Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var application = GetContainerApplication(context.ResourceContext.Resource.Id);
        switch (action.Kind)
        {
            case ResourceActionKind.Start:
                application = await MaterializeContainerImageAsync(
                    application,
                    context.ResourceContext.ResourceManager,
                    context.ResourceContext.PreferredContainerHostId,
                    cancellationToken,
                    context.ResourceContext,
                    cacheMaterialization: context.Instance.ReplicaCount > 1);
                try
                {
                    await StartContainerApplicationInstanceAsync(
                        application,
                        context.ResourceContext.Resource.DependsOn,
                        context.ResourceContext.ResourceGroupId,
                        context.ResourceContext.Registrations,
                        context.ResourceContext.ResourceManager,
                        context.ResourceContext.PreferredContainerHostId,
                        context.Service,
                        context.Instance,
                        cancellationToken);
                }
                catch
                {
                    _containerImageMaterializer.RemoveMaterialization(application);
                    throw;
                }
                finally
                {
                    if (context.Instance.ReplicaOrdinal >= context.Instance.ReplicaCount)
                    {
                        _containerImageMaterializer.RemoveMaterialization(application);
                    }
                }
                return;
            case ResourceActionKind.Stop:
                if (context.Instance.ReplicaOrdinal == 1)
                {
                    MarkStopping(application.Id);
                }

                await StopContainerApplicationInstanceAsync(
                    application,
                    context.ResourceContext.ResourceManager,
                    context.ResourceContext.PreferredContainerHostId,
                    context.Instance,
                    removeContainer: true,
                    cancellationToken);
                if (context.Instance.ReplicaOrdinal >= context.Instance.ReplicaCount)
                {
                    CompleteContainerApplicationStop(
                        application,
                        application.Id,
                        force: true,
                        _containerProcesses.GetProcessLog(application.Id),
                        cancellationToken,
                        context.ResourceContext);
                }

                return;
            default:
                throw new NotSupportedException(
                    $"Container app services do not support action '{action.DisplayName}'.");
        }
    }

    public async Task ReconcileOrchestratorServiceRoutingAsync(
        ResourceOrchestratorServiceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        if (!_containerIngress.ShouldUseIngress(context.Service))
        {
            return;
        }

        var application = GetContainerApplication(context.ResourceContext.Resource.Id);
        var engine = await ResolveRequiredContainerHostAsync(
            application,
            context.ResourceContext.ResourceManager,
            context.ResourceContext.PreferredContainerHostId,
            ContainerHostCapabilityIds.ContainerImage,
            cancellationToken);
        await _containerIngress.StartAsync(
            application,
            engine,
            context.Service,
            _containerProcesses.GetProcessLog(application.Id),
            Id,
            cancellationToken,
            context.ResourceContext);
    }

    public bool IsRunning(string applicationId) =>
        GetApplication(applicationId) is { } application &&
        (IsContainerBacked(application)
            ? _containerProcesses.IsRunning(application)
            : localProcesses.IsRunning(ApplicationProcessDefinitions.Create(application)));

    public void Dispose() =>
        _containerProcesses.Dispose();

    private async Task StartApplicationAsync(
        string applicationId,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var definition = GetApplication(applicationId)
            ?? throw new InvalidOperationException($"Application resource '{applicationId}' is not configured.");

        procedureContext?.AppendProviderEvent(
            Id,
            "application.start.preparing",
            $"Application provider is preparing to start '{definition.Name}' ({definition.ResourceType}) with {definition.Lifetime} lifetime.");

        if (IsContainerBacked(definition))
        {
            if (_containerProcesses.IsRunning(definition))
            {
                procedureContext?.AppendProviderEvent(
                    Id,
                    "application.start.skipped",
                    $"Application provider skipped start for '{definition.Name}' because it is already running.");
                return;
            }

            if (resourceManager is null)
            {
                throw new InvalidOperationException(
                    $"Container resource '{definition.Name}' requires resource manager context to resolve a container host.");
            }

            MarkStarting(definition.Id);
            try
            {
                await StartContainerApplicationAsync(
                    definition,
                    dependsOn,
                    resourceGroupId,
                    registrations,
                    resourceManager,
                    preferredContainerHostId,
                    cancellationToken,
                    procedureContext);
            }
            catch
            {
                ClearStarting(definition);
                throw;
            }
            return;
        }

        procedureContext?.AppendProviderEvent(
            Id,
            "application.environment.resolving",
            $"Application provider is resolving environment variables for '{definition.Name}'.");
        var resolvedEnvironmentVariables = await ResolveLocalProcessEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            resourceManager,
            cancellationToken);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.environment.resolved",
            $"Application provider resolved {resolvedEnvironmentVariables.Count.ToString(CultureInfo.InvariantCulture)} environment variable{Pluralize(resolvedEnvironmentVariables.Count)} for '{definition.Name}'.");

        var localProcess = ApplicationProcessDefinitions.Create(
            definition,
            resolvedEnvironmentVariables);
        if (localProcesses.IsRunning(localProcess))
        {
            procedureContext?.AppendProviderEvent(
                Id,
                "application.start.skipped",
                $"Application provider skipped start for '{definition.Name}' because it is already running.");
            return;
        }

        var workingDirectory = ResolveConfiguredWorkingDirectory(definition);
        var volumeMaterializations = ApplicationResourceVolumeMounts.CreateLocalProcessVolumeMaterializations(
            definition.VolumeMounts,
            resourceManager,
            environment.ContentRootPath,
            workingDirectory);
        if (volumeMaterializations.Count > 0)
        {
            procedureContext?.AppendProviderEvent(
                Id,
                "application.process.volume.mounts.prepared",
                $"Application provider prepared {volumeMaterializations.Count.ToString(CultureInfo.InvariantCulture)} filesystem volume mount{Pluralize(volumeMaterializations.Count)} for '{definition.Name}'.");
        }

        localProcess = localProcess with
        {
            VolumeMounts = volumeMaterializations
        };

        MarkStarting(definition.Id);
        try
        {
            if (ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType) && !definition.AspNetCoreHotReload)
            {
                await BuildAspNetCoreProjectAsync(
                    definition,
                    procedureContext,
                    cancellationToken);
            }

            procedureContext?.AppendProviderEvent(
                Id,
                "application.process.starting",
                $"Application provider is starting local process for '{definition.Name}'.");
            await localProcesses.StartAsync(
                localProcess,
                cancellationToken);
            procedureContext?.AppendProviderEvent(
                Id,
                "application.process.started",
                $"Application provider started local process for '{definition.Name}'.");
        }
        catch
        {
            ClearStarting(definition);
            throw;
        }
    }

    private Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveLocalProcessEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceManagerStore? resourceManager,
        CancellationToken cancellationToken)
    {
        var providerRuntimeVariables = ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType)
            ? ResolveAspNetCoreProjectEnvironmentVariables(definition, resourceManager)
            : [];
        return _environmentVariables.ResolveRuntimeEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            resourceManager,
            providerRuntimeVariables,
            cancellationToken);
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveAspNetCoreProjectEnvironmentVariables(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager = null) =>
        CreateAspNetCoreProjectEnvironmentFactory().Create(definition, resourceManager);

    private IReadOnlyList<string> ResolveAspNetCoreProjectEndpointUrls(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager = null) =>
        CreateAspNetCoreProjectEnvironmentFactory().ResolveEndpointUrls(definition, resourceManager);

    private AspNetCoreProjectEnvironmentFactory CreateAspNetCoreProjectEnvironmentFactory() =>
        new(CreateEndpointNetworkMappings);

    private async Task StartContainerApplicationAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IResourceManagerStore resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var runtimeDefinition = await MaterializeContainerImageAsync(
            definition,
            resourceManager,
            preferredContainerHostId,
            cancellationToken,
            procedureContext);
        var service = CreateActiveContainerOrchestratorService(runtimeDefinition);
        var replicaGroup = CreateDefaultContainerReplicaGroup(service);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.service.preparing",
            $"Application provider is preparing container service '{service.Name}' for '{runtimeDefinition.Name}'.");
        await _containerServicePreparation.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                new ResourceProcedureContext(
                    CreateApplicationResourceProjector().CreateResource(
                        runtimeDefinition,
                        ApplicationResourceProjectionProfiles.CreateInfrastructureProjection(runtimeDefinition),
                        DisplayName),
                    null,
                    resourceGroupId,
                    registrations,
                    resourceManager,
                    preferredContainerHostId,
                    procedureContext?.TriggeredBy,
                    procedureContext?.Cause,
                    procedureContext?.ResourceEvents),
                service,
                replicaGroup),
            ResourceAction.Start,
            cancellationToken);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.service.prepared",
            $"Application provider prepared container service '{service.Name}' for '{runtimeDefinition.Name}'.");
        foreach (var instance in replicaGroup.Instances)
        {
            await StartContainerApplicationInstanceAsync(
                runtimeDefinition,
                dependsOn,
                resourceGroupId,
                registrations,
                resourceManager,
                preferredContainerHostId,
                service,
                instance,
                cancellationToken,
                procedureContext);
        }

        await ReconcileSqlServerDatabasesAsync(runtimeDefinition.Id, cancellationToken, procedureContext);
    }

    private Task<int> ReconcileSqlServerDatabasesAsync(
        string sqlServerResourceId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext) =>
        serviceProvider
            .GetRequiredService<SqlServerDatabaseReconciliationService>()
            .ReconcileAsync(
                sqlServerResourceId,
                Id,
                procedureContext,
                cancellationToken);

    private Task<ApplicationResourceDefinition> MaterializeContainerImageAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null,
        bool cacheMaterialization = false) =>
        _containerImageMaterializer.MaterializeAsync(
            definition,
            resourceManager,
            preferredContainerHostId,
            cancellationToken,
            procedureContext,
            cacheMaterialization);

    private async Task StartContainerApplicationInstanceAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        if (string.IsNullOrWhiteSpace(definition.ContainerImage))
        {
            throw new InvalidOperationException(
                $"Container resource '{definition.Name}' cannot be started by the default orchestrator because it does not specify a container image.");
        }

        var engine = await ResolveRequiredContainerHostAsync(
            definition,
            resourceManager,
            preferredContainerHostId,
            ContainerHostCapabilityIds.ContainerImage,
            cancellationToken);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.host.resolved",
            $"Application provider resolved container host '{engine.Name}' for '{definition.Name}'.");
        var processLog = _containerProcesses.CreateProcessLogForStart(definition.Id, out var logPath);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            procedureContext?.AppendProviderEvent(
                Id,
                "application.container.instance.cleanup",
                $"Application provider is removing any existing container replica '{instance.Name}' before start.");
            await ApplicationContainerHostCommands.RunAsync(
                engine,
                ["rm", "-f", instance.Name],
                processLog,
                cancellationToken,
                dockerHostLogger);
        }

        var useIngress = _containerIngress.ShouldUseIngress(service);
        var environmentVariables = await _environmentVariables.ResolveRuntimeEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            resourceManager,
            [],
            cancellationToken);
        environmentVariables = ApplyRuntimeContainerTelemetryScopeEnvironmentVariables(
            definition,
            instance,
            environmentVariables);
        var volumeMaterializations = ApplicationResourceVolumeMounts.CreateLocalContainerVolumeMaterializations(
            service.ServiceVolumeMounts,
            resourceManager,
            environment.ContentRootPath);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.volume.mounts.prepared",
            $"Application provider prepared {volumeMaterializations.Count.ToString(CultureInfo.InvariantCulture)} volume mount{Pluralize(volumeMaterializations.Count)} for '{definition.Name}'.");

        var imageReference = definition.ProjectContainerBuild
            ? definition.ContainerImage
            : CreateRegistryImageReference(
                GetEffectiveContainerRegistry(definition),
                definition.ContainerImage);
        var imagePlatform = await TryResolveContainerImagePlatformAsync(
            engine,
            imageReference,
            cancellationToken);

        var startInfo = _containerRunCommands.CreateStartInfo(
            engine,
            definition,
            service,
            instance,
            GetRuntimeContainerProbePorts(definition, service),
            environmentVariables,
            volumeMaterializations,
            imageReference,
            imagePlatform,
            useIngress);

        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stdout");
        process.ErrorDataReceived += (_, args) => processLog.Append(args.Data ?? string.Empty, "stderr", "Error");
        process.Exited += (_, _) =>
        {
            processLog.Append(
                $"Container replica '{instance.Name}' exited with code {process.ExitCode}.",
                "process",
                process.ExitCode == 0 ? "Information" : "Error");
            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                process.Id,
                null,
                DateTimeOffset.UtcNow,
                process.ExitCode,
                logPath,
                VolumeMounts: ApplicationContainerProcessTracker.MarkVolumeMountsNotActive(
                    volumeMaterializations.Select(mount => mount.RuntimeState),
                    DateTimeOffset.UtcNow)));
        };

        cancellationToken.ThrowIfCancellationRequested();
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.starting",
            $"Application provider is starting container replica '{instance.Name}' for '{definition.Name}'.");
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var earlyExitCode = await WaitForEarlyContainerHostExitAsync(
            process,
            options.ContainerStartConfirmationDelay,
            cancellationToken);
        if (earlyExitCode is not null)
        {
            process.WaitForExit();
            var now = DateTimeOffset.UtcNow;
            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                process.Id,
                null,
                now,
                earlyExitCode,
                logPath,
                VolumeMounts: ApplicationContainerProcessTracker.MarkVolumeMountsNotActive(
                    volumeMaterializations.Select(mount => mount.RuntimeState),
                    now)));
            var failureMessage = CreateContainerStartFailureMessage(
                processLog,
                instance.Name,
                earlyExitCode.Value);
            process.Dispose();
            throw new InvalidOperationException(failureMessage);
        }

        var startedAt = ApplicationContainerProcessTracker.TryGetStartTime(process);
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            process.Id,
            startedAt,
            DateTimeOffset.UtcNow,
            LogPath: logPath,
            VolumeMounts: volumeMaterializations
                .Select(mount => mount.RuntimeState)
                .ToArray()));

        try
        {
            await WaitForContainerApplicationInstanceReadinessAsync(
                definition,
                service,
                instance,
                process,
                processLog,
                cancellationToken,
                procedureContext);
        }
        catch
        {
            await CleanUpFailedContainerApplicationInstanceStartAsync(
                definition,
                engine,
                instance,
                process,
                processLog,
                logPath,
                volumeMaterializations.Select(mount => mount.RuntimeState),
                cancellationToken);
            throw;
        }

        processLog.Append(
            $"Started container image '{definition.ContainerImage}' as '{instance.Name}' replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} of {instance.ReplicaCount.ToString(CultureInfo.InvariantCulture)} using {engine.Name} with {definition.Lifetime} lifetime.",
            "process",
            "Information");
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.started",
            $"Application provider started container replica '{instance.Name}' for '{definition.Name}'.");

        _containerProcesses.Track(
            definition.Id,
            process,
            processLog,
            definition.Lifetime,
            logPath);

        if (instance.ReplicaOrdinal == instance.ReplicaCount &&
            useIngress)
        {
            await _containerIngress.StartAsync(
                definition,
                engine,
                service,
                processLog,
                Id,
                cancellationToken,
                procedureContext);
        }
    }

    private async Task StopApplicationAsync(
        string applicationId,
        bool force,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var application = store.GetApplication(applicationId);
        var log = _containerProcesses.GetProcessLog(applicationId);

        if (application is not null)
        {
            MarkStopping(applicationId);
            procedureContext?.AppendProviderEvent(
                Id,
                "application.stop.preparing",
                $"Application provider is preparing to stop '{application.Name}' ({application.ResourceType}) with {application.Lifetime} lifetime.");
        }

        if (application is not null &&
            !IsContainerBacked(application))
        {
            procedureContext?.AppendProviderEvent(
                Id,
                "application.process.stopping",
                $"Application provider is stopping local process for '{application.Name}'.");
            await localProcesses.StopAsync(
                ApplicationProcessDefinitions.Create(application),
                force,
                cancellationToken);
            procedureContext?.AppendProviderEvent(
                Id,
                "application.process.stopped",
                $"Application provider stopped local process for '{application.Name}'.");
            ClearStopping(applicationId);
            return;
        }

        if (application is not null &&
            IsContainerBacked(application))
        {
            if (resourceManager is null)
            {
                throw new InvalidOperationException(
                    $"Container resource '{application.Name}' requires resource manager context to resolve a container host.");
            }

            var engine = await ResolveContainerHostAsync(
                application.ContainerHostId,
                preferredContainerHostId,
                resourceManager,
                requiredCapability: null,
                cancellationToken);
            if (engine is not null)
            {
                await StopContainerAsync(application, engine, log, cancellationToken, procedureContext);
                ClearStopping(applicationId);
            }
        }

        CompleteContainerApplicationStop(
            application,
            applicationId,
            force,
            log,
            cancellationToken,
            procedureContext);
    }

    private void CompleteContainerApplicationStop(
        ApplicationResourceDefinition? application,
        string applicationId,
        bool force,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        if (!_containerProcesses.TryGetRunningProcess(application, out var process))
        {
            ClearStopping(applicationId);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        procedureContext?.AppendProviderEvent(
            Id,
            "application.process.stopping",
            $"Application provider is stopping tracked process for '{application?.Name ?? applicationId}'.");
        log.Append(force ? "Stopping process." : "Stopping control-plane-scoped process.", "process", "Information");
        ProcessShutdown.KillProcessTreeAndWait(process);
        var logPath = _containerProcesses.GetTrackedLogPath(applicationId);
        runtimeStates.Save(new ApplicationRuntimeState(
            applicationId,
            process.Id,
            null,
            DateTimeOffset.UtcNow,
            ApplicationContainerProcessTracker.TryGetExitCode(process),
            logPath,
            VolumeMounts: ApplicationContainerProcessTracker.MarkVolumeMountsNotActive(
                runtimeStates.Get(applicationId)?.RuntimeVolumeMounts ?? [],
                DateTimeOffset.UtcNow)));
        procedureContext?.AppendProviderEvent(
            Id,
            "application.process.stopped",
            $"Application provider stopped tracked process for '{application?.Name ?? applicationId}'.");
        ClearStopping(applicationId);
    }

    private async Task StopContainerAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var service = CreateActiveContainerOrchestratorService(definition);
        if (_containerIngress.ShouldUseIngress(service))
        {
            await _containerIngress.StopAsync(
                definition,
                service,
                engine,
                log,
                Id,
                cancellationToken,
                procedureContext);
        }

        var replicaGroup = CreateDefaultContainerReplicaGroup(service);
        foreach (var instance in replicaGroup.Instances)
        {
            await StopContainerApplicationInstanceAsync(
                definition,
                engine,
                log,
                instance,
                removeContainer: true,
                cancellationToken,
                procedureContext);
        }
    }

    private async Task StopContainerApplicationInstanceAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        ResourceOrchestratorServiceInstance instance,
        bool removeContainer,
        CancellationToken cancellationToken)
    {
        if (resourceManager is null)
        {
            throw new InvalidOperationException(
                $"Container resource '{definition.Name}' requires resource manager context to resolve a container host.");
        }

        var engine = await ResolveContainerHostAsync(
            definition.ContainerHostId,
            preferredContainerHostId,
            resourceManager,
            requiredCapability: null,
            cancellationToken);
        if (engine is null)
        {
            return;
        }

        await StopContainerApplicationInstanceAsync(
            definition,
            engine,
            _containerProcesses.GetProcessLog(definition.Id),
            instance,
            removeContainer,
            cancellationToken);
    }

    private async Task StopContainerApplicationInstanceAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        ResourceOrchestratorServiceInstance instance,
        bool removeContainer,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.stopping",
            $"Application provider is stopping container replica '{instance.Name}' for '{definition.Name}'.");
        if (removeContainer || definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            await ApplicationContainerHostCommands.RunAsync(
                engine,
                ["rm", "-f", instance.Name],
                log,
                cancellationToken,
                dockerHostLogger);
        }
        else
        {
            await ApplicationContainerHostCommands.RunAsync(
                engine,
                ["stop", instance.Name],
                log,
                cancellationToken,
                dockerHostLogger);
        }

        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.stopped",
            $"Application provider stopped container replica '{instance.Name}' for '{definition.Name}'.");
    }

    private static void AddIfNotEmpty(
        IDictionary<string, string> attributes,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[name] = value.Trim();
        }
    }

    private static string Pluralize(int count) =>
        count == 1 ? string.Empty : "s";

    private static bool IsProjectBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsAspNetCoreProject(application.ResourceType) ||
        !string.IsNullOrWhiteSpace(application.ProjectPath);

    private ResourceState GetState(string applicationId)
        => CreateRuntimeStateTracker().GetState(applicationId);

    private ApplicationResourceProjector CreateApplicationResourceProjector() =>
        new(
            runtimeStates,
            GetState,
            CreateResourceObservability,
            (application, state, runtimeRevisionScoped) => CreateDefaultContainerOrchestratorDeployment(
                application,
                state,
                runtimeRevisionScoped),
            ResolveLocalPort);

    private void MarkStarting(string applicationId)
        => CreateRuntimeStateTracker().MarkStarting(applicationId);

    private void ClearStarting(ApplicationResourceDefinition definition)
        => CreateRuntimeStateTracker().ClearStarting(definition.Id);

    private void MarkStopping(string applicationId)
        => CreateRuntimeStateTracker().MarkStopping(applicationId);

    private void ClearStopping(string applicationId)
        => CreateRuntimeStateTracker().ClearStopping(applicationId);

    private ApplicationRuntimeStateTracker CreateRuntimeStateTracker() =>
        new(
            runtimeStates,
            IsRunning,
            transientStateTimeout: StartingStateTimeout);

    private IReadOnlyList<ResourceEndpoint> CreateEndpoints(ApplicationResourceDefinition application)
        => CreateApplicationResourceProjector().CreateEndpoints(application);

    private IReadOnlyList<ResourceEndpointNetworkMapping> CreateEndpointNetworkMappings(
        ApplicationResourceDefinition application)
        => CreateApplicationResourceProjector().CreateEndpointNetworkMappings(application);

    private string CreateServiceEndpointAddress(string resourceId, ServicePort port) =>
        CreateApplicationResourceProjector().CreateServiceEndpointAddress(resourceId, port);

    private string CreateRuntimeContainerProbeEndpointAddress(
        string resourceId,
        ServicePort port,
        ResourceOrchestratorServiceInstance instance)
    {
        var protocol = NormalizeProtocol(port.Protocol);
        var host = FirstNonEmpty(port.IPAddress, port.Host, "localhost")!;
        return $"{protocol}://{host}:{ResolveReplicaProbeLocalPort(resourceId, port, instance).ToString(CultureInfo.InvariantCulture)}";
    }

    private ApplicationResourceDefinition NormalizeDefinition(ApplicationResourceDefinition definition) =>
        _definitionNormalizer.Normalize(definition);

    private ApplicationResourceDefinition ResolveDefinition(ApplicationResourceDefinition definition) =>
        _definitionNormalizer.Resolve(definition);

    private async Task BuildAspNetCoreProjectAsync(
        ApplicationResourceDefinition definition,
        ResourceProcedureContext? procedureContext,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return;
        }

        var projectPath = definition.ProjectPath.Trim();
        var workingDirectory = ResolveConfiguredWorkingDirectory(definition);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.project.build.waiting",
            $"Application provider is waiting to build project '{projectPath}' for '{definition.Name}'.");
        await AspNetCoreProjectBuildLock.WaitAsync(cancellationToken);
        try
        {
            procedureContext?.AppendProviderEvent(
                Id,
                "application.project.build.started",
                $"Application provider is building project '{projectPath}' for '{definition.Name}'.");
            var exitCode = await localProcesses.RunCommandAsync(
                definition.Id,
                "dotnet",
                ["build", projectPath, "--nologo", "--disable-build-servers"],
                workingDirectory,
                cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Project '{projectPath}' failed to build before starting '{definition.Name}'. See the application log for compiler output.");
            }

            procedureContext?.AppendProviderEvent(
                Id,
                "application.project.build.completed",
                $"Application provider built project '{projectPath}' for '{definition.Name}'.");
        }
        finally
        {
            AspNetCoreProjectBuildLock.Release();
        }
    }

    private string ResolveProjectPath(ApplicationResourceDefinition definition)
    {
        var projectPath = definition.ProjectPath?.Trim() ?? string.Empty;
        if (Path.IsPathRooted(projectPath))
        {
            return Path.GetFullPath(projectPath);
        }

        return Path.GetFullPath(projectPath, ResolveConfiguredWorkingDirectory(definition));
    }

    private string ResolveConfiguredWorkingDirectory(ApplicationResourceDefinition definition) =>
        string.IsNullOrWhiteSpace(definition.WorkingDirectory)
            ? environment.ContentRootPath
            : Path.IsPathRooted(definition.WorkingDirectory)
                ? Path.GetFullPath(definition.WorkingDirectory)
                : Path.GetFullPath(definition.WorkingDirectory, environment.ContentRootPath);

    private string ResolveConfiguredExecutablePath(
        ApplicationResourceDefinition definition,
        string workingDirectory)
    {
        var executablePath = definition.ExecutablePath.Trim();
        return Path.IsPathRooted(executablePath)
            ? Path.GetFullPath(executablePath)
            : Path.GetFullPath(executablePath, workingDirectory);
    }

    private static bool IsExplicitExecutablePath(string executablePath) =>
        executablePath.Contains(Path.DirectorySeparatorChar) ||
        executablePath.Contains(Path.AltDirectorySeparatorChar);

    private ResourceWorkloadConfiguration CreateWorkloadConfiguration(
        ApplicationResourceDefinition application,
        string? resourceGroupId = null,
        IResourceManagerStore? resourceManager = null) =>
        _workloadConfigurations.Create(
            application,
            resourceGroupId,
            resourceManager);

    private ApplicationResourceDefinition GetContainerApplication(string resourceId)
    {
        var application = store.GetApplication(resourceId)
            ?? throw new InvalidOperationException(
                $"Container app resource '{resourceId}' is not configured.");
        if (!IsContainerBacked(application))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is not a container-backed application.");
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

        return await ResolveContainerHostAsync(
            definition.ContainerHostId,
            preferredContainerHostId,
            resourceManager,
            requiredCapability,
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Resource '{definition.Name}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set WithContainerHost(...).");
    }

    private Task<ContainerHostDescriptor?> ResolveContainerHostAsync(
        string? containerHostId,
        string? preferredContainerHostId,
        IResourceManagerStore resourceManager,
        string? requiredCapability,
        CancellationToken cancellationToken) =>
        _containerHosts.ResolveAsync(
            containerHostId,
            preferredContainerHostId,
            resourceManager,
            requiredCapability,
            cancellationToken);

    private ContainerHostDescriptor? ResolveStaticContainerHost(ApplicationResourceDefinition definition)
        => _containerHosts.ResolveStatic(definition);

    private Task<ContainerHostDescriptor?> ResolveStaticContainerHostAsync(
        ApplicationResourceDefinition definition,
        CancellationToken cancellationToken) =>
        _containerHosts.ResolveStaticAsync(definition, cancellationToken);

    private IReadOnlyList<ContainerHostDescriptor> GetContainerHosts() =>
        _containerHosts.GetContainerHosts();

    private static string GetContainerName(ResourceOrchestratorService service, int replica = 1) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultInstanceName(
            service.Name,
            replica,
            service.Replicas);

    private static string CreateRuntimeContainerResourceId(string resourceId, int replica) =>
        ApplicationResourceNames.CreateRuntimeContainerResourceId(resourceId, replica);

    private static string GetContainerName(string resourceId, int replica = 1, int replicas = 1)
    {
        var serviceName = GetContainerServiceName(resourceId);
        return ResourceOrchestratorReplicaGroups.CreateDefaultInstanceName(
            serviceName,
            replica,
            replicas);
    }

    private static string GetContainerServiceName(string resourceId) =>
        ApplicationContainerOrchestratorDeploymentFactory.CreateServiceName(resourceId);

    private static string CreateDefaultContainerOrchestratorDeploymentId(string resourceId) =>
        ApplicationContainerOrchestratorDeploymentFactory.CreateDeploymentId(resourceId);

    private static IReadOnlyList<ServicePort> GetRuntimeContainerProbePorts(
        ApplicationResourceDefinition application,
        ResourceOrchestratorService service)
    {
        if (!ShouldProjectRuntimeContainerProbeTargets(application))
        {
            return [];
        }

        var namedEndpoints = application.HealthChecks
            .Select(check => NormalizeNullable(check.HttpSource?.EndpointName))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasUnnamedHttpProbe = application.HealthChecks.Any(check =>
            check.HttpSource is not null &&
            string.IsNullOrWhiteSpace(check.HttpSource.EndpointName));

        return service.ServicePorts
            .Where(IsHttpProbePort)
            .Where(port =>
                namedEndpoints.Contains(port.Name) ||
                hasUnnamedHttpProbe)
            .DistinctBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldProjectRuntimeContainerProbeTargets(
        ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        IsReplicaModeEnabled(application) &&
        application.HealthChecks.Any(check => check.HttpSource is not null);

    private static bool ShouldProjectActiveRuntimeContainerProbeTargets(
        ApplicationResourceDefinition application,
        ResourceState state) =>
        ShouldProjectRuntimeContainerProbeTargets(application) &&
        state is ResourceState.Running or ResourceState.Starting or ResourceState.Degraded;

    private static bool IsHttpProbePort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "https";

    private static string CreateStableIdentifier(string value)
        => ApplicationResourceNames.CreateStableIdentifier(value);

    private static bool IsRuntimeContainerReplica(Resource resource) =>
        string.Equals(resource.EffectiveTypeId, "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            GetAttribute(resource, ResourceAttributeNames.RuntimeKind),
            "containerReplica",
            StringComparison.OrdinalIgnoreCase);

    private static string GetAttribute(Resource resource, string name) =>
        resource.ResourceAttributes.TryGetValue(name, out var value)
            ? value
            : string.Empty;

    private static async Task<int?> WaitForEarlyContainerHostExitAsync(
        Process process,
        TimeSpan confirmationDelay,
        CancellationToken cancellationToken)
    {
        if (confirmationDelay <= TimeSpan.Zero)
        {
            return process.HasExited ? process.ExitCode : null;
        }

        var exitTask = process.WaitForExitAsync(cancellationToken);
        var delayTask = Task.Delay(confirmationDelay, cancellationToken);
        var completedTask = await Task.WhenAny(exitTask, delayTask);
        if (completedTask == exitTask)
        {
            await exitTask;
            return process.ExitCode;
        }

        await delayTask;
        return process.HasExited ? process.ExitCode : null;
    }

    private async Task WaitForContainerApplicationInstanceReadinessAsync(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        Process process,
        ApplicationProcessLog processLog,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext)
    {
        var checks = SelectContainerStartupReadinessChecks(definition);
        if (checks.Count == 0)
        {
            return;
        }

        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.readiness.waiting",
            $"Application provider is waiting for {checks.Count.ToString(CultureInfo.InvariantCulture)} readiness check{Pluralize(checks.Count)} on container replica '{instance.Name}' for '{definition.Name}'.");

        foreach (var check in checks)
        {
            await WaitForContainerApplicationReadinessCheckAsync(
                definition,
                service,
                instance,
                process,
                processLog,
                check,
                cancellationToken);
        }

        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.instance.readiness.ready",
            $"Application provider observed container replica '{instance.Name}' readiness for '{definition.Name}'.");
    }

    private async Task WaitForContainerApplicationReadinessCheckAsync(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        Process process,
        ApplicationProcessLog processLog,
        ResourceHealthCheck check,
        CancellationToken cancellationToken)
    {
        var uri = CreateContainerApplicationReadinessProbeUri(definition, service, instance, check)
            ?? throw new InvalidOperationException(
                $"Container replica '{instance.Name}' readiness check '{check.Name}' cannot resolve an HTTP endpoint.");
        var deadline = DateTimeOffset.UtcNow.Add(GetContainerReadinessTimeout(check));
        var pollInterval = GetContainerReadinessPollInterval();
        string? lastDetail = null;

        while (DateTimeOffset.UtcNow <= deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                var exitCode = ApplicationContainerProcessTracker.TryGetExitCode(process);
                throw new InvalidOperationException(
                    $"Container replica '{instance.Name}' exited before readiness check '{check.Name}' became healthy with code {exitCode?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}.");
            }

            var result = await ProbeContainerApplicationReadinessAsync(uri, check, cancellationToken);
            if (result.Healthy)
            {
                processLog.Append(
                    $"Container replica '{instance.Name}' readiness check '{check.Name}' succeeded at {uri}.",
                    "readiness",
                    "Information");
                return;
            }

            lastDetail = result.Detail;
            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(remaining < pollInterval ? remaining : pollInterval, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Container replica '{instance.Name}' did not become ready before timeout. Readiness check '{check.Name}' at '{uri}' last reported: {lastDetail ?? "No response"}.");
    }

    private async Task CleanUpFailedContainerApplicationInstanceStartAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ResourceOrchestratorServiceInstance instance,
        Process process,
        ApplicationProcessLog processLog,
        string? logPath,
        IEnumerable<ResourceVolumeMountMaterialization> volumeMounts,
        CancellationToken cancellationToken)
    {
        processLog.Append(
            $"Cleaning up container replica '{instance.Name}' after startup readiness failed.",
            "readiness",
            "Warning");

        try
        {
            if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
            {
                await ApplicationContainerHostCommands.RunAsync(
                    engine,
                    ["rm", "-f", instance.Name],
                    processLog,
                    cancellationToken,
                    dockerHostLogger);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            processLog.Append(
                $"Container replica '{instance.Name}' cleanup failed after readiness failure: {exception.Message}",
                "readiness",
                "Error");
        }

        if (!process.HasExited)
        {
            ProcessShutdown.KillProcessTreeAndWait(process);
        }

        var now = DateTimeOffset.UtcNow;
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            ApplicationContainerProcessTracker.TryGetProcessId(process),
            null,
            now,
            ApplicationContainerProcessTracker.TryGetExitCode(process),
            logPath,
            VolumeMounts: ApplicationContainerProcessTracker.MarkVolumeMountsNotActive(volumeMounts, now)));
        process.Dispose();
    }

    private async Task<(bool Healthy, string Detail)> ProbeContainerApplicationReadinessAsync(
        Uri uri,
        ResourceHealthCheck check,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(check.HttpSource?.Timeout ?? check.Timeout ?? TimeSpan.FromSeconds(5));
        try
        {
            using var response = await ContainerReadinessHttpClient.GetAsync(
                uri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            return ((int)response.StatusCode < 400, $"{(int)response.StatusCode} {response.ReasonPhrase}".Trim());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, "Timed out");
        }
        catch (HttpRequestException exception)
        {
            return (false, exception.Message);
        }
    }

    private static IReadOnlyList<ResourceHealthCheck> SelectContainerStartupReadinessChecks(
        ApplicationResourceDefinition definition)
    {
        var startupOrReadiness = definition.HealthChecks
            .Where(check => check.HttpSource is not null)
            .Where(check => check.Type is ResourceProbeType.Startup or ResourceProbeType.Readiness)
            .ToArray();
        if (startupOrReadiness.Length > 0)
        {
            return startupOrReadiness;
        }

        return definition.HealthChecks
            .Where(check => check.HttpSource is not null)
            .Where(check => check.Type == ResourceProbeType.Health)
            .ToArray();
    }

    private Uri? CreateContainerApplicationReadinessProbeUri(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance,
        ResourceHealthCheck check)
    {
        var source = check.HttpSource;
        if (source is null)
        {
            return null;
        }

        if (Uri.TryCreate(source.Path, UriKind.Absolute, out var absolute) &&
            absolute.Scheme is "http" or "https")
        {
            return absolute;
        }

        var port = ResolveContainerApplicationReadinessProbePort(definition, service, source);
        if (port is null)
        {
            return null;
        }

        var baseAddress = ShouldProjectRuntimeContainerProbeTargets(definition)
            ? CreateRuntimeContainerProbeEndpointAddress(definition.Id, port, instance)
            : CreateServiceEndpointAddress(definition.Id, port);
        var path = string.IsNullOrWhiteSpace(source.Path) ? "/" : source.Path.Trim();
        return Uri.TryCreate(new Uri(baseAddress), path, out var uri)
            ? uri
            : null;
    }

    private static ServicePort? ResolveContainerApplicationReadinessProbePort(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        ResourceHttpProbeSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.EndpointName))
        {
            return service.ServicePorts.FirstOrDefault(port =>
                string.Equals(port.Name, source.EndpointName, StringComparison.OrdinalIgnoreCase) &&
                IsHttpProbePort(port));
        }

        return GetRuntimeContainerProbePorts(definition, service).FirstOrDefault() ??
            service.ServicePorts.FirstOrDefault(IsHttpProbePort);
    }

    private TimeSpan GetContainerReadinessTimeout(ResourceHealthCheck check)
    {
        if (options.ContainerReadinessTimeout > TimeSpan.Zero)
        {
            return options.ContainerReadinessTimeout;
        }

        return check.HttpSource?.Timeout ?? check.Timeout ?? TimeSpan.FromSeconds(5);
    }

    private TimeSpan GetContainerReadinessPollInterval() =>
        options.ContainerReadinessPollInterval > TimeSpan.Zero
            ? options.ContainerReadinessPollInterval
            : TimeSpan.FromMilliseconds(500);

    private static string CreateContainerStartFailureMessage(
        ApplicationProcessLog processLog,
        string instanceName,
        int exitCode)
    {
        var details = processLog
            .Read(10, before: null)
            .Where(entry => string.Equals(entry.Severity, "Error", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Message.Trim())
            .FirstOrDefault(message =>
                !string.IsNullOrWhiteSpace(message) &&
                !message.StartsWith("Container replica '", StringComparison.OrdinalIgnoreCase));

        var message = $"Container replica '{instanceName}' exited during startup with code {exitCode.ToString(CultureInfo.InvariantCulture)}.";
        return string.IsNullOrWhiteSpace(details)
            ? message
            : $"{message} {details}";
    }

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        application.ProjectContainerBuild ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        NormalizeContainerRegistry(application.ContainerRegistry);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static string CreateRegistryImageReference(string registry, string image)
    {
        var imageRegistry = GetImageRegistryAddress(registry);
        var normalizedImage = image.Trim();
        if (IsDockerHubRegistry(imageRegistry) ||
            normalizedImage.StartsWith($"{imageRegistry}/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedImage;
        }

        return $"{imageRegistry}/{normalizedImage}";
    }

    private async Task<string?> TryResolveContainerImagePlatformAsync(
        ContainerHostDescriptor engine,
        string imageReference,
        CancellationToken cancellationToken)
    {
        var result = await ApplicationContainerHostCommands.CaptureAsync(
            engine,
            [
                "image",
                "inspect",
                "--format",
                "{{.Os}}/{{.Architecture}}{{if .Variant}}/{{.Variant}}{{end}}",
                imageReference
            ],
            cancellationToken,
            dockerHostLogger);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var platform = result.Output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return IsValidContainerImagePlatform(platform)
            ? platform
            : null;
    }

    private static bool IsValidContainerImagePlatform(string? platform)
    {
        if (string.IsNullOrWhiteSpace(platform) ||
            platform.Contains("<no value>", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = platform.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length is 2 or 3 &&
            parts.All(part => part.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '_' or '-' or '.'));
    }

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Authority)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private static bool IsDockerHubRegistry(string registry) =>
        string.Equals(registry, ContainerRegistryDefaults.DockerHub, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(registry, "index.docker.io", StringComparison.OrdinalIgnoreCase);

    internal static string FormatContainerHostCommandLine(IReadOnlyList<string> arguments) =>
        ApplicationContainerHostCommands.FormatCommandLine(arguments);

    private static bool IsHidden(ApplicationResourceDefinition application) =>
        application.EnvironmentVariables.Any(variable =>
            string.Equals(variable.Name, HiddenResourceEnvironmentVariable, StringComparison.OrdinalIgnoreCase) &&
            bool.TryParse(variable.Value, out var hidden) &&
            hidden);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private string CreateUniqueImportId(string name) =>
        _applicationDefinitionRegistrations.CreateUniqueImportId(name);

    private string ValidateAvailableImportId(string resourceId) =>
        _applicationDefinitionRegistrations.ValidateAvailableImportId(resourceId);

    private static string ResolveWorkingDirectory(ApplicationResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.WorkingDirectory))
        {
            return definition.WorkingDirectory;
        }

        var executableDirectory = ApplicationResourceTypes.IsAspNetCoreProject(definition.ResourceType)
            ? null
            : Path.GetDirectoryName(definition.ExecutablePath);
        return string.IsNullOrWhiteSpace(executableDirectory)
            ? Environment.CurrentDirectory
            : executableDirectory;
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string GetResourceName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var id) && !string.IsNullOrWhiteSpace(id.Name)
            ? id.Name
            : resourceId;

}
