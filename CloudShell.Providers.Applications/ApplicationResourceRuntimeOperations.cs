using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logging;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
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
    IResourceEventSink? resourceEvents = null,
    ILoggerFactory? loggerFactory = null,
    ApplicationResourceDefinitionNormalizer? definitionNormalizer = null,
    ApplicationResourceDefinitionRegistrationService? applicationDefinitionRegistrations = null,
    ApplicationWorkloadConfigurationProvider? workloadConfigurations = null,
    ApplicationResourceSettingResolver? settingResolver = null,
    IApplicationResourceActionAvailabilityOperations? actionAvailability = null) :
    IApplicationResourceProcedureOperations,
    IContainerApplicationResourceProviderOperations,
    IDisposable
{
    private static readonly TimeSpan StartingStateTimeout = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim AspNetCoreProjectBuildLock = new(1, 1);
    private static readonly HttpClient ContainerReadinessHttpClient = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    private const string DefaultContainerNetworkName = "cloudshell";
    public const string HiddenResourceEnvironmentVariable = "CloudShell__ResourceManager__Hidden";

    private readonly ConcurrentDictionary<string, Lazy<Task<ApplicationResourceDefinition>>> _containerImageMaterializations =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<IResourceIdentityCredentialEnvironmentProvider> identityCredentialEnvironmentProviders =
        identityCredentialEnvironmentProviders.ToArray();
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
    private readonly ApplicationWorkloadConfigurationProvider _workloadConfigurations =
        workloadConfigurations ?? new ApplicationWorkloadConfigurationProvider(
            options,
            declarations,
            identityCredentialEnvironmentProviders);
    private readonly ApplicationResourceSettingResolver _settingResolver =
        settingResolver ?? new ApplicationResourceSettingResolver(
            declarations,
            serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
            serviceProvider.GetServices<ISecretReferenceResolver>());
    private readonly IApplicationResourceActionAvailabilityOperations _actionAvailability =
        actionAvailability ?? serviceProvider.GetService<IApplicationResourceActionAvailabilityOperations>() ??
            new ApplicationResourceActionAvailabilityOperations(
                new ApplicationResourceDefinitionSource(
                    store,
                    definitionNormalizer ?? new ApplicationResourceDefinitionNormalizer(environment)),
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
                settingResolver ?? new ApplicationResourceSettingResolver(
                    declarations,
                    serviceProvider.GetServices<IConfigurationEntryReferenceResolver>(),
                    serviceProvider.GetServices<ISecretReferenceResolver>()),
                workloadConfigurations ?? new ApplicationWorkloadConfigurationProvider(
                    options,
                    declarations,
                    identityCredentialEnvironmentProviders),
                containerHosts ?? new ApplicationContainerHostResolver(serviceProvider),
                options,
                environment,
                declarations);
    private readonly ApplicationContainerProcessTracker _containerProcesses =
        serviceProvider.GetService<ApplicationContainerProcessTracker>() ?? new ApplicationContainerProcessTracker(
            runtimeStates,
            options,
            environment,
            loggerFactory);
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
            workloadConfigurations ?? new ApplicationWorkloadConfigurationProvider(
                options,
                declarations,
                identityCredentialEnvironmentProviders),
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

    public bool CanUpdateImage(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanUpdateReplicas(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanExecuteOrchestratorService(
        Resource resource,
        ResourceAction action) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Stop or ResourceActionKind.Restart;

    public Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        return Task.FromResult(CreateDefaultContainerOrchestratorService(application));
    }

    public bool CanDescribeDeployment(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        var state = context.Resource.State ?? GetState(application.Id);
        return Task.FromResult<ResourceOrchestratorDeployment?>(
            CreateDefaultContainerOrchestratorDeployment(application, state));
    }

    public async Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var application = GetContainerApplication(context.ResourceContext.Resource.Id);

        if (action.Kind is ResourceActionKind.Stop && ShouldUseContainerAppIngress(context.Service))
        {
            var stopEngine = await ResolveRequiredContainerHostAsync(
                application,
                context.ResourceContext.ResourceManager,
                context.ResourceContext.PreferredContainerHostId,
                ContainerHostCapabilityIds.ContainerImage,
                cancellationToken);
            await StopContainerAppIngressAsync(
                application,
                stopEngine,
                _containerProcesses.GetProcessLog(application.Id),
                cancellationToken);
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
        var processLog = _containerProcesses.GetProcessLog(application.Id);

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

    public async Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var application = GetContainerApplication(context.ResourceContext.Resource.Id);
        switch (action.Kind)
        {
            case ResourceActionKind.Start:
                var materializationKey = CreateContainerImageMaterializationKey(application);
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
                    RemoveContainerImageMaterialization(materializationKey);
                    throw;
                }
                finally
                {
                    if (context.Instance.ReplicaOrdinal >= context.Instance.ReplicaCount)
                    {
                        RemoveContainerImageMaterialization(materializationKey);
                    }
                }
                return;
            case ResourceActionKind.Stop:
                await StopContainerApplicationInstanceAsync(
                    application,
                    context.ResourceContext.ResourceManager,
                    context.ResourceContext.PreferredContainerHostId,
                    context.Instance,
                    removeContainer: true,
                    cancellationToken);
                return;
            default:
                throw new NotSupportedException(
                    $"Container app services do not support action '{action.DisplayName}'.");
        }
    }

    public async Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        if (requestedReplicas is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedReplicas),
                requestedReplicas,
                "Requested replicas must be greater than or equal to 1.");
        }

        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        var normalizedImage = image.Trim();
        var requestedReplicaCount = requestedReplicas ?? application.Replicas;
        if (string.Equals(application.ContainerImage, normalizedImage, StringComparison.Ordinal) &&
            requestedReplicaCount == application.Replicas)
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses image '{normalizedImage}'.");
        }

        var wasRunning = IsRunning(application.Id);
        if (restartIfRunning && wasRunning)
        {
            await EnsureContainerRestartAvailableForUpdateAsync(
                context,
                "update image",
                cancellationToken);
        }

        var plan = ContainerDeploymentPlanner.PlanImageDeployment(
            application,
            normalizedImage,
            requestedReplicaCount,
            requestedReplicas.HasValue,
            triggeredBy,
            CreateDefaultContainerOrchestratorDeploymentId(application.Id),
            NormalizeDefinition);
        var updated = plan.Definition;
        store.Save(updated);
        containerDeployments.RecordDeployment(
            plan.Deployment,
            plan.Revision,
            plan.BasedOnRevision);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            $"Deployed container image '{normalizedImage}' from '{application.ContainerImage ?? "none"}' and produced revision '{updated.ContainerRevision}' with requested replicas '{FormatRequestedReplicas(requestedReplicas)}'.",
            DateTimeOffset.UtcNow,
            triggeredBy));

        if (restartIfRunning && wasRunning)
        {
            await StopApplicationAsync(
                application.Id,
                force: true,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken,
                context);
            await StartApplicationAsync(
                application.Id,
                context.Resource.DependsOn,
                context.ResourceGroupId,
                context.Registrations,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken,
                context);
            resourceEvents?.Append(new ResourceEvent(
                application.Id,
                ResourceEventTypes.Events.Lifecycle.Restarted,
                $"Restarted container app on revision '{updated.ContainerRevision}' after image update.",
                DateTimeOffset.UtcNow,
                triggeredBy));

            return ResourceProcedureResult.Completed(
                $"Deployed {application.Name} image '{normalizedImage}', produced revision '{updated.ContainerRevision}', and restarted it.");
        }

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
                $"Deployed {application.Name} image '{normalizedImage}' and produced revision '{updated.ContainerRevision}'.",
                application.Id,
                "The container app is running. Runtime reconciliation is required to cut over to this deployment.")
            : ResourceProcedureResult.Completed(
                $"Deployed {application.Name} image '{normalizedImage}' and produced revision '{updated.ContainerRevision}'.");
    }

    public async Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        if (replicas < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicas), replicas, "Replicas must be greater than or equal to 1.");
        }

        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        if (application.ReplicasEnabled && application.Replicas == replicas)
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses {replicas} replica{Pluralize(replicas)}.");
        }

        var wasRunning = IsRunning(application.Id);
        if (restartIfRunning && wasRunning)
        {
            await EnsureContainerRestartAvailableForUpdateAsync(
                context,
                "update replicas",
                cancellationToken);
        }

        var plan = ContainerScalingPlanner.PlanReplicaUpdate(
            application,
            replicas,
            NormalizeDefinition);
        var updated = plan.Definition;

        if (restartIfRunning && wasRunning)
        {
            await StopApplicationAsync(
                application.Id,
                force: true,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken,
                context);
        }

        store.Save(updated);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Deployment.ReplicasUpdated,
            $"Changed container app replicas from '{application.Replicas}' to '{updated.Replicas}'.",
            DateTimeOffset.UtcNow,
            triggeredBy));

        if (restartIfRunning && wasRunning)
        {
            await StartApplicationAsync(
                application.Id,
                context.Resource.DependsOn,
                context.ResourceGroupId,
                context.Registrations,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken,
                context);
            resourceEvents?.Append(new ResourceEvent(
                application.Id,
                ResourceEventTypes.Events.Lifecycle.Restarted,
                $"Restarted container app with {updated.Replicas} replica{Pluralize(updated.Replicas)}.",
                DateTimeOffset.UtcNow,
                triggeredBy));

            return ResourceProcedureResult.Completed(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)} and restarted it.");
        }

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.",
                application.Id,
                "The container app is running. Runtime reconciliation is required to apply the replica count.")
            : ResourceProcedureResult.Completed(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.");
    }

    private async Task EnsureContainerRestartAvailableForUpdateAsync(
        ResourceProcedureContext context,
        string operation,
        CancellationToken cancellationToken)
    {
        var restartReason = await _actionAvailability.GetActionUnavailableReasonAsync(
            context,
            ResourceAction.Restart,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(restartReason))
        {
            var applicationName = store.GetApplication(context.Resource.Id) is { } application
                ? FormatApplicationResourceName(application)
                : context.Resource.Name;
            throw new InvalidOperationException(
                $"Container app resource '{applicationName}' cannot {operation} and restart because {restartReason}");
        }
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
        var volumeMaterializations = CreateLocalProcessVolumeMaterializations(
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
        CancellationToken cancellationToken) =>
        ResolveApplicationEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            resourceManager,
            includeAspNetCoreProjectVariables: true,
            cancellationToken);

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

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveDependencyEnvironmentVariables(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn) =>
        definition.DependsOn
            .Concat(dependsOn)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(dependency => environmentVariableProviders
                .SelectMany(provider => provider.GetEnvironmentVariables(dependency)))
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveServiceDiscoveryEnvironmentVariables(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceManagerStore? resourceManager)
    {
        if (resourceManager is null)
        {
            return [];
        }

        var references = definition.References
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Where(reference => IsSameResourceGroup(resourceManager.GetGroupForResource(reference)?.Id, resourceGroupId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (references.Count == 0)
        {
            return [];
        }

        return references
            .Select(reference => resourceManager.GetResource(reference))
            .Where(resource => resource is not null)
            .Cast<Resource>()
            .SelectMany(CreateServiceDiscoveryEndpointEnvironmentVariables)
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveObservabilityEnvironmentVariables(
        ApplicationResourceDefinition definition)
    {
        var observability = GetEffectiveObservability(definition);
        if (!observability.HasAnySignal)
        {
            return [];
        }

        var variables = new List<EnvironmentVariableAssignment>
        {
            new("OTEL_SERVICE_NAME", FirstNonEmpty(
                observability.ServiceName,
                ApplicationServiceDiscoveryDisplay.CreateConfigurationSegment(definition.Name),
                ApplicationServiceDiscoveryDisplay.CreateConfigurationSegment(definition.Id)) ?? definition.Id),
            new("OTEL_RESOURCE_ATTRIBUTES", CreateOtelResourceAttributes(definition, observability))
        };

        var endpoint = FirstNonEmpty(
            observability.OtlpEndpoint,
            options.OtlpEndpoint,
            Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL"));
        var protocol = FirstNonEmpty(
            observability.OtlpProtocol,
            options.OtlpProtocol,
            endpoint is null
                ? null
                : "grpc");

        if (endpoint is null)
        {
            endpoint = FirstNonEmpty(
                Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL"),
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
            protocol = FirstNonEmpty(
                observability.OtlpProtocol,
                options.OtlpProtocol,
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL"),
                Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_HTTP_ENDPOINT_URL") is null
                    ? null
                    : "http/protobuf");
        }

        if (!string.IsNullOrWhiteSpace(endpoint))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_ENDPOINT", endpoint));
        }

        if (!string.IsNullOrWhiteSpace(protocol))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_PROTOCOL", protocol));
        }

        var headers = FirstNonEmpty(
            observability.OtlpHeaders,
            options.OtlpHeaders,
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_HEADERS"));
        if (!string.IsNullOrWhiteSpace(headers))
        {
            variables.Add(new("OTEL_EXPORTER_OTLP_HEADERS", headers));
        }

        return variables;
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveWorkloadEnvironmentVariables(
        ApplicationResourceDefinition definition,
        string? resourceGroupId = null,
        IResourceManagerStore? resourceManager = null) =>
        (definition.UseServiceDiscovery
                ? ResolveServiceDiscoveryEnvironmentVariables(definition, resourceGroupId, resourceManager)
                : [])
            .Concat(ResolveObservabilityEnvironmentVariables(definition))
            .Concat(ResolveResourceIdentityEnvironmentVariables(definition))
            .Concat(definition.EnvironmentVariables)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private async Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveApplicationEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceManagerStore? resourceManager,
        bool includeAspNetCoreProjectVariables,
        CancellationToken cancellationToken)
    {
        var configuredVariables = await _settingResolver.ResolveConfiguredEnvironmentVariablesAsync(
            definition,
            resourceGroupId,
            cancellationToken);

        return ResolveDependencyEnvironmentVariables(definition, dependsOn)
            .Concat(definition.UseServiceDiscovery
                ? ResolveServiceDiscoveryEnvironmentVariables(definition, resourceGroupId, resourceManager)
                : [])
            .Concat(ResolveObservabilityEnvironmentVariables(definition))
            .Concat(includeAspNetCoreProjectVariables
                ? ResolveAspNetCoreProjectEnvironmentVariables(definition, resourceManager)
                : [])
            .Concat(ResolveResourceIdentityEnvironmentVariables(definition))
            .Concat(configuredVariables)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveResourceIdentityEnvironmentVariables(
        ApplicationResourceDefinition definition)
    {
        var declaration = declarations.GetDeclaration(definition.Id);
        if (declaration?.IdentityBinding is null)
        {
            return [];
        }

        var providerCatalog = declarations.CreateIdentityProviderCatalog(
            new ResourceIdentityProviderCatalog());
        var resolution = providerCatalog.Resolve(declaration.IdentityBinding);
        if (resolution.Provider is null)
        {
            return [];
        }

        var identity = ResourceIdentityReference.ForResource(
            definition.Id,
            declaration.IdentityBinding.Name);
        var scope = declaration.IdentityBinding.IdentityScopes.Count == 0
            ? string.IsNullOrWhiteSpace(options.ResourceIdentityDefaultScope)
                ? "ControlPlane.Access"
                : options.ResourceIdentityDefaultScope
            : declaration.IdentityBinding.IdentityScopes[0];
        var credentialEnvironmentProvider = identityCredentialEnvironmentProviders.FirstOrDefault(provider =>
            provider.CanCreateEnvironment(resolution.Provider));
        if (credentialEnvironmentProvider is not null)
        {
            return credentialEnvironmentProvider
                .CreateEnvironment(new ResourceIdentityCredentialEnvironmentRequest(
                    resolution.Provider,
                    identity,
                    declaration.IdentityBinding,
                    scope))
                .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
                .ToArray();
        }

        var tokenEndpoint = options.ResourceIdentityTokenEndpoint;
        if (resolution.Provider.Kind != ResourceIdentityProviderKind.BuiltIn ||
            string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return [];
        }

        var clientId = CreateResourceIdentityClientId(identity);

        return
        [
            new(
                EnvironmentCloudShellResourceCredential.TokenEndpointEnvironmentVariable,
                tokenEndpoint),
            new(
                EnvironmentCloudShellResourceCredential.ClientIdEnvironmentVariable,
                clientId),
            new(
                EnvironmentCloudShellResourceCredential.ClientSecretEnvironmentVariable,
                ResolveBuiltInResourceIdentityClientSecret(resolution.Provider, clientId)),
            new(
                EnvironmentCloudShellResourceCredential.ScopeEnvironmentVariable,
                scope)
        ];
    }

    private string ResolveBuiltInResourceIdentityClientSecret(
        ResourceIdentityProviderDefinition provider,
        string clientId) =>
        provider.ProviderSettings.TryGetValue("clientSecret", out var configuredSecret) &&
        !string.IsNullOrWhiteSpace(configuredSecret)
            ? configuredSecret
            : $"local-development-{SanitizeResourceIdentityClientId(clientId)}-secret";

    private static string CreateResourceIdentityClientId(ResourceIdentityReference identity) =>
        string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";

    private static string SanitizeResourceIdentityClientId(string value)
    {
        var characters = value
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        return new string(characters).Trim('-');
    }

    private ResourceIdentityReference? ResolveIdentity(string resourceId) =>
        _settingResolver.ResolveIdentity(resourceId);

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
        await PrepareOrchestratorServiceAsync(
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

    private async Task<ApplicationResourceDefinition> MaterializeContainerImageAsync(
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
            return await MaterializeContainerImageCoreAsync(
                definition,
                resourceManager,
                preferredContainerHostId,
                cancellationToken,
                procedureContext);
        }

        var key = CreateContainerImageMaterializationKey(definition);
        var materialization = _containerImageMaterializations.GetOrAdd(
            key,
            _ => new Lazy<Task<ApplicationResourceDefinition>>(
                () => MaterializeContainerImageCoreAsync(
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
            RemoveContainerImageMaterialization(key);
            throw;
        }
    }

    private async Task<ApplicationResourceDefinition> MaterializeContainerImageCoreAsync(
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
        var log = _containerProcesses.GetProcessLog(definition.Id);
        var imageReference = CreateProjectContainerImageReference(definition);

        procedureContext?.AppendProviderEvent(
            Id,
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
            Id,
            "application.container.image.built",
            $"Application provider built project container image '{imageReference.Reference}' for '{definition.Name}'.");
        return definition with
        {
            ContainerImage = imageReference.Reference
        };
    }

    private static string CreateContainerImageMaterializationKey(ApplicationResourceDefinition definition) =>
        string.Join(
            '|',
            definition.Id,
            definition.ContainerRevision,
            definition.ContainerRegistry,
            definition.ContainerHostId,
            definition.ProjectPath,
            definition.ContainerBuildContext,
            definition.ContainerDockerfile);

    private void RemoveContainerImageMaterialization(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            _containerImageMaterializations.TryRemove(key, out _);
        }
    }

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

        var startInfo = new ProcessStartInfo
        {
            FileName = ApplicationContainerHostCommands.GetExecutable(engine),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        ApplicationContainerHostCommands.ConfigureEnvironment(startInfo, engine);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(instance.Name);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            startInfo.ArgumentList.Add("--rm");
        }

        var useIngress = ShouldUseContainerAppIngress(service);
        var network = service.ServiceNetworks.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (!string.IsNullOrWhiteSpace(network))
        {
            startInfo.ArgumentList.Add("--network");
            startInfo.ArgumentList.Add(network);
            if (useIngress)
            {
                startInfo.ArgumentList.Add("--network-alias");
                startInfo.ArgumentList.Add(CreateContainerAppIngressTargetName(service, instance));
            }
        }

        if (instance.ReplicaOrdinal == 1)
        {
            foreach (var port in service.ServicePorts.Where(port => !useIngress || !IsContainerAppIngressPort(port)))
            {
                var hostPort = ResolveLocalPort(definition.Id, port);
                startInfo.ArgumentList.Add("-p");
                startInfo.ArgumentList.Add($"{hostPort}:{port.TargetPort}/{NormalizeContainerPublishProtocol(port.Protocol)}");
            }
        }

        foreach (var port in GetRuntimeContainerProbePorts(definition, service))
        {
            var hostPort = ResolveReplicaProbeLocalPort(definition.Id, port, instance);
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add($"{hostPort}:{port.TargetPort}/{NormalizeContainerPublishProtocol(port.Protocol)}");
        }

        var environmentVariables = await ResolveApplicationEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            resourceManager,
            includeAspNetCoreProjectVariables: false,
            cancellationToken);
        environmentVariables = ApplyRuntimeContainerTelemetryScopeEnvironmentVariables(
            definition,
            instance,
            environmentVariables);
        foreach (var variable in environmentVariables)
        {
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"{variable.Name}={variable.Value}");
        }

        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"CLOUDSHELL_RESOURCE_ID={definition.Id}");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"CLOUDSHELL_REPLICA_ORDINAL={instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}");
        var volumeMaterializations = CreateLocalContainerVolumeMaterializations(
            service.ServiceVolumeMounts,
            resourceManager,
            environment.ContentRootPath);
        foreach (var volumeMaterialization in volumeMaterializations)
        {
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add(volumeMaterialization.Argument);
        }
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
        if (!string.IsNullOrWhiteSpace(imagePlatform))
        {
            startInfo.ArgumentList.Add("--platform");
            startInfo.ArgumentList.Add(imagePlatform);
        }

        startInfo.ArgumentList.Add(imageReference);

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
            await StartContainerAppIngressAsync(
                definition,
                engine,
                service,
                processLog,
            cancellationToken,
            procedureContext);
        }
    }

    private static bool IsSameResourceGroup(
        string? candidateResourceGroupId,
        string? resourceGroupId) =>
        string.Equals(
            NormalizeGroupId(candidateResourceGroupId),
            NormalizeGroupId(resourceGroupId),
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<EnvironmentVariableAssignment> CreateServiceDiscoveryEndpointEnvironmentVariables(
        Resource resource)
    {
        foreach (var binding in ApplicationServiceDiscoveryDisplay.GetEndpointBindings(resource))
        {
            yield return new EnvironmentVariableAssignment(
                binding.EnvironmentVariableName,
                binding.Address);
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
        if (ShouldUseContainerAppIngress(service))
        {
            await StopContainerAppIngressAsync(
                definition,
                engine,
                log,
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
                removeContainer: false,
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

    private async Task StartContainerAppIngressAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ResourceOrchestratorService service,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsContainerAppIngressPort)
            .ToArray();
        if (ingressPorts.Length == 0)
        {
            return;
        }

        var ingressName = GetContainerAppIngressName(service);
        var configurationDirectory = await WriteContainerAppIngressConfigurationAsync(
            definition,
            service,
            cancellationToken,
            procedureContext);

        await ApplicationContainerHostCommands.RunAsync(
            engine,
            ["rm", "-f", ingressName],
            log,
            cancellationToken,
            dockerHostLogger);

        var arguments = new List<string>
        {
            "run",
            "-d",
            "--name",
            ingressName,
            "--network",
            DefaultContainerNetworkName
        };

        foreach (var port in ingressPorts)
        {
            var hostPort = ResolveLocalPort(definition.Id, port);
            arguments.Add("-p");
            arguments.Add($"{hostPort.ToString(CultureInfo.InvariantCulture)}:{hostPort.ToString(CultureInfo.InvariantCulture)}/{NormalizeContainerPublishProtocol(port.Protocol)}");
        }

        arguments.Add("-v");
        arguments.Add($"{configurationDirectory}:/etc/traefik/dynamic:ro");
        arguments.Add(options.ReplicatedContainerAppIngressImage);
        arguments.Add("--providers.file.directory=/etc/traefik/dynamic");
        arguments.Add("--providers.file.watch=true");

        foreach (var port in ingressPorts)
        {
            var entrypoint = CreateContainerAppIngressEntrypoint(port);
            var hostPort = ResolveLocalPort(definition.Id, port);
            arguments.Add($"--entrypoints.{entrypoint}.address=:{hostPort.ToString(CultureInfo.InvariantCulture)}");
        }

        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.ingress.starting",
            $"Application provider is starting ingress '{ingressName}' for '{definition.Name}'.");
        await ApplicationContainerHostCommands.RunAsync(
            engine,
            arguments,
            log,
            cancellationToken,
            dockerHostLogger);
        log.Append(
            $"Started replicated container app ingress '{ingressName}' for {definition.Name}.",
            "process",
            "Information");
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.ingress.started",
            $"Application provider started ingress '{ingressName}' for '{definition.Name}'.");
    }

    private async Task<string> WriteContainerAppIngressConfigurationAsync(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorService service,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsContainerAppIngressPort)
            .ToArray();
        var configurationDirectory = GetContainerAppIngressConfigurationDirectory(definition.Id);
        Directory.CreateDirectory(configurationDirectory);
        var configurationPath = Path.Combine(configurationDirectory, "dynamic.yml");
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.ingress.configuring",
            $"Application provider is writing ingress configuration for '{definition.Name}'.");
        await File.WriteAllTextAsync(
            configurationPath,
            CreateContainerAppIngressConfiguration(service, ingressPorts),
            cancellationToken);

        return configurationDirectory;
    }

    private async Task StopContainerAppIngressAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ResourceProcedureContext? procedureContext = null)
    {
        var service = CreateDefaultContainerOrchestratorService(definition);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.ingress.stopping",
            $"Application provider is stopping ingress '{GetContainerAppIngressName(service)}' for '{definition.Name}'.");
        await StopContainerAppIngressAsync(
            service,
            engine,
            log,
            cancellationToken,
            dockerHostLogger);
        procedureContext?.AppendProviderEvent(
            Id,
            "application.container.ingress.stopped",
            $"Application provider stopped ingress '{GetContainerAppIngressName(service)}' for '{definition.Name}'.");
    }

    private static async Task StopContainerAppIngressAsync(
        ResourceOrchestratorService service,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken,
        ILogger? logger = null)
    {
        await ApplicationContainerHostCommands.RunAsync(
            engine,
            ["rm", "-f", GetContainerAppIngressName(service)],
            log,
            cancellationToken,
            logger);
    }

    private string GetContainerAppIngressConfigurationDirectory(string resourceId)
    {
        var root = Path.IsPathRooted(options.IngressConfigurationDirectory)
            ? options.IngressConfigurationDirectory
            : Path.GetFullPath(options.IngressConfigurationDirectory, environment.ContentRootPath);
        var directoryName = SlugPattern()
            .Replace(resourceId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(root, string.IsNullOrWhiteSpace(directoryName) ? "container-app" : directoryName);
    }

    private static string CreateContainerAppIngressConfiguration(
        ResourceOrchestratorService service,
        IReadOnlyList<ServicePort> ports)
    {
        var httpPorts = ports
            .Where(port => NormalizeProtocol(port.Protocol) == "http")
            .ToArray();
        var tcpPorts = ports
            .Where(port => NormalizeProtocol(port.Protocol) == "tcp")
            .ToArray();
        var builder = new StringBuilder();

        if (httpPorts.Length > 0)
        {
            builder.AppendLine("http:");
            builder.AppendLine("  routers:");
            foreach (var port in httpPorts)
            {
                var routeId = CreateContainerAppIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [\"{CreateContainerAppIngressEntrypoint(port)}\"]");
                builder.AppendLine("      rule: \"PathPrefix(`/`)\"");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      service: \"{routeId}\"");
            }

            builder.AppendLine("  services:");
            foreach (var port in httpPorts)
            {
                var routeId = CreateContainerAppIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine("      loadBalancer:");
                builder.AppendLine("        servers:");
                var replicaGroup = CreateDefaultContainerReplicaGroup(service);
                foreach (var instance in replicaGroup.Instances)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - url: \"http://{CreateContainerAppIngressTargetName(service, instance)}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
                }
            }
        }

        if (tcpPorts.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("tcp:");
            builder.AppendLine("  routers:");
            foreach (var port in tcpPorts)
            {
                var routeId = CreateContainerAppIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      entryPoints: [\"{CreateContainerAppIngressEntrypoint(port)}\"]");
                builder.AppendLine("      rule: \"HostSNI(`*`)\"");
                builder.AppendLine(CultureInfo.InvariantCulture, $"      service: \"{routeId}\"");
            }

            builder.AppendLine("  services:");
            foreach (var port in tcpPorts)
            {
                var routeId = CreateContainerAppIngressRouteId(service, port);
                builder.AppendLine(CultureInfo.InvariantCulture, $"    {routeId}:");
                builder.AppendLine("      loadBalancer:");
                builder.AppendLine("        servers:");
                var replicaGroup = CreateDefaultContainerReplicaGroup(service);
                foreach (var instance in replicaGroup.Instances)
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - address: \"{CreateContainerAppIngressTargetName(service, instance)}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
                }
            }
        }

        return builder.ToString();
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

    private static string FormatApplicationResourceName(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.Name)
            ? application.Id
            : application.Name;

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

    private bool ShouldUseContainerAppIngress(ResourceOrchestratorService service) =>
        options.EnableReplicatedContainerAppIngress &&
        service.Replicas > 1 &&
        service.ServicePorts.Any(IsContainerAppIngressPort);

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

    private static bool IsContainerAppIngressPort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "tcp";

    private static bool IsHttpProbePort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "https";

    private static string GetContainerAppIngressName(ResourceOrchestratorService service) =>
        $"{service.Name}-ingress";

    private static string CreateContainerAppIngressTargetName(
        ResourceOrchestratorService service,
        ResourceOrchestratorServiceInstance instance)
    {
        const int maxDnsLabelLength = 63;
        if (instance.Name.Length <= maxDnsLabelLength)
        {
            return instance.Name;
        }

        var serviceName = CreateStableIdentifier(service.Name);
        if (serviceName.Length > 40)
        {
            serviceName = serviceName[..40].Trim('-');
        }

        var hash = ApplicationResourceHash.StableHash(instance.Name).ToString("x8", CultureInfo.InvariantCulture);
        var replica = Math.Max(1, instance.ReplicaOrdinal).ToString(CultureInfo.InvariantCulture);
        return $"{serviceName}-r{replica}-{hash}";
    }

    private static string CreateContainerAppIngressEntrypoint(ServicePort port) =>
        CreateStableIdentifier(string.IsNullOrWhiteSpace(port.Name) ? $"port-{port.TargetPort}" : port.Name);

    private static string CreateContainerAppIngressRouteId(
        ResourceOrchestratorService service,
        ServicePort port) =>
        CreateStableIdentifier($"{service.Name}-{port.Name}-{port.TargetPort.ToString(CultureInfo.InvariantCulture)}");

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

    private static string FormatRequestedReplicas(int? requestedReplicas) =>
        requestedReplicas is { } value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "unchanged";

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        NormalizeContainerRegistry(application.ContainerRegistry);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static ProjectContainerImageReference CreateProjectContainerImageReference(
        ApplicationResourceDefinition definition)
    {
        var repository = GetContainerServiceName(definition.Id);
        var tag = GetEffectiveContainerRevision(definition);
        var registry = GetImageRegistryAddress(GetEffectiveContainerRegistry(definition));
        var reference = IsDockerHubRegistry(registry)
            ? $"{repository}:{tag}"
            : $"{registry}/{repository}:{tag}";
        return new ProjectContainerImageReference(reference, repository, tag);
    }

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

        Process? process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Container registry login could not be started.");
        try
        {
            ApplicationContainerHostCommands.LogStarted(logger, process, engine, command, commandLine);
            await process.StandardInput.WriteLineAsync(password.AsMemory(), cancellationToken);
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

    private static string? NormalizeGroupId(string? resourceGroupId) =>
        string.IsNullOrWhiteSpace(resourceGroupId) ? null : resourceGroupId;

    private static string GetResourceName(string resourceId) =>
        ResourceId.TryParse(resourceId, out var id) && !string.IsNullOrWhiteSpace(id.Name)
            ? id.Name
            : resourceId;

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    private sealed record ProjectContainerImageReference(
        string Reference,
        string Repository,
        string Tag);
}
