using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Client.Authentication;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceProvider(
    ApplicationResourceStore store,
    ApplicationRuntimeStateStore runtimeStates,
    LocalProcessRunner localProcesses,
    ApplicationProviderOptions options,
    IHostEnvironment environment,
    IServiceProvider serviceProvider,
    IEnumerable<IResourceEnvironmentVariableProvider> environmentVariableProviders,
    IEnumerable<IConfigurationEntryReferenceResolver> configurationEntryResolvers,
    IEnumerable<ISecretReferenceResolver> secretResolvers,
    ResourceDeclarationStore declarations,
    ILogger<ApplicationResourceProvider>? logger = null,
    IResourceEventSink? resourceEvents = null) :
    IResourceProvider,
    ILogProvider,
    IResourceProcedureProvider,
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider,
    IResourceTemplateProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider,
    IResourceOrchestrationDescriptorProvider,
    IResourceOrchestratorServiceProcedureProvider,
    IResourceActionAvailabilityProvider,
    IResourceAppSettingConfigurationProvider,
    IResourceEnvironmentVariableConfigurationProvider,
    IHostScopedResourceCleanupProvider,
    IDisposable
{
    private static readonly JsonSerializerOptions TemplateSerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan StartingStateTimeout = TimeSpan.FromMinutes(5);
    private const string DefaultContainerNetworkName = "cloudshell";
    public const string HiddenResourceEnvironmentVariable = "CloudShell__ResourceManager__Hidden";

    private readonly ConcurrentDictionary<string, ApplicationProcessState> _processes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ApplicationResourceProvider> _logger =
        logger ?? NullLogger<ApplicationResourceProvider>.Instance;

    public string Id => "applications";

    public string DisplayName => "Applications";

    public IReadOnlyList<Resource> GetResources() => store
        .GetApplications()
        .Where(application => !IsHidden(application))
        .Select(CreateResource)
        .ToArray();

    public IReadOnlyList<LogDescriptor> GetLogs() => store
        .GetApplications()
        .SelectMany(CreateLogDescriptors)
        .ToArray();

    public async Task<IReadOnlyList<LogEntry>> ReadLogAsync(
        string logId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        if (TryGetApplicationLogId(logId, out var applicationId))
        {
            var entries = await localProcesses.ReadLogAsync(applicationId, maxEntries, before, cancellationToken);
            return entries
                .Where(IsConsoleLogEntry)
                .ToArray();
        }

        return [];
    }

    public async IAsyncEnumerable<LogEntry> StreamLogAsync(
        string logId,
        int initialEntries = 50,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!TryGetApplicationLogId(logId, out var applicationId))
        {
            yield break;
        }

        await foreach (var entry in localProcesses.StreamLogAsync(
                           applicationId,
                           initialEntries,
                           cancellationToken))
        {
            if (IsConsoleLogEntry(entry))
            {
                yield return entry;
            }
        }
    }

    private static IReadOnlyList<LogDescriptor> CreateLogDescriptors(ApplicationResourceDefinition application)
    {
        return
        [
            new LogDescriptor(
                GetLogId(application.Id),
                "Console logs",
                "Applications",
                application.Name,
                LogSourceKind.Resource,
                ResourceId: application.Id,
                SupportsStreaming: true,
                Description: "Container app or process stdout and stderr.")
        ];
    }

    public async Task SetupApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeDefinition(
            string.IsNullOrWhiteSpace(definition.Id)
                ? definition with { Id = CreateUniqueImportId(definition.Name) }
                : definition);
        store.Save(normalized);

        await registrations.RegisterAsync(
            Id,
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            normalized.DependsOn,
            cancellationToken);
    }

    public async Task UpdateApplicationAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var existing = store.GetApplication(definition.Id);
        if (existing is null)
        {
            throw new InvalidOperationException($"Application resource '{definition.Id}' is not configured.");
        }

        var normalized = NormalizeDefinition(definition);

        store.Save(normalized);
        await registrations.AssignToGroupAsync(
            normalized.Id,
            NormalizeGroupId(resourceGroupId),
            normalized.DependsOn,
            cancellationToken);
    }

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
            case ResourceActionKind.Start:
                await StartApplicationAsync(
                    context.Resource.Id,
                    context.Resource.DependsOn,
                    context.ResourceGroupId,
                    context.Registrations,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken);
                return ResourceProcedureResult.Completed($"Started {context.Resource.Name}.");
            case ResourceActionKind.Stop:
                await StopApplicationAsync(
                    context.Resource.Id,
                    force: true,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken);
                return ResourceProcedureResult.Completed($"Stopped {context.Resource.Name}.");
            case ResourceActionKind.Restart:
                await StopApplicationAsync(
                    context.Resource.Id,
                    force: true,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken);
                await StartApplicationAsync(
                    context.Resource.Id,
                    context.Resource.DependsOn,
                    context.ResourceGroupId,
                    context.Registrations,
                    context.ResourceManager,
                    context.PreferredContainerHostId,
                    cancellationToken);
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

    public bool CanConfigureEnvironmentVariables(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanConfigureAppSettings(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null &&
        action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart;

    public Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (action.Kind is not (ResourceActionKind.Start or ResourceActionKind.Restart))
        {
            return Task.FromResult<string?>(null);
        }

        var application = store.GetApplication(context.Resource.Id);
        if (application is null)
        {
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult(GetReferenceUnavailableReason(application, context));
    }

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

    public async Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        if (action.Kind != ResourceActionKind.Start)
        {
            return;
        }

        var application = GetContainerApplication(context.ResourceContext.Resource.Id);
        var engine = await ResolveRequiredContainerHostAsync(
            application,
            context.ResourceContext.ResourceManager,
            context.ResourceContext.PreferredContainerHostId,
            cancellationToken);
        var processLog = GetProcessLog(application.Id);

        if (action.Kind is ResourceActionKind.Stop && ShouldUseContainerAppIngress(context.Service))
        {
            await StopContainerAppIngressAsync(
                application,
                engine,
                processLog,
                cancellationToken);
            return;
        }

        await LoginToContainerRegistryAsync(
            engine,
            GetEffectiveContainerRegistry(application),
            application.ContainerRegistryCredentials,
            processLog,
            cancellationToken);

        foreach (var network in context.Service.ServiceNetworks
                     .Where(network => !string.IsNullOrWhiteSpace(network))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await EnsureContainerNetworkAsync(
                engine,
                network,
                processLog,
                cancellationToken);
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
                return;
            case ResourceActionKind.Stop:
                await StopContainerApplicationInstanceAsync(
                    application,
                    context.ResourceContext.ResourceManager,
                    context.ResourceContext.PreferredContainerHostId,
                    context.Instance,
                    cancellationToken);
                return;
            default:
                throw new NotSupportedException(
                    $"Container app services do not support action '{action.DisplayName}'.");
        }
    }

    public IReadOnlyList<EnvironmentVariableAssignment> GetConfiguredEnvironmentVariables(string resourceId) =>
        store.GetApplication(resourceId)?.EnvironmentVariables ?? [];

    public IReadOnlyList<AppSetting> GetConfiguredAppSettings(string resourceId) =>
        store.GetApplication(resourceId)?.AppSettings ?? [];

    public async Task<ResourceProcedureResult> UpdateAppSettingsAsync(
        ResourceProcedureContext context,
        IReadOnlyList<AppSetting> appSettings,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{context.Resource.Id}' is not configured.");
        var dependencies = GetConfigurationDependencyResourceIds(
                application.DependsOn,
                appSettings,
                application.EnvironmentVariables)
            .ToArray();
        var definition = application with
        {
            AppSettings = appSettings,
            DependsOn = dependencies
        };
        var restartRequired =
            IsRunning(application.Id) &&
            !application.AppSettings.SequenceEqual(appSettings);

        await UpdateApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        AppendConfigurationEvent(
            application.Id,
            ResourceEventTypes.Events.Configuration.AppSettingsUpdated,
            $"Updated {appSettings.Count} app setting{Pluralize(appSettings.Count)}.");

        return restartRequired
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                "App settings updated.",
                application.Id,
                "The resource is running. Restart it now to apply the app setting changes.")
            : ResourceProcedureResult.Completed("App settings updated.");
    }

    public async Task<ResourceProcedureResult> UpdateEnvironmentVariablesAsync(
        ResourceProcedureContext context,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{context.Resource.Id}' is not configured.");
        var dependencies = GetConfigurationDependencyResourceIds(
                application.DependsOn,
                application.AppSettings,
                environmentVariables)
            .ToArray();
        var definition = application with
        {
            EnvironmentVariables = environmentVariables,
            DependsOn = dependencies
        };
        var restartRequired =
            IsRunning(application.Id) &&
            !application.EnvironmentVariables.SequenceEqual(environmentVariables);

        await UpdateApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        AppendConfigurationEvent(
            application.Id,
            ResourceEventTypes.Events.Configuration.EnvironmentVariablesUpdated,
            $"Updated {environmentVariables.Count} environment variable{Pluralize(environmentVariables.Count)}.");

        return restartRequired
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                "Environment variables updated.",
                application.Id,
                "The resource is running. Restart it now to apply the environment changes.")
            : ResourceProcedureResult.Completed("Environment variables updated.");
    }

    public async Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);

        var application = store.GetApplication(context.Resource.Id)
            ?? throw new InvalidOperationException(
                $"Container app resource '{context.Resource.Id}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{context.Resource.Id}' is not a container app.");
        }

        var normalizedImage = image.Trim();
        if (string.Equals(application.ContainerImage, normalizedImage, StringComparison.Ordinal))
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses image '{normalizedImage}'.");
        }

        var wasRunning = IsRunning(application.Id);
        var nextRevision = CreateContainerRevision();
        var updated = NormalizeDefinition(application with
        {
            ContainerImage = normalizedImage,
            ContainerBuildContext = null,
            ContainerDockerfile = null,
            ContainerRevision = nextRevision
        });
        store.Save(updated);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            "containerApp.imageChanged",
            $"Changed container image from '{application.ContainerImage ?? "none"}' to '{normalizedImage}' and created revision '{updated.ContainerRevision}'.",
            DateTimeOffset.UtcNow,
            triggeredBy));

        if (restartIfRunning && wasRunning)
        {
            await StopApplicationAsync(
                application.Id,
                force: true,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken);
            await StartApplicationAsync(
                application.Id,
                context.Resource.DependsOn,
                context.ResourceGroupId,
                context.Registrations,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken);
            resourceEvents?.Append(new ResourceEvent(
                application.Id,
                "containerApp.restarted",
                $"Restarted container app on revision '{updated.ContainerRevision}' after image update.",
                DateTimeOffset.UtcNow,
                triggeredBy));

            return ResourceProcedureResult.Completed(
                $"Updated {application.Name} to image '{normalizedImage}' and restarted it.");
        }

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                $"Updated {application.Name} to image '{normalizedImage}'.",
                application.Id,
                "The container app is running. Restart it to use the new image.")
            : ResourceProcedureResult.Completed(
                $"Updated {application.Name} to image '{normalizedImage}'.");
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

        if (application.Replicas == replicas)
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses {replicas} replica{Pluralize(replicas)}.");
        }

        var wasRunning = IsRunning(application.Id);
        var updated = NormalizeDefinition(application with
        {
            Replicas = replicas
        });

        if (restartIfRunning && wasRunning)
        {
            await StopApplicationAsync(
                application.Id,
                force: true,
                context.ResourceManager,
                context.PreferredContainerHostId,
                cancellationToken);
        }

        store.Save(updated);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            "containerApp.replicasChanged",
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
                cancellationToken);
            resourceEvents?.Append(new ResourceEvent(
                application.Id,
                "containerApp.restarted",
                $"Restarted container app with {updated.Replicas} replica{Pluralize(updated.Replicas)}.",
                DateTimeOffset.UtcNow,
                triggeredBy));

            return ResourceProcedureResult.Completed(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)} and restarted it.");
        }

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRestartRequired(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.",
                application.Id,
                "The container app is running. Restart it to apply the replica count.")
            : ResourceProcedureResult.Completed(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.");
    }

    private void AppendConfigurationEvent(
        string resourceId,
        string eventType,
        string message) =>
        resourceEvents?.Append(new ResourceEvent(
            resourceId,
            eventType,
            message,
            DateTimeOffset.UtcNow));

    public bool CanExport(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public Task<ResourceTemplateDefinition> ExportAsync(
        Resource resource,
        ResourceTemplateExportContext context,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var configuration = new ApplicationResourceTemplateConfiguration(
            application.ExecutablePath,
            application.Arguments,
            application.WorkingDirectory,
            application.Endpoint,
            application.EnvironmentVariables,
            application.Lifetime,
            application.References,
            application.UseServiceDiscovery,
            application.AppSettings,
            GetEffectiveObservability(application),
            application.ContainerImage,
            IsContainerBacked(application) ? GetEffectiveContainerRegistry(application) : null,
            application.ContainerBuildContext,
            application.ContainerDockerfile,
            application.ContainerHostId,
            application.Replicas,
            application.EndpointPorts,
            application.ProjectPath,
            application.ProjectArguments,
            application.AspNetCoreHotReload);

        return Task.FromResult(new ResourceTemplateDefinition(
            application.Name,
            Id,
            application.ResourceType,
            resource.DependsOn,
            "1.0",
            JsonSerializer.SerializeToElement(configuration, TemplateSerializerOptions),
            application.Id));
    }

    public bool CanImport(ResourceTemplateDefinition template) =>
        string.Equals(template.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        ApplicationResourceTypes.IsApplication(template.ResourceType) &&
        string.Equals(template.ProviderConfigurationVersion, "1.0", StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceTemplateImportResult> ImportAsync(
        ResourceTemplateDefinition template,
        ResourceTemplateImportContext context,
        CancellationToken cancellationToken = default)
    {
        if (!CanImport(template))
        {
            throw new InvalidOperationException("The application resource template is not supported.");
        }

        var configuration = template.Configuration.Deserialize<ApplicationResourceTemplateConfiguration>(
            TemplateSerializerOptions)
            ?? throw new InvalidOperationException("The application resource template configuration is invalid.");

        var resourceId = string.IsNullOrWhiteSpace(template.ResourceId)
            ? CreateUniqueImportId(template.Name)
            : ValidateAvailableImportId(template.ResourceId);
        var definition = new ApplicationResourceDefinition(
            resourceId,
            template.Name,
            configuration.ExecutablePath,
            arguments: configuration.Arguments,
            workingDirectory: configuration.WorkingDirectory,
            endpoint: configuration.Endpoint,
            environmentVariables: configuration.EnvironmentVariables,
            appSettings: configuration.AppSettings,
            lifetime: configuration.Lifetime,
            dependsOn: context.DependsOn,
            references: configuration.References,
            useServiceDiscovery: configuration.UseServiceDiscovery,
            containerImage: configuration.ContainerImage,
            containerRegistry: configuration.ContainerRegistry,
            containerBuildContext: configuration.ContainerBuildContext,
            containerDockerfile: configuration.ContainerDockerfile,
            containerHostId: configuration.ContainerHostId,
            replicas: configuration.Replicas,
            endpointPorts: configuration.EndpointPorts,
            resourceType: template.ResourceType,
            observability: configuration.Observability,
            projectPath: configuration.ProjectPath,
            projectArguments: configuration.ProjectArguments,
            aspNetCoreHotReload: configuration.AspNetCoreHotReload);

        await SetupApplicationAsync(
            definition,
            context.ResourceGroupId,
            context.Registrations,
            cancellationToken);

        return new ResourceTemplateImportResult(
            resourceId,
            $"Imported application resource '{template.Name}'.");
    }

    public ApplicationResourceDefinition? GetApplication(string id) => store.GetApplication(id);

    public IReadOnlyList<ApplicationResourceDefinition> GetApplications() => store.GetApplications();

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase);

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: true,
            StartAsDependency: true,
            StartAfterCreate: false);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        var declaredApplication = options.DeclaredApplications.FirstOrDefault(application =>
            string.Equals(application.Definition.Id, declaration.ResourceId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Application resource declaration '{declaration.ResourceId}' was not found.");

        if (!declaration.OverwritePersistedState &&
            (registrations.GetRegistration(declaration.ResourceId) is not null ||
             store.GetApplication(declaration.ResourceId) is not null))
        {
            return Task.CompletedTask;
        }

        return SetupApplicationAsync(
            declaredApplication.Definition with { DependsOn = declaration.DependsOn },
            declaration.ResourceGroupId,
            registrations,
            cancellationToken);
    }

    public bool IsRunning(string applicationId) =>
        store.GetApplication(applicationId) is { } application &&
        (IsContainerBacked(application)
            ? TryGetRunningProcess(application, out _)
            : localProcesses.IsRunning(CreateLocalProcessDefinition(application)));

    public bool CanDescribe(Resource resource) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public async Task CleanupHostScopedResourcesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var application in store.GetApplications())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (application.Lifetime != ApplicationLifetime.ControlPlaneScoped ||
                IsContainerBacked(application))
            {
                continue;
            }

            await localProcesses.CleanupHostScopedProcessAsync(
                CreateLocalProcessDefinition(application),
                cancellationToken);
            LogDevelopmentLifecycle(
                "Reconciled host-scoped application resource {ResourceId} ({ResourceType}) during Control Plane startup.",
                application.Id,
                application.ResourceType);
        }
    }

    public Task<ResourceOrchestrationDescriptor> DescribeAsync(
        Resource resource,
        ResourceOrchestrationDescriptorContext context,
        CancellationToken cancellationToken = default)
    {
        var application = store.GetApplication(resource.Id)
            ?? throw new InvalidOperationException($"Application resource '{resource.Id}' is not configured.");

        var workload = CreateWorkloadConfiguration(application);
        return Task.FromResult(new ResourceOrchestrationDescriptor(
            resource.Id,
            resource.EffectiveTypeId,
            resource.DependsOn,
            [],
            resource.Endpoints,
            "1.0",
            JsonSerializer.SerializeToElement(workload, TemplateSerializerOptions)));
    }

    private void LogDevelopmentLifecycle(string message, params object?[] args)
    {
        if (environment.IsDevelopment())
        {
            _logger.LogInformation(message, args);
        }
    }

    public void Dispose()
    {
        foreach (var (applicationId, state) in _processes)
        {
            try
            {
                if (state.Lifetime == ApplicationLifetime.ControlPlaneScoped &&
                    !state.Process.HasExited)
                {
                    LogDevelopmentLifecycle(
                        "Stopping host-scoped application resource {ResourceId} during Control Plane shutdown.",
                        applicationId);
                    ProcessShutdown.KillProcessTreeAndWait(state.Process);
                    runtimeStates.Save(new ApplicationRuntimeState(
                        applicationId,
                        state.Process.Id,
                        null,
                        DateTimeOffset.UtcNow,
                        TryGetExitCode(state.Process),
                        state.LogPath));
                    LogDevelopmentLifecycle(
                        "Stopped host-scoped application resource {ResourceId} during Control Plane shutdown.",
                        applicationId);
                }
            }
            catch (InvalidOperationException)
            {
            }

            state.Process.Dispose();
        }
    }

    private async Task StartApplicationAsync(
        string applicationId,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken)
    {
        var definition = store.GetApplication(applicationId)
            ?? throw new InvalidOperationException($"Application resource '{applicationId}' is not configured.");

        LogDevelopmentLifecycle(
            "Starting application resource {ResourceId} ({ResourceType}) with {Lifetime} lifetime.",
            definition.Id,
            definition.ResourceType,
            definition.Lifetime);

        if (IsContainerBacked(definition))
        {
            if (TryGetRunningProcess(definition, out _))
            {
                LogDevelopmentLifecycle(
                    "Application resource {ResourceId} ({ResourceType}) is already running.",
                    definition.Id,
                    definition.ResourceType);
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
                    cancellationToken);
                LogDevelopmentLifecycle(
                    "Started application resource {ResourceId} ({ResourceType}).",
                    definition.Id,
                    definition.ResourceType);
            }
            catch
            {
                ClearStarting(definition);
                throw;
            }
            return;
        }

        var localProcess = CreateLocalProcessDefinition(
            definition,
            await ResolveLocalProcessEnvironmentVariablesAsync(
                definition,
                dependsOn,
                resourceGroupId,
                registrations,
                cancellationToken));
        if (localProcesses.IsRunning(localProcess))
        {
            LogDevelopmentLifecycle(
                "Application resource {ResourceId} ({ResourceType}) is already running.",
                definition.Id,
                definition.ResourceType);
            return;
        }

        MarkStarting(definition.Id);
        try
        {
            await localProcesses.StartAsync(
                localProcess,
                cancellationToken);
            LogDevelopmentLifecycle(
                "Started application resource {ResourceId} ({ResourceType}).",
                definition.Id,
                definition.ResourceType);
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
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken) =>
        ResolveApplicationEnvironmentVariablesAsync(
            definition,
            dependsOn,
            resourceGroupId,
            registrations,
            includeAspNetCoreProjectVariables: true,
            cancellationToken);

    private IReadOnlyList<EnvironmentVariableAssignment> ResolveAspNetCoreProjectEnvironmentVariables(
        ApplicationResourceDefinition definition)
    {
        if (!string.Equals(
                definition.ResourceType,
                ApplicationResourceTypes.AspNetCoreProject,
                StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var urls = CreateEndpoints(definition)
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint.Address))
            .Where(endpoint => !endpoint.Protocol.Equals("process", StringComparison.OrdinalIgnoreCase))
            .Where(endpoint => !endpoint.Address.StartsWith("process://", StringComparison.OrdinalIgnoreCase))
            .Select(endpoint => endpoint.Address)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return urls.Length == 0
            ? []
            : [new EnvironmentVariableAssignment("ASPNETCORE_URLS", string.Join(';', urls))];
    }

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
        IResourceRegistrationStore registrations)
    {
        var references = definition.References
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Where(reference => IsSameResourceGroup(registrations.GetRegistration(reference), resourceGroupId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (references.Count == 0)
        {
            return [];
        }

        return serviceProvider
            .GetServices<IResourceProvider>()
            .SelectMany(provider => provider.GetResources())
            .Where(resource => references.Contains(resource.Id))
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
                CreateServiceDiscoveryConfigurationSegment(definition.Name),
                CreateServiceDiscoveryConfigurationSegment(definition.Id)) ?? definition.Id),
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
        ApplicationResourceDefinition definition) =>
        ResolveObservabilityEnvironmentVariables(definition)
            .Concat(definition.EnvironmentVariables)
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private async Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveApplicationEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        bool includeAspNetCoreProjectVariables,
        CancellationToken cancellationToken)
    {
        var configuredVariables = await ResolveConfiguredEnvironmentVariablesAsync(
            definition,
            resourceGroupId,
            cancellationToken);

        return ResolveDependencyEnvironmentVariables(definition, dependsOn)
            .Concat(definition.UseServiceDiscovery
                ? ResolveServiceDiscoveryEnvironmentVariables(definition, resourceGroupId, registrations)
                : [])
            .Concat(ResolveObservabilityEnvironmentVariables(definition))
            .Concat(includeAspNetCoreProjectVariables
                ? ResolveAspNetCoreProjectEnvironmentVariables(definition)
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
        var tokenEndpoint = options.ResourceIdentityTokenEndpoint;
        if (string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            return [];
        }

        var declaration = declarations.GetDeclaration(definition.Id);
        if (declaration?.IdentityBinding is null)
        {
            return [];
        }

        var providerCatalog = declarations.CreateIdentityProviderCatalog(
            new ResourceIdentityProviderCatalog());
        var resolution = providerCatalog.Resolve(declaration.IdentityBinding);
        if (resolution.Provider?.Kind != ResourceIdentityProviderKind.BuiltIn)
        {
            return [];
        }

        var identity = ResourceIdentityReference.ForResource(
            definition.Id,
            declaration.IdentityBinding.Name);
        var clientId = CreateResourceIdentityClientId(identity);
        var scope = declaration.IdentityBinding.IdentityScopes.Count == 0
            ? string.IsNullOrWhiteSpace(options.ResourceIdentityDefaultScope)
                ? "ControlPlane.Access"
                : options.ResourceIdentityDefaultScope
            : declaration.IdentityBinding.IdentityScopes[0];

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

    private async Task<IReadOnlyList<EnvironmentVariableAssignment>> ResolveConfiguredEnvironmentVariablesAsync(
        ApplicationResourceDefinition definition,
        string? resourceGroupId,
        CancellationToken cancellationToken)
    {
        var context = new ResourceSettingResolutionContext(
            definition.Id,
            resourceGroupId,
            "run",
            ResolveIdentity(definition.Id));
        var variables = new List<EnvironmentVariableAssignment>();

        foreach (var setting in definition.AppSettings)
        {
            var value = await ResolveSettingValueAsync(
                setting.Name,
                setting.Value,
                setting.ConfigurationEntry,
                setting.Secret,
                context,
                cancellationToken);
            variables.Add(new EnvironmentVariableAssignment(setting.Name, value));
        }

        foreach (var variable in definition.EnvironmentVariables)
        {
            var value = await ResolveSettingValueAsync(
                variable.Name,
                variable.Value,
                variable.ConfigurationEntry,
                variable.Secret,
                context,
                cancellationToken);
            variables.Add(new EnvironmentVariableAssignment(variable.Name, value));
        }

        return variables;
    }

    private async Task<string> ResolveSettingValueAsync(
        string name,
        string? literalValue,
        ConfigurationEntryReference? configurationEntry,
        SecretReference? secret,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        if (configurationEntry is not null)
        {
            return ResolveConfigurationEntryValue(name, configurationEntry, context);
        }

        if (secret is not null)
        {
            return await ResolveSecretValueAsync(name, secret, context, cancellationToken);
        }

        return literalValue ?? string.Empty;
    }

    private string ResolveConfigurationEntryValue(
        string name,
        ConfigurationEntryReference reference,
        ResourceSettingResolutionContext context)
    {
        var errors = new List<string>();
        foreach (var resolver in configurationEntryResolvers)
        {
            var result = resolver.ResolveConfigurationEntry(reference, context);
            if (result.IsResolved)
            {
                return result.Value ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        var message = errors.Count == 0
            ? $"No configuration provider can resolve entry '{reference.EntryName}' from '{reference.StoreResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(name, "configuration-entry", message);
    }

    private async Task<string> ResolveSecretValueAsync(
        string name,
        SecretReference reference,
        ResourceSettingResolutionContext context,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var resolver in secretResolvers)
        {
            var result = await resolver.ResolveSecretAsync(reference, context, cancellationToken);
            if (result.IsResolved)
            {
                return result.Value ?? string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                errors.Add(result.ErrorMessage);
            }
        }

        var message = errors.Count == 0
            ? $"No vault provider can resolve secret '{reference.SecretName}' from '{reference.VaultResourceId}'."
            : string.Join(" ", errors);
        throw new ResourceSettingResolutionException(name, "secret", message);
    }

    private ResourceIdentityReference? ResolveIdentity(string resourceId)
    {
        var declaration = declarations.GetDeclaration(resourceId);
        return declaration?.IdentityBinding is null
            ? null
            : ResourceIdentityReference.ForResource(resourceId, declaration.IdentityBinding.Name);
    }

    private string? GetReferenceUnavailableReason(
        ApplicationResourceDefinition definition,
        ResourceProcedureContext context)
    {
        foreach (var setting in definition.AppSettings)
        {
            var reason = GetReferenceUnavailableReason(
                setting.Name,
                setting.ConfigurationEntry,
                setting.Secret,
                definition,
                context);
            if (reason is not null)
            {
                return reason;
            }
        }

        foreach (var variable in definition.EnvironmentVariables)
        {
            var reason = GetReferenceUnavailableReason(
                variable.Name,
                variable.ConfigurationEntry,
                variable.Secret,
                definition,
                context);
            if (reason is not null)
            {
                return reason;
            }
        }

        return null;
    }

    private string? GetReferenceUnavailableReason(
        string settingName,
        ConfigurationEntryReference? configurationEntry,
        SecretReference? secret,
        ApplicationResourceDefinition definition,
        ResourceProcedureContext context)
    {
        if (configurationEntry is not null)
        {
            return GetConfigurationReferenceUnavailableReason(
                settingName,
                configurationEntry,
                definition,
                context);
        }

        if (secret is not null)
        {
            return GetSecretReferenceUnavailableReason(
                settingName,
                secret,
                definition,
                context);
        }

        return null;
    }

    private string? GetConfigurationReferenceUnavailableReason(
        string settingName,
        ConfigurationEntryReference reference,
        ApplicationResourceDefinition definition,
        ResourceProcedureContext context)
    {
        var target = context.ResourceManager?.GetResource(reference.StoreResourceId);
        if (target is null)
        {
            return $"Setting '{settingName}' references configuration store '{reference.StoreResourceId}', but that resource is not available.";
        }

        return GetIdentityGrantUnavailableReason(
            settingName,
            reference.StoreResourceId,
            "configuration entries",
            ConfigurationStoreResourceOperationPermissions.ReadEntries,
            target,
            definition);
    }

    private string? GetSecretReferenceUnavailableReason(
        string settingName,
        SecretReference reference,
        ApplicationResourceDefinition definition,
        ResourceProcedureContext context)
    {
        var target = context.ResourceManager?.GetResource(reference.VaultResourceId);
        if (target is null)
        {
            return $"Setting '{settingName}' references Secrets Vault '{reference.VaultResourceId}', but that resource is not available.";
        }

        return GetIdentityGrantUnavailableReason(
            settingName,
            reference.VaultResourceId,
            "secrets",
            SecretsVaultResourceOperationPermissions.ReadSecrets,
            target,
            definition);
    }

    private string? GetIdentityGrantUnavailableReason(
        string settingName,
        string referencedResourceId,
        string readableItemLabel,
        string permission,
        Resource target,
        ApplicationResourceDefinition definition)
    {
        var identity = ResolveIdentity(definition.Id);
        if (identity is null)
        {
            return null;
        }

        var result = declarations
            .CreatePermissionGrantEvaluator()
            .Evaluate(identity, target.Id, permission);
        if (result.IsAllowed)
        {
            return null;
        }

        return $"Setting '{settingName}' references '{referencedResourceId}', but identity '{FormatIdentity(identity)}' is not allowed to read {readableItemLabel}. Grant '{permission}' on resource '{target.Id}'.";
    }

    private static string FormatIdentity(ResourceIdentityReference identity) =>
        string.IsNullOrWhiteSpace(identity.Name)
            ? identity.ResourceId
            : $"{identity.ResourceId}/{identity.Name}";

    private async Task StartContainerApplicationAsync(
        ApplicationResourceDefinition definition,
        IReadOnlyList<string> dependsOn,
        string? resourceGroupId,
        IResourceRegistrationStore registrations,
        IResourceManagerStore resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken)
    {
        var service = CreateDefaultContainerOrchestratorService(definition);
        await PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                new ResourceProcedureContext(
                    CreateResource(definition),
                    null,
                    resourceGroupId,
                    registrations,
                    resourceManager,
                    preferredContainerHostId),
                service),
            ResourceAction.Start,
            cancellationToken);
        foreach (var instance in CreateDefaultContainerServiceInstances(service))
        {
            await StartContainerApplicationInstanceAsync(
                definition,
                dependsOn,
                resourceGroupId,
                registrations,
                resourceManager,
                preferredContainerHostId,
                service,
                instance,
                cancellationToken);
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
        CancellationToken cancellationToken)
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
            cancellationToken);
        var logPath = GetLogPath(definition.Id);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var processLog = new ApplicationProcessLog(logPath);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            await RunContainerHostCommandAsync(
                engine,
                ["rm", "-f", instance.Name],
                processLog,
                cancellationToken);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = GetContainerHostExecutable(engine),
            WorkingDirectory = Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        ConfigureContainerHostEnvironment(startInfo, engine);
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--name");
        startInfo.ArgumentList.Add(instance.Name);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            startInfo.ArgumentList.Add("--rm");
        }

        var network = service.ServiceNetworks.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
        if (!string.IsNullOrWhiteSpace(network))
        {
            startInfo.ArgumentList.Add("--network");
            startInfo.ArgumentList.Add(network);
        }

        var useIngress = ShouldUseContainerAppIngress(service);
        if (instance.ReplicaOrdinal == 1)
        {
            foreach (var port in service.ServicePorts.Where(port => !useIngress || !IsContainerAppIngressPort(port)))
            {
                var hostPort = ResolveLocalPort(definition.Id, port);
                startInfo.ArgumentList.Add("-p");
                startInfo.ArgumentList.Add($"{hostPort}:{port.TargetPort}/{NormalizeContainerPublishProtocol(port.Protocol)}");
            }
        }

        foreach (var variable in await ResolveApplicationEnvironmentVariablesAsync(
                     definition,
                     dependsOn,
                     resourceGroupId,
                     registrations,
                     includeAspNetCoreProjectVariables: false,
                     cancellationToken))
        {
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"{variable.Name}={variable.Value}");
        }

        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"CLOUDSHELL_RESOURCE_ID={definition.Id}");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"CLOUDSHELL_REPLICA_ORDINAL={instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}");
        startInfo.ArgumentList.Add(CreateRegistryImageReference(
            GetEffectiveContainerRegistry(definition),
            definition.ContainerImage));

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
                logPath));
        };

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var startedAt = TryGetStartTime(process);
        runtimeStates.Save(new ApplicationRuntimeState(
            definition.Id,
            process.Id,
            startedAt,
            DateTimeOffset.UtcNow,
            LogPath: logPath));

        processLog.Append(
            $"Started container image '{definition.ContainerImage}' as '{instance.Name}' replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} of {instance.ReplicaCount.ToString(CultureInfo.InvariantCulture)} using {engine.Name} with {definition.Lifetime} lifetime.",
            "process",
            "Information");

        _processes[definition.Id] = new ApplicationProcessState(
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
                cancellationToken);
        }
    }

    private static bool IsSameResourceGroup(
        ResourceRegistration? registration,
        string? resourceGroupId) =>
        registration is not null &&
        string.Equals(
            NormalizeGroupId(registration.ResourceGroupId),
            NormalizeGroupId(resourceGroupId),
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<EnvironmentVariableAssignment> CreateServiceDiscoveryEndpointEnvironmentVariables(
        Resource resource)
    {
        var serviceNames = CreateServiceDiscoveryServiceNames(resource).ToArray();
        if (serviceNames.Length == 0)
        {
            yield break;
        }

        foreach (var endpoint in resource.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Address) ||
                endpoint.Protocol.Equals("process", StringComparison.OrdinalIgnoreCase) ||
                endpoint.Address.StartsWith("process://", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var endpointKey in CreateServiceDiscoveryEndpointKeys(endpoint))
            {
                foreach (var serviceName in serviceNames)
                {
                    yield return new EnvironmentVariableAssignment(
                        $"services__{serviceName}__{endpointKey}__0",
                        endpoint.Address);
                }
            }
        }
    }

    private static IEnumerable<string> CreateServiceDiscoveryServiceNames(Resource resource)
    {
        var names = new[]
            {
                CreateServiceDiscoveryConfigurationSegment(resource.Name),
                CreateServiceDiscoveryConfigurationSegment(resource.Id)
            }
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            yield return name;
        }
    }

    private static IEnumerable<string> CreateServiceDiscoveryEndpointKeys(ResourceEndpoint endpoint)
    {
        var keys = new[]
            {
                CreateServiceDiscoveryConfigurationSegment(endpoint.Name),
                CreateServiceDiscoveryConfigurationSegment(endpoint.Protocol)
            }
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var key in keys)
        {
            yield return key;
        }
    }

    private async Task StopApplicationAsync(
        string applicationId,
        bool force,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken)
    {
        var application = store.GetApplication(applicationId);
        var log = GetProcessLog(applicationId);

        if (application is not null)
        {
            LogDevelopmentLifecycle(
                "Stopping application resource {ResourceId} ({ResourceType}) with {Lifetime} lifetime.",
                application.Id,
                application.ResourceType,
                application.Lifetime);
        }

        if (application is not null &&
            !IsContainerBacked(application))
        {
            await localProcesses.StopAsync(
                CreateLocalProcessDefinition(application),
                force,
                cancellationToken);
            LogDevelopmentLifecycle(
                "Stopped application resource {ResourceId} ({ResourceType}).",
                application.Id,
                application.ResourceType);
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
                cancellationToken);
            if (engine is not null)
            {
                await StopContainerAsync(application, engine, log, cancellationToken);
                LogDevelopmentLifecycle(
                    "Stopped application resource {ResourceId} ({ResourceType}).",
                    application.Id,
                    application.ResourceType);
            }
        }

        if (!TryGetRunningProcess(application, out var process))
        {
            return;
        }

        log.Append(force ? "Stopping process." : "Stopping control-plane-scoped process.", "process", "Information");
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync(cancellationToken);
        runtimeStates.Save(new ApplicationRuntimeState(
            applicationId,
            process.Id,
            null,
            DateTimeOffset.UtcNow,
            TryGetExitCode(process),
            GetLogPath(applicationId)));
    }

    private async Task StopContainerAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        var service = CreateDefaultContainerOrchestratorService(definition);
        if (ShouldUseContainerAppIngress(service))
        {
            await StopContainerAppIngressAsync(
                definition,
                engine,
                log,
                cancellationToken);
        }

        foreach (var instance in CreateDefaultContainerServiceInstances(service))
        {
            await StopContainerApplicationInstanceAsync(
                definition,
                engine,
                log,
                instance,
                cancellationToken);
        }
    }

    private async Task StopContainerApplicationInstanceAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        ResourceOrchestratorServiceInstance instance,
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
            cancellationToken);
        if (engine is null)
        {
            return;
        }

        await StopContainerApplicationInstanceAsync(
            definition,
            engine,
            GetProcessLog(definition.Id),
            instance,
            cancellationToken);
    }

    private async Task StopContainerApplicationInstanceAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        ResourceOrchestratorServiceInstance instance,
        CancellationToken cancellationToken)
    {
        await RunContainerHostCommandAsync(
            engine,
            ["stop", instance.Name],
            log,
            cancellationToken);
        if (definition.Lifetime == ApplicationLifetime.ControlPlaneScoped)
        {
            await RunContainerHostCommandAsync(
                engine,
                ["rm", "-f", instance.Name],
                log,
                cancellationToken);
        }
    }

    private async Task StartContainerAppIngressAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ResourceOrchestratorService service,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        var ingressPorts = service.ServicePorts
            .Where(IsContainerAppIngressPort)
            .ToArray();
        if (ingressPorts.Length == 0)
        {
            return;
        }

        var ingressName = GetContainerAppIngressName(service);
        var configurationDirectory = GetContainerAppIngressConfigurationDirectory(definition.Id);
        Directory.CreateDirectory(configurationDirectory);
        var configurationPath = Path.Combine(configurationDirectory, "dynamic.yml");
        await File.WriteAllTextAsync(
            configurationPath,
            CreateContainerAppIngressConfiguration(service, ingressPorts),
            cancellationToken);

        await RunContainerHostCommandAsync(
            engine,
            ["rm", "-f", ingressName],
            log,
            cancellationToken);

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

        await RunContainerHostCommandAsync(
            engine,
            arguments,
            log,
            cancellationToken);
        log.Append(
            $"Started replicated container app ingress '{ingressName}' for {definition.Name}.",
            "process",
            "Information");
    }

    private Task StopContainerAppIngressAsync(
        ApplicationResourceDefinition definition,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        var service = CreateDefaultContainerOrchestratorService(definition);
        return StopContainerAppIngressAsync(
            service,
            engine,
            log,
            cancellationToken);
    }

    private static async Task StopContainerAppIngressAsync(
        ResourceOrchestratorService service,
        ContainerHostDescriptor engine,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        await RunContainerHostCommandAsync(
            engine,
            ["rm", "-f", GetContainerAppIngressName(service)],
            log,
            cancellationToken);
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
                foreach (var instance in ResourceOrchestratorServiceInstances.CreateDefaultInstances(service))
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - url: \"http://{instance.Name}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
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
                foreach (var instance in ResourceOrchestratorServiceInstances.CreateDefaultInstances(service))
                {
                    builder.AppendLine(CultureInfo.InvariantCulture, $"          - address: \"{instance.Name}:{port.TargetPort.ToString(CultureInfo.InvariantCulture)}\"");
                }
            }
        }

        return builder.ToString();
    }

    private static async Task EnsureContainerNetworkAsync(
        ContainerHostDescriptor engine,
        string network,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        await RunContainerHostCommandAsync(
            engine,
            ["network", "create", network],
            log,
            cancellationToken);
    }

    private static async Task<int> RunContainerHostCommandAsync(
        ContainerHostDescriptor engine,
        IReadOnlyList<string> arguments,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = GetContainerHostExecutable(engine),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ConfigureContainerHostEnvironment(startInfo, engine);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return -1;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            if (!string.IsNullOrWhiteSpace(output))
            {
                log.Append(output.Trim(), "process", "Information");
            }

            if (!string.IsNullOrWhiteSpace(error) &&
                !error.Contains("No such container", StringComparison.OrdinalIgnoreCase) &&
                !error.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                log.Append(error.Trim(), "process", process.ExitCode == 0 ? "Information" : "Warning");
            }

            return process.ExitCode;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Append(exception.Message, "process", "Warning");
            return -1;
        }
    }

    private Resource CreateResource(ApplicationResourceDefinition application)
    {
        var state = GetState(application.Id);
        var endpoints = CreateEndpoints(application);
        return new Resource(
            application.Id,
            application.Name,
            GetResourceKind(application),
            DisplayName,
            "local",
            state,
            endpoints,
            ApplicationResourceTypes.IsContainerApp(application.ResourceType)
                ? GetEffectiveContainerRevision(application)
                : IsContainerBacked(application)
                    ? FirstNonEmpty(application.ContainerImage, application.ContainerBuildContext) ?? "container"
                : IsAspNetCoreProject(application)
                    ? FirstNonEmpty(Path.GetFileName(application.ProjectPath), "project") ?? "project"
                : Path.GetFileName(application.ExecutablePath),
            DateTimeOffset.UtcNow,
            application.DependsOn,
            TypeId: application.ResourceType,
            Actions: CreateActions(state),
            HealthChecks: application.HealthChecks,
            Observability: GetEffectiveObservability(application),
            ResourceClass: GetResourceClass(application),
            Attributes: CreateAttributes(application),
            Capabilities: CreateCapabilities(endpoints));
    }

    private static IReadOnlyList<ResourceCapability> CreateCapabilities(
        IReadOnlyList<ResourceEndpoint> endpoints)
    {
        var capabilities = new List<ResourceCapability>
        {
            new(ResourceCapabilityIds.EnvironmentVariables)
        };

        if (endpoints.Count > 0)
        {
            capabilities.Add(new(ResourceCapabilityIds.EndpointSource));
        }

        return capabilities;
    }

    private static IReadOnlyDictionary<string, string> CreateAttributes(ApplicationResourceDefinition application)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.WorkloadKind] = CreateWorkloadKind(application),
            [ResourceAttributeNames.EndpointCount] = application.EndpointPorts.Count.ToString(CultureInfo.InvariantCulture)
        };

        if (IsAspNetCoreProject(application))
        {
            AddIfNotEmpty(attributes, ResourceAttributeNames.ProjectPath, application.ProjectPath);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ProjectArguments, application.ProjectArguments);
            attributes[ResourceAttributeNames.ProjectHotReload] =
                application.AspNetCoreHotReload.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();
        }
        else if (IsContainerBacked(application))
        {
            attributes[ResourceAttributeNames.ContainerReplicas] =
                Math.Max(1, application.Replicas).ToString(CultureInfo.InvariantCulture);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerImage, application.ContainerImage);
            attributes[ResourceAttributeNames.ContainerRegistry] = GetEffectiveContainerRegistry(application);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerBuildContext, application.ContainerBuildContext);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerDockerfile, application.ContainerDockerfile);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerHostId, application.ContainerHostId);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ContainerRevision, GetEffectiveContainerRevision(application));
        }
        else
        {
            AddIfNotEmpty(attributes, ResourceAttributeNames.ExecutablePath, application.ExecutablePath);
            AddIfNotEmpty(attributes, ResourceAttributeNames.ExecutableArguments, application.Arguments);
            AddIfNotEmpty(attributes, ResourceAttributeNames.WorkingDirectory, application.WorkingDirectory);
        }

        return attributes;
    }

    private static string CreateWorkloadKind(ApplicationResourceDefinition application)
    {
        if (IsAspNetCoreProject(application))
        {
            return ResourceWorkloadKind.AspNetCoreProject.ToString();
        }

        if (!string.IsNullOrWhiteSpace(application.ContainerImage))
        {
            return ResourceWorkloadKind.ContainerImage.ToString();
        }

        if (!string.IsNullOrWhiteSpace(application.ContainerBuildContext))
        {
            return ResourceWorkloadKind.ContainerBuild.ToString();
        }

        return ResourceWorkloadKind.LocalExecutable.ToString();
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

    private static ResourceClass GetResourceClass(ApplicationResourceDefinition application) =>
        application.ResourceType switch
        {
            ApplicationResourceTypes.AspNetCoreProject => ResourceClass.Project,
            var type when ApplicationResourceTypes.IsContainerApp(type) => ResourceClass.Container,
            ApplicationResourceTypes.SqlServer => ResourceClass.Container,
            _ => IsContainerBacked(application) ? ResourceClass.Container : ResourceClass.Executable
        };

    private static bool IsAspNetCoreProject(ApplicationResourceDefinition application) =>
        string.Equals(
            application.ResourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase);

    private static string GetResourceKind(ApplicationResourceDefinition application) =>
        application.ResourceType switch
        {
            ApplicationResourceTypes.AspNetCoreProject => "ASP.NET Core project",
            var type when ApplicationResourceTypes.IsContainerApp(type) => "Container app",
            ApplicationResourceTypes.SqlServer => "SQL Server",
            _ => IsContainerBacked(application) ? "Container app" : "Executable application"
        };

    private ResourceState GetState(string applicationId)
    {
        var runtimeState = runtimeStates.Get(applicationId);
        if (runtimeState?.State is ResourceState.Starting &&
            DateTimeOffset.UtcNow - runtimeState.LastObservedAt <= StartingStateTimeout)
        {
            return ResourceState.Starting;
        }

        return IsRunning(applicationId)
            ? ResourceState.Running
            : ResourceState.Stopped;
    }

    private static IReadOnlyList<ResourceAction> CreateActions(ResourceState state) =>
        state is ResourceState.Running or ResourceState.Starting
            ? [ResourceAction.Stop, ResourceAction.Restart]
            : [ResourceAction.Start];

    private void MarkStarting(string applicationId)
    {
        var state = runtimeStates.Get(applicationId);
        runtimeStates.Save(state is null
            ? new ApplicationRuntimeState(
                applicationId,
                null,
                null,
                DateTimeOffset.UtcNow,
                State: ResourceState.Starting)
            : state with
            {
                LastObservedAt = DateTimeOffset.UtcNow,
                State = ResourceState.Starting
            });
    }

    private void ClearStarting(ApplicationResourceDefinition definition)
    {
        var state = runtimeStates.Get(definition.Id);
        if (state?.State is not ResourceState.Starting ||
            IsRunning(definition.Id))
        {
            return;
        }

        runtimeStates.Save(state with
        {
            LastObservedAt = DateTimeOffset.UtcNow,
            State = ResourceState.Stopped
        });
    }

    private IReadOnlyList<ResourceEndpoint> CreateEndpoints(ApplicationResourceDefinition application)
    {
        if (application.EndpointPorts.Count > 0)
        {
            return application.EndpointPorts
                .Select(port => ResourceEndpoint.FromAddress(
                    port.Name,
                    $"{NormalizeProtocol(port.Protocol)}://localhost:{ResolveLocalPort(application.Id, port)}",
                    NormalizeProtocol(port.Protocol),
                    port.Exposure))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(application.Endpoint))
        {
            if (IsContainerBacked(application))
            {
                return [];
            }

            return [ResourceEndpoint.Logical("process", $"process://{application.Id}", "process")];
        }

        var endpoint = application.Endpoint;
        var protocol = Uri.TryCreate(endpoint, UriKind.Absolute, out var uri)
            ? uri.Scheme
            : "tcp";

        return [ResourceEndpoint.FromAddress("application", endpoint, protocol, ResourceExposureScope.Public)];
    }

    private static ProcessStartInfo CreateScopedStartInfo(ApplicationResourceDefinition definition)
    {
        var command = CreateProcessCommand(definition);
        return new ProcessStartInfo
        {
            FileName = command.ExecutablePath,
            Arguments = command.Arguments ?? string.Empty,
            WorkingDirectory = ResolveWorkingDirectory(definition),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
    }

    private static ProcessStartInfo CreateDetachedStartInfo(
        ApplicationResourceDefinition definition,
        string logPath)
    {
        var command = CreateProcessCommand(definition);
        var workingDirectory = ResolveWorkingDirectory(definition);
        var arguments = command.Arguments ?? string.Empty;

        if (OperatingSystem.IsWindows())
        {
            var windowsShellCommand = $"\"{EscapeWindowsCommandArgument(command.ExecutablePath)}\" {arguments} >> \"{EscapeWindowsCommandArgument(logPath)}\" 2>&1";
            var startInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/s");
            startInfo.ArgumentList.Add("/c");
            startInfo.ArgumentList.Add(windowsShellCommand);
            return startInfo;
        }

        var shellCommand = $"exec {QuoteUnixShellArgument(command.ExecutablePath)} {arguments} >> {QuoteUnixShellArgument(logPath)} 2>&1";
        var unixStartInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        unixStartInfo.ArgumentList.Add("-c");
        unixStartInfo.ArgumentList.Add(shellCommand);
        return unixStartInfo;
    }

    private bool TryGetRunningProcess(
        ApplicationResourceDefinition? definition,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Process? process)
    {
        process = null;
        if (definition is null)
        {
            return false;
        }

        if (_processes.TryGetValue(definition.Id, out var state))
        {
            if (!state.Process.HasExited)
            {
                process = state.Process;
                return true;
            }

            runtimeStates.Save(new ApplicationRuntimeState(
                definition.Id,
                state.Process.Id,
                null,
                DateTimeOffset.UtcNow,
                TryGetExitCode(state.Process),
                state.LogPath));
        }

        var runtimeState = runtimeStates.Get(definition.Id);
        if (runtimeState?.LastKnownProcessId is null ||
            runtimeState.LastKnownProcessStartedAt is null)
        {
            return false;
        }

        try
        {
            var candidate = Process.GetProcessById(runtimeState.LastKnownProcessId.Value);
            if (candidate.HasExited ||
                !ProcessStartMatches(candidate, runtimeState.LastKnownProcessStartedAt.Value))
            {
                return false;
            }

            var logPath = runtimeState.LogPath ?? GetLogPath(definition.Id);
            var log = new ApplicationProcessLog(logPath);
            candidate.EnableRaisingEvents = true;
            candidate.Exited += (_, _) =>
            {
                log.Append(
                    $"Process exited with code {TryGetExitCode(candidate)?.ToString() ?? "unknown"}.",
                    "process",
                    TryGetExitCode(candidate) == 0 ? "Information" : "Error");
                runtimeStates.Save(new ApplicationRuntimeState(
                    definition.Id,
                    candidate.Id,
                    null,
                    DateTimeOffset.UtcNow,
                    TryGetExitCode(candidate),
                    logPath));
            };

            _processes[definition.Id] = new ApplicationProcessState(
                candidate,
                log,
                definition.Lifetime,
                logPath);
            process = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private ApplicationProcessLog GetProcessLog(string applicationId)
    {
        if (_processes.TryGetValue(applicationId, out var state))
        {
            return state.Log;
        }

        return new ApplicationProcessLog(
            runtimeStates.Get(applicationId)?.LogPath ?? GetLogPath(applicationId));
    }

    private string GetLogPath(string applicationId)
    {
        var logDirectory = Path.IsPathRooted(options.LogDirectory)
            ? options.LogDirectory
            : Path.GetFullPath(options.LogDirectory, environment.ContentRootPath);
        var logFileName = SlugPattern()
            .Replace(applicationId.ToLowerInvariant(), "-")
            .Trim('-');

        return Path.Combine(logDirectory, $"{logFileName}.log");
    }

    private static bool ProcessStartMatches(
        Process process,
        DateTimeOffset expectedStartedAt)
    {
        var actualStartedAt = TryGetStartTime(process);
        if (actualStartedAt is null)
        {
            return true;
        }

        return (actualStartedAt.Value - expectedStartedAt).Duration() <= TimeSpan.FromSeconds(2);
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.HasExited ? process.ExitCode : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static ApplicationResourceDefinition NormalizeDefinition(ApplicationResourceDefinition definition)
    {
        var id = string.IsNullOrWhiteSpace(definition.Id)
            ? CreateId(definition.Name)
            : definition.Id.Trim();
        var resourceType = NormalizeResourceType(definition.ResourceType);
        var isAspNetCoreProject = string.Equals(
            resourceType,
            ApplicationResourceTypes.AspNetCoreProject,
            StringComparison.OrdinalIgnoreCase);
        var legacyProjectPath = isAspNetCoreProject
            ? TryExtractProjectPathFromDotNetArguments(definition.Arguments)
            : null;

        return definition with
        {
            Id = id,
            Name = definition.Name.Trim(),
            ExecutablePath = isAspNetCoreProject ? string.Empty : definition.ExecutablePath.Trim(),
            Arguments = isAspNetCoreProject ? null : NormalizeNullable(definition.Arguments),
            WorkingDirectory = NormalizeNullable(definition.WorkingDirectory),
            Endpoint = NormalizeNullable(definition.Endpoint),
            Lifetime = definition.Lifetime,
            UseServiceDiscovery = definition.UseServiceDiscovery,
            ContainerImage = NormalizeNullable(definition.ContainerImage),
            ContainerRegistry = IsContainerBacked(definition)
                ? NormalizeContainerRegistry(definition.ContainerRegistry)
                : null,
            ContainerRegistryCredentials = IsContainerBacked(definition)
                ? ContainerRegistryCredentials.Normalize(definition.ContainerRegistryCredentials)
                : null,
            ContainerBuildContext = NormalizeNullable(definition.ContainerBuildContext),
            ContainerDockerfile = NormalizeNullable(definition.ContainerDockerfile),
            ContainerHostId = NormalizeNullable(definition.ContainerHostId),
            ContainerRevision = NormalizeNullable(definition.ContainerRevision) ??
                (IsContainerBacked(definition) ? CreateContainerRevision() : null),
            Replicas = Math.Max(1, definition.Replicas),
            ResourceType = resourceType,
            ProjectPath = isAspNetCoreProject
                ? NormalizeNullable(definition.ProjectPath) ?? legacyProjectPath
                : null,
            ProjectArguments = isAspNetCoreProject
                ? NormalizeNullable(definition.ProjectArguments) ??
                    TryExtractApplicationArgumentsFromDotNetArguments(definition.Arguments)
                : null,
            AspNetCoreHotReload = isAspNetCoreProject
                ? ResolveAspNetCoreHotReload(definition)
                : definition.AspNetCoreHotReload,
            DependsOn = NormalizeDependencies(definition.DependsOn, id),
            References = NormalizeReferences(definition.References, id),
            EndpointPorts = NormalizeEndpointPorts(definition.EndpointPorts, resourceType, definition.Endpoint),
            HealthChecks = NormalizeHealthChecks(definition.HealthChecks),
            Observability = NormalizeObservability(definition.Observability),
            AppSettings = NormalizeAppSettings(definition.AppSettings),
            EnvironmentVariables = NormalizeEnvironmentVariables(definition.EnvironmentVariables)
        };
    }

    private static IReadOnlyList<AppSetting> NormalizeAppSettings(
        IReadOnlyList<AppSetting> appSettings) =>
        appSettings
            .Where(setting => !string.IsNullOrWhiteSpace(setting.Name))
            .Select(setting => setting with
            {
                Name = setting.Name.Trim(),
                ConfigurationEntry = NormalizeConfigurationEntryReference(setting.ConfigurationEntry),
                Secret = NormalizeSecretReference(setting.Secret)
            })
            .Where(setting => setting.Value is not null ||
                setting.ConfigurationEntry is not null ||
                setting.Secret is not null)
            .ToArray();

    private static IReadOnlyList<EnvironmentVariableAssignment> NormalizeEnvironmentVariables(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables) =>
        environmentVariables
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .Select(variable => variable with
            {
                Name = variable.Name.Trim(),
                ConfigurationEntry = NormalizeConfigurationEntryReference(variable.ConfigurationEntry),
                Secret = NormalizeSecretReference(variable.Secret)
            })
            .Where(variable => variable.ConfigurationEntry is null || variable.Secret is null)
            .ToArray();

    private static IEnumerable<string> GetConfigurationDependencyResourceIds(
        IReadOnlyList<string> existingDependencies,
        IReadOnlyList<AppSetting> appSettings,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables) =>
        existingDependencies
            .Concat(GetAppSettingReferenceResourceIds(appSettings))
            .Concat(GetEnvironmentVariableReferenceResourceIds(environmentVariables))
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> GetAppSettingReferenceResourceIds(IReadOnlyList<AppSetting> appSettings)
    {
        foreach (var setting in appSettings)
        {
            if (!string.IsNullOrWhiteSpace(setting.ConfigurationEntry?.StoreResourceId))
            {
                yield return setting.ConfigurationEntry.StoreResourceId;
            }

            if (!string.IsNullOrWhiteSpace(setting.Secret?.VaultResourceId))
            {
                yield return setting.Secret.VaultResourceId;
            }
        }
    }

    private static IEnumerable<string> GetEnvironmentVariableReferenceResourceIds(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        foreach (var variable in environmentVariables)
        {
            if (!string.IsNullOrWhiteSpace(variable.ConfigurationEntry?.StoreResourceId))
            {
                yield return variable.ConfigurationEntry.StoreResourceId;
            }

            if (!string.IsNullOrWhiteSpace(variable.Secret?.VaultResourceId))
            {
                yield return variable.Secret.VaultResourceId;
            }
        }
    }

    private static ConfigurationEntryReference? NormalizeConfigurationEntryReference(
        ConfigurationEntryReference? reference) =>
        reference is null ||
        string.IsNullOrWhiteSpace(reference.StoreResourceId) ||
        string.IsNullOrWhiteSpace(reference.EntryName)
            ? null
            : reference with
            {
                StoreResourceId = reference.StoreResourceId.Trim(),
                EntryName = reference.EntryName.Trim(),
                Version = NormalizeNullable(reference.Version)
            };

    private static SecretReference? NormalizeSecretReference(SecretReference? reference) =>
        reference is null ||
        string.IsNullOrWhiteSpace(reference.VaultResourceId) ||
        string.IsNullOrWhiteSpace(reference.SecretName)
            ? null
            : reference with
            {
                VaultResourceId = reference.VaultResourceId.Trim(),
                SecretName = reference.SecretName.Trim(),
                Version = NormalizeNullable(reference.Version)
            };

    private static bool ResolveAspNetCoreHotReload(ApplicationResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return definition.AspNetCoreHotReload;
        }

        return definition.Arguments?.TrimStart().StartsWith("watch ", StringComparison.OrdinalIgnoreCase) ?? true;
    }

    private static string? TryExtractProjectPathFromDotNetArguments(string? arguments)
    {
        var tokens = SplitCommandLine(arguments);
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (string.Equals(tokens[index], "--project", StringComparison.OrdinalIgnoreCase))
            {
                return tokens[index + 1];
            }
        }

        return null;
    }

    private static string? TryExtractApplicationArgumentsFromDotNetArguments(string? arguments)
    {
        var separatorIndex = arguments?.IndexOf(" -- ", StringComparison.Ordinal);
        if (separatorIndex is null or < 0)
        {
            return null;
        }

        return NormalizeNullable(arguments![(separatorIndex.Value + 4)..]);
    }

    private static IReadOnlyList<string> SplitCommandLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        foreach (var character in value)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                continue;
            }

            if (character == '\\')
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(character);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    private ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        definition.Observability ??
        (options.EnableObservabilityByDefault
            ? ResourceObservability.Default
            : ResourceObservability.None);

    private static ResourceObservability? NormalizeObservability(ResourceObservability? observability)
    {
        if (observability is null)
        {
            return null;
        }

        var attributes = observability.Attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
            .ToDictionary(
                attribute => attribute.Key.Trim(),
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase);

        return observability with
        {
            OtlpEndpoint = NormalizeNullable(observability.OtlpEndpoint),
            OtlpProtocol = NormalizeNullable(observability.OtlpProtocol),
            OtlpHeaders = NormalizeNullable(observability.OtlpHeaders),
            ServiceName = NormalizeNullable(observability.ServiceName),
            ResourceAttributes = attributes.Count == 0 ? null : attributes
        };
    }

    private static string CreateOtelResourceAttributes(
        ApplicationResourceDefinition definition,
        ResourceObservability observability)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service.instance.id"] = definition.Id,
            ["cloudshell.resource.id"] = definition.Id,
            ["cloudshell.resource.type"] = definition.ResourceType
        };

        foreach (var attribute in observability.Attributes)
        {
            if (!string.IsNullOrWhiteSpace(attribute.Key))
            {
                attributes[attribute.Key.Trim()] = attribute.Value;
            }
        }

        return string.Join(
            ',',
            attributes
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Key))
                .Select(attribute => $"{attribute.Key}={EscapeOtelAttributeValue(attribute.Value)}"));
    }

    private static string EscapeOtelAttributeValue(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);

    private static LocalProcessDefinition CreateLocalProcessDefinition(
        ApplicationResourceDefinition definition,
        IReadOnlyList<EnvironmentVariableAssignment>? environmentVariables = null)
    {
        var command = CreateProcessCommand(definition);
        return new LocalProcessDefinition(
            definition.Id,
            command.ExecutablePath,
            command.Arguments,
            definition.WorkingDirectory,
            environmentVariables ?? definition.EnvironmentVariables,
            ToLocalProcessLifetime(definition.Lifetime));
    }

    private static ApplicationProcessCommand CreateProcessCommand(ApplicationResourceDefinition definition)
    {
        if (!IsAspNetCoreProject(definition))
        {
            return new ApplicationProcessCommand(
                definition.ExecutablePath,
                definition.Arguments);
        }

        if (string.IsNullOrWhiteSpace(definition.ProjectPath))
        {
            return new ApplicationProcessCommand(
                string.IsNullOrWhiteSpace(definition.ExecutablePath) ? "dotnet" : definition.ExecutablePath,
                definition.Arguments);
        }

        return new ApplicationProcessCommand(
            "dotnet",
            BuildDotNetAspNetCoreProjectArguments(
                definition.ProjectPath,
                definition.AspNetCoreHotReload,
                definition.ProjectArguments));
    }

    private static string BuildDotNetAspNetCoreProjectArguments(
        string projectPath,
        bool hotReload,
        string? applicationArguments)
    {
        var runnerArguments = hotReload
            ? $"watch --project {QuoteCommandArgument(projectPath)} run --no-launch-profile"
            : $"run --project {QuoteCommandArgument(projectPath)} --no-launch-profile";

        return string.IsNullOrWhiteSpace(applicationArguments)
            ? runnerArguments
            : $"{runnerArguments} -- {applicationArguments.Trim()}";
    }

    private static LocalProcessLifetime ToLocalProcessLifetime(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => LocalProcessLifetime.ControlPlaneScoped,
            _ => LocalProcessLifetime.Detached
        };

    private ResourceWorkloadConfiguration CreateWorkloadConfiguration(
        ApplicationResourceDefinition application)
    {
        if (IsAspNetCoreProject(application))
        {
            return new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.AspNetCoreProject,
                application.Name,
                WorkingDirectory: application.WorkingDirectory,
                ProjectPath: application.ProjectPath,
                ProjectArguments: application.ProjectArguments,
                AspNetCoreHotReload: application.AspNetCoreHotReload,
                Replicas: Math.Max(1, application.Replicas),
                AppSettings: application.AppSettings,
                EnvironmentVariables: ResolveWorkloadEnvironmentVariables(application),
                Ports: application.EndpointPorts,
                Lifetime: ToResourceLifetime(application.Lifetime),
                Observability: GetEffectiveObservability(application));
        }

        if (!string.IsNullOrWhiteSpace(application.ContainerImage))
        {
            return new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerImage,
                application.Name,
                Image: application.ContainerImage,
                Registry: GetEffectiveContainerRegistry(application),
                ContainerHostId: application.ContainerHostId,
                Replicas: Math.Max(1, application.Replicas),
                AppSettings: application.AppSettings,
                EnvironmentVariables: ResolveWorkloadEnvironmentVariables(application),
                Ports: application.EndpointPorts,
                Lifetime: ToResourceLifetime(application.Lifetime),
                Observability: GetEffectiveObservability(application));
        }

        if (!string.IsNullOrWhiteSpace(application.ContainerBuildContext))
        {
            return new ResourceWorkloadConfiguration(
                ResourceWorkloadKind.ContainerBuild,
                application.Name,
                BuildContext: application.ContainerBuildContext,
                Dockerfile: application.ContainerDockerfile,
                Registry: GetEffectiveContainerRegistry(application),
                ContainerHostId: application.ContainerHostId,
                Replicas: Math.Max(1, application.Replicas),
                AppSettings: application.AppSettings,
                EnvironmentVariables: ResolveWorkloadEnvironmentVariables(application),
                Ports: application.EndpointPorts,
                Lifetime: ToResourceLifetime(application.Lifetime),
                Observability: GetEffectiveObservability(application));
        }

        return new ResourceWorkloadConfiguration(
            ResourceWorkloadKind.LocalExecutable,
            application.Name,
            ExecutablePath: application.ExecutablePath,
            Arguments: application.Arguments,
            WorkingDirectory: application.WorkingDirectory,
            Replicas: Math.Max(1, application.Replicas),
            AppSettings: application.AppSettings,
            EnvironmentVariables: ResolveWorkloadEnvironmentVariables(application),
            Lifetime: ToResourceLifetime(application.Lifetime),
            Observability: GetEffectiveObservability(application));
    }

    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        new(
            application.Id,
            GetContainerServiceName(application.Id),
            CreateWorkloadConfiguration(application),
            Networks: [DefaultContainerNetworkName]);

    private ApplicationResourceDefinition GetContainerApplication(string resourceId)
    {
        var application = store.GetApplication(resourceId)
            ?? throw new InvalidOperationException(
                $"Container app resource '{resourceId}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is not a container app.");
        }

        return application;
    }

    private static IEnumerable<ResourceOrchestratorServiceInstance> CreateDefaultContainerServiceInstances(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorServiceInstances.CreateDefaultInstances(service);

    private async Task<ContainerHostDescriptor> ResolveRequiredContainerHostAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
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
            cancellationToken)
            ?? throw new InvalidOperationException(
                $"Resource '{definition.Name}' is container-backed but no default container host is registered. Use UseDocker(), UseContainerHost(...), or set WithContainerHost(...).");
    }

    private async Task<ContainerHostDescriptor?> ResolveContainerHostAsync(
        string? containerHostId,
        string? preferredContainerHostId,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        var selectedEngineId = FirstNonEmpty(containerHostId, preferredContainerHostId);
        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            return await ResolveContainerHostByIdAsync(selectedEngineId, resourceManager, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Container host '{selectedEngineId}' is not registered.");
        }

        return GetContainerHosts()
            .Where(engine => engine.IsDefault)
            .OrderBy(engine => engine.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?? await ResolveDefaultContainerHostResourceAsync(resourceManager, cancellationToken);
    }

    private async Task<ContainerHostDescriptor?> ResolveContainerHostByIdAsync(
        string engineId,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        var engine = GetContainerHosts()
            .FirstOrDefault(engine => string.Equals(engine.Id, engineId, StringComparison.OrdinalIgnoreCase));
        if (engine is not null)
        {
            return engine;
        }

        var resource = resourceManager.GetResource(engineId);
        if (resource is null)
        {
            return null;
        }

        var descriptor = await TryDescribeContainerHostAsync(resource, resourceManager, cancellationToken);
        return descriptor is null ? null : TryReadContainerHost(descriptor);
    }

    private async Task<ContainerHostDescriptor?> ResolveDefaultContainerHostResourceAsync(
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        foreach (var resource in resourceManager.GetResources())
        {
            var descriptor = await TryDescribeContainerHostAsync(resource, resourceManager, cancellationToken);
            if (descriptor is null)
            {
                continue;
            }

            var engine = TryReadContainerHost(descriptor);
            if (engine?.IsDefault == true)
            {
                return engine;
            }
        }

        return null;
    }

    private async Task<ResourceOrchestrationDescriptor?> TryDescribeContainerHostAsync(
        Resource resource,
        IResourceManagerStore resourceManager,
        CancellationToken cancellationToken)
    {
        var provider = serviceProvider
            .GetServices<IResourceOrchestrationDescriptorProvider>()
            .Where(provider => !ReferenceEquals(provider, this))
            .FirstOrDefault(provider => provider.CanDescribe(resource));
        if (provider is null)
        {
            return null;
        }

        return await provider.DescribeAsync(
            resource,
            new ResourceOrchestrationDescriptorContext(
                null,
                resourceManager.GetGroupForResource(resource.Id),
                resourceManager),
            cancellationToken);
    }

    private static ContainerHostDescriptor? TryReadContainerHost(
        ResourceOrchestrationDescriptor descriptor)
    {
        if (!descriptor.ResourceType.Equals(ContainerHostResourceTypes.ContainerHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            return descriptor.Configuration.Deserialize<ContainerHostDescriptor>(TemplateSerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private IReadOnlyList<ContainerHostDescriptor> GetContainerHosts() =>
        serviceProvider
            .GetServices<IContainerHostProvider>()
            .Select(provider => provider.GetDefaultHost())
            .Where(engine => !string.IsNullOrWhiteSpace(engine.Id))
            .GroupBy(engine => engine.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();

    private int ResolveLocalPort(string resourceId, ServicePort port)
    {
        if (port.Port is not null)
        {
            return Math.Max(1, port.Port.Value);
        }

        var start = Math.Max(1, options.AutoLocalPortStart);
        var end = Math.Max(start, options.AutoLocalPortEnd);
        var range = end - start + 1;
        return start + (int)(StableHash($"{resourceId}:{port.Name}") % (uint)range);
    }

    private static IReadOnlyList<ServicePort> NormalizeEndpointPorts(
        IReadOnlyList<ServicePort> ports,
        string resourceType,
        string? endpoint = null)
    {
        var normalized = ports
            .Where(port => !string.IsNullOrWhiteSpace(port.Name))
            .Select(port => port with
            {
                Name = port.Name.Trim(),
                Protocol = NormalizeProtocol(port.Protocol),
                TargetPort = Math.Max(1, port.TargetPort),
                Port = port.Port is null ? null : Math.Max(1, port.Port.Value)
            })
            .DistinctBy(port => port.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 &&
            string.Equals(resourceType, ApplicationResourceTypes.AspNetCoreProject, StringComparison.OrdinalIgnoreCase)
            ? CreateAspNetCoreProjectEndpointPorts(endpoint)
            : normalized;
    }

    private static IReadOnlyList<ServicePort> NormalizeEndpointPorts(
        IReadOnlyList<ServicePort> ports) =>
        NormalizeEndpointPorts(ports, ApplicationResourceTypes.ExecutableApplication);

    private static IReadOnlyList<ServicePort> CreateAspNetCoreProjectEndpointPorts(string? endpoint)
    {
        if (Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
            uri.Port > 0)
        {
            return
            [
                new ServicePort(
                    "http",
                    uri.Port,
                    uri.Port,
                    string.IsNullOrWhiteSpace(uri.Scheme) ? "http" : uri.Scheme,
                    ResourceExposureScope.Local)
            ];
        }

        return [new ServicePort("http", 80, Protocol: "http", Exposure: ResourceExposureScope.Local)];
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;

        var hash = offset;
        foreach (var character in value)
        {
            hash ^= character;
            hash *= prime;
        }

        return hash;
    }

    private static string GetContainerName(ResourceOrchestratorService service, int replica = 1) =>
        ResourceOrchestratorServiceInstances.CreateDefaultInstanceName(
            service.Name,
            replica,
            service.Replicas);

    private static string GetContainerName(string resourceId, int replica = 1, int replicas = 1)
    {
        var serviceName = GetContainerServiceName(resourceId);
        return ResourceOrchestratorServiceInstances.CreateDefaultInstanceName(
            serviceName,
            replica,
            replicas);
    }

    private static string GetContainerServiceName(string resourceId) =>
        ResourceOrchestratorServiceInstances.CreateDefaultServiceName(resourceId);

    private bool ShouldUseContainerAppIngress(ResourceOrchestratorService service) =>
        options.EnableReplicatedContainerAppIngress &&
        service.Replicas > 1 &&
        service.ServicePorts.Any(IsContainerAppIngressPort);

    private static bool IsContainerAppIngressPort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "tcp";

    private static string GetContainerAppIngressName(ResourceOrchestratorService service) =>
        $"{service.Name}-ingress";

    private static string CreateContainerAppIngressEntrypoint(ServicePort port) =>
        CreateStableIdentifier(string.IsNullOrWhiteSpace(port.Name) ? $"port-{port.TargetPort}" : port.Name);

    private static string CreateContainerAppIngressRouteId(
        ResourceOrchestratorService service,
        ServicePort port) =>
        CreateStableIdentifier($"{service.Name}-{port.Name}-{port.TargetPort.ToString(CultureInfo.InvariantCulture)}");

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

    private static string GetContainerHostExecutable(ContainerHostDescriptor engine) =>
        engine.Kind == ContainerHostKind.Podman ? "podman" : "docker";

    private static void ConfigureContainerHostEnvironment(
        ProcessStartInfo startInfo,
        ContainerHostDescriptor engine)
    {
        if (string.IsNullOrWhiteSpace(engine.Endpoint))
        {
            return;
        }

        if (engine.Kind == ContainerHostKind.Podman)
        {
            startInfo.Environment["CONTAINER_HOST"] = engine.Endpoint;
            return;
        }

        startInfo.Environment["DOCKER_HOST"] = engine.Endpoint;
    }

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string NormalizeContainerPublishProtocol(string? protocol) =>
        NormalizeProtocol(protocol) switch
        {
            "http" or "https" => "tcp",
            "udp" => "udp",
            "sctp" => "sctp",
            _ => "tcp"
        };

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        !string.IsNullOrWhiteSpace(application.ContainerImage) ||
        !string.IsNullOrWhiteSpace(application.ContainerBuildContext);

    private static string GetEffectiveContainerRevision(ApplicationResourceDefinition application) =>
        NormalizeNullable(application.ContainerRevision) ?? "unrevisioned";

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        NormalizeContainerRegistry(application.ContainerRegistry);

    private static string NormalizeContainerRegistry(string? registry) =>
        NormalizeNullable(registry) ?? ContainerRegistryDefaults.Default;

    private static string CreateRegistryImageReference(string registry, string image)
    {
        var imageRegistry = GetImageRegistryAddress(registry);
        var normalizedImage = image.Trim();
        if (normalizedImage.StartsWith($"{imageRegistry}/", StringComparison.OrdinalIgnoreCase) ||
            (IsDockerHubRegistry(imageRegistry) && HasExplicitRegistry(normalizedImage)))
        {
            return normalizedImage;
        }

        return $"{imageRegistry}/{normalizedImage}";
    }

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private static bool IsDockerHubRegistry(string registry) =>
        string.Equals(registry, ContainerRegistryDefaults.DockerHub, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(registry, "index.docker.io", StringComparison.OrdinalIgnoreCase);

    private static bool HasExplicitRegistry(string image)
    {
        var slashIndex = image.IndexOf('/');
        if (slashIndex <= 0)
        {
            return false;
        }

        var firstSegment = image[..slashIndex];
        return firstSegment.Contains('.', StringComparison.Ordinal) ||
            firstSegment.Contains(':', StringComparison.Ordinal) ||
            string.Equals(firstSegment, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task LoginToContainerRegistryAsync(
        ContainerHostDescriptor engine,
        string registry,
        ContainerRegistryCredentials? credentials,
        ApplicationProcessLog log,
        CancellationToken cancellationToken)
    {
        credentials = ContainerRegistryCredentials.Normalize(credentials);
        if (credentials is null)
        {
            return;
        }

        var registryAddress = GetImageRegistryAddress(registry);
        var password = credentials.ResolvePassword();
        var startInfo = new ProcessStartInfo
        {
            FileName = GetContainerHostExecutable(engine),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        ConfigureContainerHostEnvironment(startInfo, engine);
        startInfo.ArgumentList.Add("login");
        startInfo.ArgumentList.Add(registryAddress);
        startInfo.ArgumentList.Add("--username");
        startInfo.ArgumentList.Add(credentials.Username);
        startInfo.ArgumentList.Add("--password-stdin");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Container registry login could not be started.");
        await process.StandardInput.WriteLineAsync(password.AsMemory(), cancellationToken);
        process.StandardInput.Close();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
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

    private static string CreateContainerRevision() =>
        $"rev-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}"[..27];

    private static string NormalizeResourceType(string? resourceType) =>
        ApplicationResourceTypes.IsApplication(resourceType)
            ? resourceType!.Trim()
            : ApplicationResourceTypes.ExecutableApplication;

    private static ResourceLifetime ToResourceLifetime(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => ResourceLifetime.ControlPlaneScoped,
            _ => ResourceLifetime.Detached
        };

    private static IReadOnlyList<ResourceHealthCheck> NormalizeHealthChecks(
        IReadOnlyList<ResourceHealthCheck> healthChecks) =>
        healthChecks
            .Where(check => !string.IsNullOrWhiteSpace(check.Path))
            .Select(check => check with
            {
                Path = check.Path.Trim(),
                EndpointName = string.IsNullOrWhiteSpace(check.EndpointName) ? null : check.EndpointName.Trim(),
                Name = string.IsNullOrWhiteSpace(check.Name) ? check.Type.ToString().ToLowerInvariant() : check.Name.Trim()
            })
            .ToArray();

    private static bool IsHidden(ApplicationResourceDefinition application) =>
        application.EnvironmentVariables.Any(variable =>
            string.Equals(variable.Name, HiddenResourceEnvironmentVariable, StringComparison.OrdinalIgnoreCase) &&
            bool.TryParse(variable.Value, out var hidden) &&
            hidden);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string CreateId(string name)
    {
        var slug = SlugPattern()
            .Replace(name.Trim().ToLowerInvariant(), "-")
            .Trim('-');

        return string.IsNullOrWhiteSpace(slug)
            ? $"application:{Guid.NewGuid():N}"
            : $"application:{slug}";
    }

    private string CreateUniqueImportId(string name) =>
        CreateUniqueId(name, resourceId => store.GetApplication(resourceId) is not null);

    private string ValidateAvailableImportId(string resourceId)
    {
        var normalized = resourceId.Trim();
        if (store.GetApplication(normalized) is not null)
        {
            throw new InvalidOperationException($"Resource id '{normalized}' is already in use.");
        }

        return normalized;
    }

    private static string CreateUniqueId(string name, Func<string, bool> exists)
    {
        var candidate = CreateId(name);
        if (!exists(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        while (exists($"{candidate}-{suffix}"))
        {
            suffix++;
        }

        return $"{candidate}-{suffix}";
    }

    private static string ResolveWorkingDirectory(ApplicationResourceDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.WorkingDirectory))
        {
            return definition.WorkingDirectory;
        }

        var executableDirectory = IsAspNetCoreProject(definition)
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

    private static IReadOnlyList<string> NormalizeDependencies(
        IReadOnlyList<string> dependsOn,
        string resourceId) =>
        dependsOn
            .Where(dependency => !string.IsNullOrWhiteSpace(dependency))
            .Select(dependency => dependency.Trim())
            .Where(dependency => !string.Equals(dependency, resourceId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlyList<string> NormalizeReferences(
        IReadOnlyList<string> references,
        string resourceId) =>
        references
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .Select(reference => reference.Trim())
            .Where(reference => !string.Equals(reference, resourceId, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string QuoteUnixShellArgument(string value) =>
        "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static string QuoteCommandArgument(string argument) =>
        argument.Any(char.IsWhiteSpace)
            ? $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : argument;

    private static string EscapeWindowsCommandArgument(string value) =>
        value.Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string CreateServiceDiscoveryConfigurationSegment(string value) =>
        ServiceDiscoveryConfigurationSegmentPattern()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-');

    private static string GetLogId(string applicationId) => $"{applicationId}:logs";

    private static bool TryGetApplicationLogId(
        string logId,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId) =>
        TryGetApplicationIdFromLogId(logId, ":logs", out applicationId);

    private static bool TryGetApplicationIdFromLogId(
        string logId,
        string suffix,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? applicationId)
    {
        if (logId.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            applicationId = logId[..^suffix.Length];
            return true;
        }

        applicationId = null;
        return false;
    }

    private static bool IsConsoleLogEntry(LogEntry entry) =>
        string.Equals(entry.Source, "stdout", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entry.Source, "stderr", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex SlugPattern();

    [GeneratedRegex("[^a-z0-9_.-]+")]
    private static partial Regex ServiceDiscoveryConfigurationSegmentPattern();

    private sealed record ApplicationProcessState(
        Process Process,
        ApplicationProcessLog Log,
        ApplicationLifetime Lifetime,
        string LogPath);

    private sealed record ApplicationProcessCommand(
        string ExecutablePath,
        string? Arguments);

    private sealed record ApplicationResourceTemplateConfiguration(
        string ExecutablePath,
        string? Arguments,
        string? WorkingDirectory,
        string? Endpoint,
        IReadOnlyList<EnvironmentVariableAssignment> EnvironmentVariables,
        ApplicationLifetime Lifetime,
        IReadOnlyList<string>? References = null,
        bool UseServiceDiscovery = false,
        IReadOnlyList<AppSetting>? AppSettings = null,
        ResourceObservability? Observability = null,
        string? ContainerImage = null,
        string? ContainerRegistry = null,
        string? ContainerBuildContext = null,
        string? ContainerDockerfile = null,
        string? ContainerHostId = null,
        int Replicas = 1,
        IReadOnlyList<ServicePort>? EndpointPorts = null,
        string? ProjectPath = null,
        string? ProjectArguments = null,
        bool AspNetCoreHotReload = true);
}
