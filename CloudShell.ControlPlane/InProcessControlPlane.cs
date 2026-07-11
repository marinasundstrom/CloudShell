using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Notifications;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Usage;
using CloudShell.ControlPlane.DeploymentArtifacts;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Deployment;
using CloudShell.ControlPlane.ResourceManager.Health;
using CloudShell.ControlPlane.ResourceManager.Identity;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.ControlPlane.ResourceManager.Recovery;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ResourceAttributeValue = CloudShell.ResourceModel.ResourceAttributeValue;
using ResourceDefinition = CloudShell.ResourceModel.ResourceDefinition;
using ResourceDefinitionDiagnosticCodes = CloudShell.ResourceModel.ResourceDefinitionDiagnosticCodes;
using ResourceDefinitionDiagnostic = CloudShell.ResourceModel.ResourceDefinitionDiagnostic;
using ResourceDefinitionDiagnosticSeverity = CloudShell.ResourceModel.ResourceDefinitionDiagnosticSeverity;
using ResourceDefinitionTemplate = CloudShell.ResourceModel.ResourceTemplate;
using ResourceDefinitionValidationResult = CloudShell.ResourceModel.ResourceDefinitionValidationResult;
using ResourceGraphCommitContext = CloudShell.ResourceModel.ResourceGraphCommitContext;

namespace CloudShell.ControlPlane;

public sealed class InProcessControlPlane(
    IResourceManagerStore resourceManager,
    IResourceGroupStore resourceGroups,
    IResourceRegistrationStore registrations,
    ResourceDeclarationStore declarations,
    ResourceOrchestrationService orchestration,
    ResourceDeploymentService deployments,
    ResourceReplicaGroupReconciliationService replicaGroupReconciliation,
    ResourceIdentityProvisioningService resourceIdentityProvisioning,
    ResourceIdentityProviderSetupService resourceIdentityProviderSetup,
    ResourceDefinitionTemplateService templates,
    ILogStore logs,
    ITraceStore traces,
    IMetricStore metrics,
    IUsageStore usage,
    IResourceHealthStore resourceHealth,
    IResourceRecoveryStore resourceRecovery,
    ResourceHealthProbeService healthProbes,
    ResourceHealthRefreshCoordinator healthRefreshes,
    IResourceOrchestrationSettings orchestrationSettings,
    IEnumerable<IResourceMonitoringProvider> monitoringProviders,
    ICloudShellAuthorizationService authorization,
    IResourceEventStore? resourceEvents = null,
    IHttpContextAccessor? httpContextAccessor = null,
    ResourceIdentityProviderCatalog? identityProviders = null,
    IEnumerable<IResourceIdentityDirectoryProvider>? identityDirectoryProviders = null,
    IEnumerable<IResourcePermissionGrantStatusProvider>? permissionGrantStatusProviders = null,
    ResourceOrchestratorDeploymentCleanupCoordinator? deploymentCleanup = null,
    IDeploymentArtifactStore? deploymentArtifacts = null,
    IEnumerable<IDeploymentArtifactLayoutProvider>? deploymentArtifactLayoutProviders = null,
    IEnumerable<IDeploymentArtifactValidationProvider>? deploymentArtifactValidationProviders = null,
    IOptions<ResourceManagerOptions>? resourceManagerOptions = null,
    IOptions<DeploymentArtifactOptions>? deploymentArtifactOptions = null,
    IResourceEventSink? resourceEventSink = null,
    ICloudShellNotificationStore? notifications = null,
    IEnumerable<ICloudShellNotificationActionHandler>? notificationActionHandlers = null) : IControlPlane
{
    private const string PreferredUsernameClaimType = "preferred_username";
    private const string UnauthenticatedRequestActor = "user";
    private const string RecoveryTriggeredBy = "recovery";
    private const string LivenessTriggeredBy = "liveness";
    private const string ReplicaManagementTriggeredBy = "replica-management";
    private const string ApplicationDotnetAppType = "application.dotnet-app";
    private const string ApplicationPythonAppType = "application.python-app";
    private const string ApplicationJavaAppType = "application.java-app";
    private const string ApplicationJavaScriptAppType = "application.javascript-app";
    private const string ApplicationGoAppType = "application.go-app";
    private const string ApplicationExecutableType = "application.executable";
    private const string ApplicationContainerAppType = "application.container-app";
    private const string ProjectPathAttribute = "project.path";
    private const string DotnetExecutablePathAttribute = "executablePath";
    private const string ExecutablePathAttribute = "path";

    public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

    private readonly ResourceManagerOptions resourceManagerOptions =
        resourceManagerOptions?.Value ?? new ResourceManagerOptions();
    private readonly DeploymentArtifactOptions deploymentArtifactOptions =
        deploymentArtifactOptions?.Value ?? new DeploymentArtifactOptions();

    private readonly ResourceIdentityProviderCatalog identityProviders =
        identityProviders ?? new ResourceIdentityProviderCatalog();

    private readonly IReadOnlyList<IResourceIdentityDirectoryProvider> identityDirectoryProviders =
        (identityDirectoryProviders ?? []).ToArray();

    private readonly IReadOnlyList<IResourcePermissionGrantStatusProvider> permissionGrantStatusProviders =
        (permissionGrantStatusProviders ?? []).ToArray();
    private readonly ResourceOrchestratorDeploymentCleanupCoordinator deploymentCleanup =
        deploymentCleanup ?? new ResourceOrchestratorDeploymentCleanupCoordinator(resourceEvents);
    private readonly IReadOnlyList<IDeploymentArtifactLayoutProvider> deploymentArtifactLayoutProviders =
        (deploymentArtifactLayoutProviders ?? []).ToArray();
    private readonly IReadOnlyList<IDeploymentArtifactValidationProvider> deploymentArtifactValidationProviders =
        (deploymentArtifactValidationProviders ?? []).ToArray();
    private readonly IReadOnlyList<ICloudShellNotificationActionHandler> notificationActionHandlers =
        (notificationActionHandlers ?? []).ToArray();
    private readonly IResourceEventSink? resourceEventWriter = resourceEventSink ?? resourceEvents;

    public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetResourceGroups());
    }

    public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceManager.GetGroupForResource(resourceId));
    }

    public Task<ResourceGroup> CreateResourceGroupAsync(
        CreateResourceGroupCommand command,
        CancellationToken cancellationToken = default) =>
        resourceGroups.CreateAsync(command.Name, command.Description, cancellationToken);

    public Task<IReadOnlyList<Resource>> ListAvailableResourcesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyLivenessStatus(resourceManager.GetAvailableResources()));
    }

    public Task<IReadOnlyList<Resource>> ListResourcesAsync(
        ResourceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(ApplyLivenessStatus(resourceManager.GetResources()), query));
    }

    public Task<Resource?> GetResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyLivenessStatus(resourceManager.GetResource(resourceId)));
    }

    public Task<IReadOnlyList<Resource>> ListResourceChildrenAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyLivenessStatus(resourceManager.GetChildren(resourceId)));
    }

    public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registrations.GetRegistrations());
    }

    public Task<ResourceRegistration?> GetResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(registrations.GetRegistration(resourceId));
    }

    public async Task CreateResourceAsync(
        CreateResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerId = RequireValue(command.ProviderId, nameof(command.ProviderId));
        var resourceType = RequireValue(command.ResourceType, nameof(command.ResourceType));
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var name = RequireValue(command.Name, nameof(command.Name));
        var configuration = RequireConfiguration(command.Configuration);
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        var resourceClass = ResolveCreationResourceClass(resourceId, resourceType, command.ResourceClass);
        var request = new ResourceCreationRequest(
            resourceType,
            resourceId,
            name,
            configuration,
            resourceGroupId,
            resourceClass,
            NormalizeAttributes(command.ResourceAttributes));
        EnsureResourceGroupExists(resourceGroupId);
        AuthorizeCreateResource(request);

        var provider = resourceManager.Providers
            .OfType<IResourceCreationProvider>()
            .FirstOrDefault(provider =>
                string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase) &&
                provider.CanCreate(request))
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceProviderCannotCreate(providerId, resourceType));

        var triggeredBy = ResolveTriggeredBy(null);
        AppendResourceEvent(
            resourceId,
            ResourceEventTypes.Events.Resource.Creating,
            $"Creating resource '{name}'.",
            triggeredBy);

        try
        {
            await provider.CreateAsync(
                request,
                new ResourceCreationContext(registrations),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendResourceEvent(
                resourceId,
                ResourceEventTypes.Events.Resource.CreateFailed,
                $"Resource '{name}' creation failed. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }

        AppendResourceEvent(
            resourceId,
            ResourceEventTypes.Events.Resource.Created,
            $"Resource '{name}' created.",
            triggeredBy,
            ResourceSignalSeverity.Success);

        if (command.StartAfterCreate)
        {
            await ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    resourceId,
                    ResourceActionIds.Start,
                    StartDependencies: true),
                cancellationToken);
        }

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceCreated,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
        IReadOnlyList<string> resourceIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resources = resourceIds
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(resourceId => resourceManager.GetResource(resourceId))
            .OfType<Resource>()
            .Select(ApplyLivenessStatusToResource)
            .ToArray();
        var capabilities = await Task.WhenAll(resources.Select(resource =>
            CreateCapabilitiesAsync(resource, cancellationToken)));

        return capabilities
            .ToDictionary(
                capability => capability.ResourceId,
                capability => capability,
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<ResourcePrincipal>> QueryResourcePrincipalsAsync(
        ResourcePrincipalQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveIdentityProviders = declarations.CreateIdentityProviderCatalog(identityProviders);
        var principals = new List<ResourcePrincipal>();
        principals.AddRange(CreateResourceIdentityPrincipals(query, effectiveIdentityProviders));

        var directoryProviders = ShouldQueryDirectoryProviders(query)
            ? identityDirectoryProviders
            : [];
        if (directoryProviders.Count > 0)
        {
            var directoryQuery = new ResourceIdentityDirectoryQuery(
                query?.SearchText,
                query?.PrincipalKinds,
                query?.Limit);
            foreach (var provider in effectiveIdentityProviders.Providers)
            {
                if (!MatchesProvider(query, provider.Id))
                {
                    continue;
                }

                var directoryProvider = directoryProviders.FirstOrDefault(directoryProvider =>
                    directoryProvider.CanQueryDirectory(provider));
                if (directoryProvider is null)
                {
                    continue;
                }

                var result = await directoryProvider.QueryDirectoryAsync(
                    new ResourceIdentityDirectoryRequest(provider, directoryQuery),
                    cancellationToken);
                principals.AddRange(result.Principals);
            }
        }

        var filtered = principals
            .Where(principal => MatchesPrincipalQuery(principal, query))
            .GroupBy(
                principal => $"{principal.Reference.ProviderId}\u001f{principal.Reference.Kind}\u001f{principal.Reference.Id}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(principal => principal.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(principal => principal.Reference.Kind)
            .ThenBy(principal => principal.Reference.Id, StringComparer.OrdinalIgnoreCase);

        return query?.Limit is > 0
            ? filtered.Take(query.Limit.Value).ToArray()
            : filtered.ToArray();
    }

    public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ApplyQuery(declarations.GetPermissionGrants(), query));
    }

    public async Task<IReadOnlyList<ResourcePermissionGrantStatus>> ListResourcePermissionGrantStatusesAsync(
        ResourcePermissionGrantQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        var grants = ApplyQuery(declarations.GetPermissionGrants(), query);
        var statuses = new List<ResourcePermissionGrantStatus>();
        foreach (var grant in grants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statuses.Add(await GetPermissionGrantStatusAsync(grant, cancellationToken));
        }

        return statuses;
    }

    public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
        ResourceIdentityReference identity,
        string targetResourceId,
        string permission,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(declarations
            .CreatePermissionGrantEvaluator()
            .Evaluate(identity, targetResourceId, permission));
    }

    public Task GrantResourcePermissionAsync(
        GrantResourcePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);

        var grant = CreatePermissionGrant(
            command.Principal,
            command.TargetResourceId,
            command.Permission);
        declarations.AddPermissionGrant(grant);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourcePermissionGrantsChanged,
            grant.ResourceIdentity?.ResourceId,
            AffectedResourceIds: GetGrantAffectedResourceIds(grant)));

        return Task.CompletedTask;
    }

    public Task RevokeResourcePermissionAsync(
        RevokeResourcePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);

        var grant = CreatePermissionGrant(
            command.Principal,
            command.TargetResourceId,
            command.Permission);
        declarations.RemovePermissionGrant(grant);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourcePermissionGrantsChanged,
            grant.ResourceIdentity?.ResourceId,
            AffectedResourceIds: GetGrantAffectedResourceIds(grant)));

        return Task.CompletedTask;
    }

    public async Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resourceId);
        if (!authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resourceId,
                CloudShellPermissions.Resources.Manage);
        }

        AuthorizeResourceIdentityProvisioning(resourceIdentityProvisioning.CreatePlan(resource.Id));

        return await resourceIdentityProvisioning.ProvisionResourceAsync(
            resource.Id,
            cancellationToken);
    }

    private void AuthorizeCreateResource(ResourceCreationRequest request)
    {
        if (!string.Equals(
                request.ResourceType,
                PlatformResourceProvider.VolumeResourceType,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        VolumeResourceDefinition? definition;
        try
        {
            definition = request.Configuration.Deserialize<VolumeResourceDefinition>();
        }
        catch (JsonException)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                "Volume configuration is invalid."));
        }

        var storageResourceId = NormalizeOptional(definition?.StorageResourceId);
        if (storageResourceId is null)
        {
            return;
        }

        var storageResource = resourceManager.GetResource(storageResourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(storageResourceId));
        var storageGroup = resourceManager.GetGroupForResource(storageResource.Id);
        if (!authorization.CanAccessResource(
                storageResource.Id,
                storageGroup?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                storageResource.Id,
                CloudShellPermissions.Resources.Manage);
        }
    }

    public async Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resourceId);
        if (!authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Read) &&
            !authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resourceId,
                FormatPermissionRequirement(CloudShellPermissions.Resources.Read));
        }

        var plan = resourceIdentityProvisioning.CreatePlan(resource.Id);
        AuthorizeResourceIdentityProvisioningStatus(plan);

        return await resourceIdentityProvisioning.GetResourceStatusAsync(
            resource.Id,
            cancellationToken);
    }

    public async Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
        string providerId,
        CancellationToken cancellationToken = default)
    {
        providerId = RequireValue(providerId, nameof(providerId));
        var provider = resourceIdentityProviderSetup.ResolveProvider(providerId);
        AuthorizeResourceIdentityProviderSetup(provider);

        return await resourceIdentityProviderSetup.SetupAsync(
            provider.Id,
            cancellationToken);
    }

    public async Task RegisterResourceAsync(
        RegisterResourceCommand command,
        CancellationToken cancellationToken = default)
    {
        var providerId = RequireValue(command.ProviderId, nameof(command.ProviderId));
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureProviderExists(providerId);
        EnsureAvailableResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        await registrations.RegisterAsync(
            providerId,
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistered,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task RemoveResourceRegistrationAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        await registrations.RemoveAsync(resourceId, cancellationToken);
        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceRegistrationRemoved,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task AssignResourceGroupAsync(
        AssignResourceGroupCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resourceGroupId = NormalizeOptional(command.ResourceGroupId);
        EnsureRegisteredResourceExists(resourceId);
        EnsureResourceGroupExists(resourceGroupId);

        await registrations.AssignToGroupAsync(
            resourceId,
            resourceGroupId,
            NormalizeOptionalDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceGroupAssigned,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task SetResourceDependenciesAsync(
        SetResourceDependenciesCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        EnsureRegisteredResourceExists(resourceId);

        await registrations.SetDependenciesAsync(
            resourceId,
            NormalizeRequiredDependencies(resourceId, command.DependsOn),
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDependenciesChanged,
            resourceId,
            AffectedResourceIds: [resourceId]));
    }

    public async Task SetResourceIdentityAsync(
        SetResourceIdentityCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resourceId);
        if (!authorization.CanAccessResource(
                resourceId,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resourceId,
                CloudShellPermissions.Resources.Manage);
        }

        await registrations.SetIdentityAsync(
            resource.Id,
            command.Identity,
            cancellationToken);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceIdentityChanged,
            resource.Id,
            AffectedResourceIds: [resource.Id]));
    }

    public async Task<ResourceProcedureResult> DeleteResourceAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        if (GetDirectProcedureProvider(resource) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceDeleteUnsupported(resource.Name));
        }

        var result = await orchestration.DeleteAsync(resource, cancellationToken);
        if (deploymentArtifacts is not null)
        {
            await deploymentArtifacts.DeleteResourceArtifactsAsync(resource.Id, cancellationToken);
        }

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceDeleted,
            resource.Id,
            AffectedResourceIds: [resource.Id]));
        return result;
    }

    public async Task<ResourceProcedureResult> ExecuteResourceActionAsync(
        ExecuteResourceActionCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var actionId = RequireValue(command.ActionId, nameof(command.ActionId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var action = resource.ResourceActions.FirstOrDefault(item =>
            string.Equals(item.Id, actionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceActionNotFound(resource.Id, actionId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        var actionPermission = ResourceActionPermissions.GetRequiredPermission(action);
        var triggeredBy = ResolveTriggeredBy(command.TriggeredBy);
        if (!CanAccessResource(resource.Id, group?.Id, actionPermission, command.ActingIdentity))
        {
            RecordDeniedResourceAction(resource, action, actionPermission, triggeredBy);
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                FormatPermissionRequirement(actionPermission));
        }

        if (GetProcedureProvider(resource) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnsupported(resource.Name));
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
        unavailableReason ??= await orchestration.GetActionUnavailableReasonAsync(
            resource,
            action,
            cancellationToken);
        if (unavailableReason is not null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceActionUnavailable(unavailableReason));
        }

        if (!command.IgnoreDependentWarning && ShouldWarnDependents(action))
        {
            var activeDependents = GetActiveDependents(resource);
            if (activeDependents.Count > 0)
            {
                throw new ControlPlaneException(ControlPlaneError.DependentResourcesRunning(
                    $"The following running resources depend on this resource: {string.Join(", ", activeDependents.Select(dependent => dependent.Name))}. Stopping it may disrupt them."));
            }
        }

        var result = await orchestration.ExecuteActionAsync(
            resource,
            action,
            command.StartDependencies,
            CreateAuthorizationService(command.ActingIdentity),
            cancellationToken,
            triggeredBy,
            cause: command.Cause,
            notifyResourceChange: NotifyResourcesChanged,
            dependencyStartFailureBehavior:
                command.DependencyStartFailureBehavior ?? orchestration.DependencyStartFailureBehavior);

        if (action.Kind == ResourceActionKind.Stop)
        {
            resourceRecovery.ClearRuntimeState(resource.Id);
        }

        return result;
    }

    private void RecordDeniedResourceAction(
        Resource resource,
        ResourceAction action,
        string permission,
        string? triggeredBy)
    {
        AppendResourceEvent(
            resource.Id,
            ResourceEventTypes.Actions.ForFailedAction(action.Id),
            $"{action.DisplayName} action was denied. The '{FormatPermissionRequirement(permission)}' permission is required for resource '{ResourceDisplayLabels.GetName(resource)}'.",
            triggeredBy,
            ResourceSignalSeverity.Warning);
    }

    private string? ResolveTriggeredBy(string? explicitTriggeredBy)
    {
        var httpContext = httpContextAccessor?.HttpContext;
        var user = httpContext?.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            return FindActorClaim(user);
        }

        if (httpContext is not null)
        {
            return UnauthenticatedRequestActor;
        }

        return string.IsNullOrWhiteSpace(explicitTriggeredBy)
            ? null
            : explicitTriggeredBy.Trim();
    }

    private void AppendResourceEvent(
        string resourceId,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info) =>
        resourceEventWriter?.Append(new ResourceEvent(
            resourceId,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));

    private static string? FindActorClaim(ClaimsPrincipal user, string claimType) =>
        user.Claims
            .Where(claim => string.Equals(claim.Type, claimType, StringComparison.Ordinal))
            .Select(claim => claim.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? FindActorClaim(ClaimsPrincipal user) =>
        FindActorClaim(user, PreferredUsernameClaimType) ??
        FindActorClaim(user, ClaimTypes.Upn) ??
        FindActorClaim(user, ClaimTypes.Email) ??
        FindActorClaim(user, ClaimTypes.Name) ??
        FindActorClaim(user, ClaimTypes.NameIdentifier);

    public async Task<ResourceProcedureResult> UpdateResourceImageAsync(
        UpdateResourceImageCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        var image = RequireValue(command.Image, nameof(command.Image));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        var provider = GetImageUpdateProvider(resource);
        if (provider is null || !provider.CanUpdateImage(resource))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceImageUpdateUnsupported(resource.Name));
        }

        var triggeredBy = ResolveTriggeredBy(command.TriggeredBy);
        AppendResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ImageUpdating,
            $"Updating image to '{image}'. Requested replicas: {FormatRequestedReplicas(command.RequestedReplicas)}.",
            triggeredBy);

        ResourceProcedureResult result;
        try
        {
            result = await provider.UpdateImageAsync(
                CreateProcedureContext(resource, triggeredBy),
                image,
                command.RestartIfRunning,
                triggeredBy,
                cancellationToken,
                command.RequestedReplicas);
        }
        catch (InvalidOperationException exception) when (exception is not ControlPlaneException)
        {
            AppendResourceEvent(
                resource.Id,
                ResourceEventTypes.Events.Deployment.ImageUpdateFailed,
                $"Image update to '{image}' failed. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw new ControlPlaneException(
                ControlPlaneError.ResourceImageUpdateUnavailable(exception.Message),
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendResourceEvent(
                resource.Id,
                ResourceEventTypes.Events.Deployment.ImageUpdateFailed,
                $"Image update to '{image}' failed. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }

        var updatedResource = resourceManager.GetResource(resource.Id);
        var revision = updatedResource?.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.ContainerRevision);
        AppendResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            string.IsNullOrWhiteSpace(revision)
                ? $"Updated image to '{image}'."
                : $"Updated image to '{image}' and produced revision '{revision}'. Requested replicas: {FormatRequestedReplicas(command.RequestedReplicas)}.",
            triggeredBy,
            ResourceSignalSeverity.Success);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceImageUpdated,
            resource.Id,
            AffectedResourceIds: [resource.Id]));

        if (updatedResource is not null)
        {
            result = await ApplyImageDeploymentIfRequiredAsync(
                updatedResource,
                result,
                triggeredBy,
                cancellationToken);
        }

        return result;
    }

    private async Task<ResourceProcedureResult> ApplyImageDeploymentIfRequiredAsync(
        Resource resource,
        ResourceProcedureResult result,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        if (!result.RuntimeReconciliationRequired)
        {
            return result;
        }

        var provider = GetDeploymentProvider(resource);
        if (provider is null ||
            !provider.CanDescribeDeployment(resource))
        {
            return result;
        }

        var deployment = await provider.DescribeDeploymentAsync(
            CreateProcedureContext(
                resource,
                triggeredBy,
                "Image deployment requested runtime reconciliation."),
            cancellationToken);
        if (deployment is null)
        {
            return result;
        }

        ResourceOrchestratorDeploymentApplyResult applyResult;
        try
        {
            applyResult = await deployments.ApplyDeploymentAsync(
                resource,
                deployment,
                cancellationToken,
                triggeredBy,
                "Image deployment requested runtime reconciliation.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failureProvider = GetDeploymentFailureProvider(resource);
            if (failureProvider is not null &&
                failureProvider.CanHandleDeploymentApplyFailed(resource))
            {
                await failureProvider.HandleDeploymentApplyFailedAsync(
                    CreateProcedureContext(
                        resource,
                        triggeredBy,
                        "Image deployment requested runtime reconciliation."),
                    deployment,
                    exception,
                    cancellationToken);
            }

            throw;
        }

        var mergedResult = MergeAppliedDeploymentResult(result, applyResult.ProcedureResult);
        var appliedProvider = GetDeploymentAppliedProvider(resource);
        if (appliedProvider is not null &&
            appliedProvider.CanHandleDeploymentApplied(resource))
        {
            await appliedProvider.HandleDeploymentAppliedAsync(
                CreateProcedureContext(
                    resource,
                    triggeredBy,
                    "Image deployment requested runtime reconciliation."),
                applyResult,
                cancellationToken);
        }

        return await RunPostApplyDeploymentTearDownAsync(
            resource,
            applyResult,
            mergedResult,
            triggeredBy,
            "Image deployment requested runtime reconciliation.",
            cancellationToken);
    }

    private static ResourceProcedureResult MergeAppliedDeploymentResult(
        ResourceProcedureResult updateResult,
        ResourceProcedureResult applyResult) =>
        new(
            string.Join(
                " ",
                new[] { updateResult.Message, applyResult.Message }
                    .Where(message => !string.IsNullOrWhiteSpace(message))),
            signals: updateResult.Signals
                .Concat(applyResult.Signals)
                .ToArray());

    private static string FormatRequestedReplicas(int? requestedReplicas) =>
        requestedReplicas is { } value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "unchanged";

    public async Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
        UpdateResourceReplicasCommand command,
        CancellationToken cancellationToken = default)
    {
        var resourceId = RequireValue(command.ResourceId, nameof(command.ResourceId));
        if (command.Replicas < 1)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Replicas must be greater than or equal to 1."));
        }

        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!authorization.CanAccessResource(
                resource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        var provider = GetReplicaUpdateProvider(resource);
        if (provider is null || !provider.CanUpdateReplicas(resource))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceReplicasUpdateUnsupported(resource.Name));
        }

        var triggeredBy = ResolveTriggeredBy(command.TriggeredBy);
        AppendResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ReplicasUpdating,
            $"Updating replicas to '{command.Replicas}'. Restart if running: {command.RestartIfRunning.ToString().ToLowerInvariant()}.",
            triggeredBy);

        ResourceProcedureResult result;
        try
        {
            result = await provider.UpdateReplicasAsync(
                CreateProcedureContext(
                    resource,
                    triggeredBy,
                    "Replica scaling requested runtime reconciliation."),
                command.Replicas,
                command.RestartIfRunning,
                triggeredBy,
                cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception is not ControlPlaneException)
        {
            AppendResourceEvent(
                resource.Id,
                ResourceEventTypes.Events.Deployment.ReplicasUpdateFailed,
                $"Replica update to '{command.Replicas}' failed. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw new ControlPlaneException(
                ControlPlaneError.ResourceReplicasUpdateUnavailable(exception.Message),
                exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendResourceEvent(
                resource.Id,
                ResourceEventTypes.Events.Deployment.ReplicasUpdateFailed,
                $"Replica update to '{command.Replicas}' failed. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }

        AppendResourceEvent(
            resource.Id,
            ResourceEventTypes.Events.Deployment.ReplicasUpdated,
            $"Updated replicas to '{command.Replicas}'. Restart if running: {command.RestartIfRunning.ToString().ToLowerInvariant()}.",
            triggeredBy,
            ResourceSignalSeverity.Success);

        NotifyResourcesChanged(new ResourceChangeNotification(
            ResourceChangeKind.ResourceReplicasUpdated,
            resource.Id,
            AffectedResourceIds: [resource.Id]));

        var updatedResource = resourceManager.GetResource(resource.Id);
        if (updatedResource is not null)
        {
            result = await ApplyReplicaDeploymentIfRequiredAsync(
                updatedResource,
                result,
                triggeredBy,
                cancellationToken);
        }

        return result;
    }

    private async Task<ResourceProcedureResult> ApplyReplicaDeploymentIfRequiredAsync(
        Resource resource,
        ResourceProcedureResult result,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        if (!result.RuntimeReconciliationRequired)
        {
            return result;
        }

        var provider = GetDeploymentProvider(resource);
        if (provider is null ||
            !provider.CanDescribeDeployment(resource))
        {
            return result;
        }

        const string cause = "Replica scaling requested runtime reconciliation.";
        var deployment = await provider.DescribeDeploymentAsync(
            CreateProcedureContext(resource, triggeredBy, cause),
            cancellationToken);
        if (deployment is null)
        {
            return result;
        }

        ResourceOrchestratorDeploymentApplyResult applyResult;
        try
        {
            applyResult = await deployments.ApplyDeploymentAsync(
                resource,
                deployment,
                cancellationToken,
                triggeredBy,
                cause);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var failureProvider = GetDeploymentFailureProvider(resource);
            if (failureProvider is not null &&
                failureProvider.CanHandleDeploymentApplyFailed(resource))
            {
                await failureProvider.HandleDeploymentApplyFailedAsync(
                    CreateProcedureContext(resource, triggeredBy, cause),
                    deployment,
                    exception,
                    cancellationToken);
            }

            throw;
        }

        var appliedProvider = GetDeploymentAppliedProvider(resource);
        if (appliedProvider is not null &&
            appliedProvider.CanHandleDeploymentApplied(resource))
        {
            await appliedProvider.HandleDeploymentAppliedAsync(
                CreateProcedureContext(resource, triggeredBy, cause),
                applyResult,
                cancellationToken);
        }

        var mergedResult = MergeAppliedDeploymentResult(result, applyResult.ProcedureResult);
        return await RunPostApplyDeploymentTearDownAsync(
            resource,
            applyResult,
            mergedResult,
            triggeredBy,
            cause,
            cancellationToken);
    }

    private async Task<ResourceProcedureResult> RunPostApplyDeploymentTearDownAsync(
        Resource resource,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        ResourceProcedureResult result,
        string? triggeredBy,
        string cause,
        CancellationToken cancellationToken) =>
        await deploymentCleanup.RunPostApplyCleanupAsync(
            resource,
            applyResult,
            result,
            triggeredBy,
            async (appliedDeployment, token) =>
            {
                var tearDownProvider = GetDeploymentTearDownProvider(resource);
                if (tearDownProvider is null ||
                    !tearDownProvider.CanDescribeDeploymentTearDown(resource))
                {
                    return [];
                }

                return await tearDownProvider.DescribeDeploymentTearDownAsync(
                    CreateProcedureContext(resource, triggeredBy, cause),
                    appliedDeployment,
                    token);
            },
            async (tearDown, replicaGroup, reason, token) =>
                await orchestration.TearDownReplicaGroupAsync(
                    resource,
                    tearDown.Service,
                    replicaGroup,
                    token,
                    triggeredBy,
                    reason),
            MergeAppliedDeploymentResult,
            cancellationToken);

    public async Task<ResourceTemplateExportResult> ExportResourceTemplateAsync(
        ResourceTemplateExportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await templates.ExportTemplateAsync(
            request.Name,
            resourceIds: request.RequestedResourceIds,
            environmentId: request.EnvironmentId,
            metadata: request.Metadata,
            cancellationToken: cancellationToken);

        return new ResourceTemplateExportResult(result.Template, result.Diagnostics);
    }

    public async Task<ResourceTemplateApplyResult> ApplyResourceTemplateAsync(
        ResourceDefinitionTemplate template,
        CancellationToken cancellationToken = default) =>
        await ApplyResourceTemplateAsync(
            new ResourceTemplateApplyRequest(template),
            cancellationToken);

    public async Task<ResourceTemplateApplyResult> ApplyResourceTemplateAsync(
        ResourceTemplateApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Template);
        var localPathDiagnostics = ValidateLocalPathResourceDefinitions(request.Template);
        if (localPathDiagnostics.Count > 0)
        {
            return new ResourceTemplateApplyResult(
                request.Template,
                IsCommitted: false,
                localPathDiagnostics);
        }

        var apply = await templates.ApplyTemplateAsync(
            request.Template,
            new ResourceGraphCommitContext(
                EnvironmentId: request.Template.EnvironmentId,
                PrincipalId: ResolveTriggeredBy("resource-template-apply"),
                Timestamp: DateTimeOffset.UtcNow),
            new ResourceModelGraphDefinitionApplyOptions(request.Mode),
            cancellationToken);

        var diagnostics = apply.Diagnostics;
        if (!apply.HasErrors && apply.IsCommitted)
        {
            await RegisterAppliedDefinitionsAsync(
                request.Template.Resources,
                GetTemplateResourceGroupId(request.Template),
                cancellationToken);
            templates.ApplyTemplateDeclarationMetadata(request.Template);
            diagnostics = diagnostics
                .Concat(await ProvisionAppliedTemplateIdentitiesAsync(
                    request.Template.Resources,
                    cancellationToken))
                .ToArray();
        }

        return new ResourceTemplateApplyResult(
            apply.Template,
            apply.IsCommitted,
            diagnostics);
    }

    private IReadOnlyList<ResourceDefinitionDiagnostic> ValidateLocalPathResourceDefinitions(
        ResourceDefinitionTemplate template)
    {
        if (resourceManagerOptions.AllowLocalPathResourceDefinitions)
        {
            return [];
        }

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        foreach (var definition in template.Resources)
        {
            foreach (var (attributeId, value) in definition.ResourceAttributeValues)
            {
                if (!IsLocalPathAttribute(definition.TypeId.Value, attributeId.Value) ||
                    !HasMeaningfulValue(value))
                {
                    continue;
                }

                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.LocalPathResourceDefinitionNotAllowed,
                    $"Resource definition '{definition.EffectiveResourceId}' uses host-local path attribute '{attributeId}', but this Control Plane host does not allow local path resource definitions. Use application artifacts or enable ResourceManager:AllowLocalPathResourceDefinitions for a trusted local development host.",
                    attributeId));
            }
        }

        return diagnostics;
    }

    private static bool IsLocalPathAttribute(string resourceTypeId, string attributeId) =>
        string.Equals(attributeId, ProjectPathAttribute, StringComparison.OrdinalIgnoreCase) &&
            IsProjectPathResourceType(resourceTypeId) ||
        string.Equals(resourceTypeId, ApplicationDotnetAppType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(attributeId, DotnetExecutablePathAttribute, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationExecutableType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(attributeId, ExecutablePathAttribute, StringComparison.OrdinalIgnoreCase);

    private static bool IsProjectPathResourceType(string resourceTypeId) =>
        string.Equals(resourceTypeId, ApplicationDotnetAppType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationPythonAppType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationJavaAppType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationJavaScriptAppType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationGoAppType, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(resourceTypeId, ApplicationContainerAppType, StringComparison.OrdinalIgnoreCase);

    private static bool HasMeaningfulValue(ResourceAttributeValue value) =>
        !value.TryGetScalarString(out var scalarValue) ||
        !string.IsNullOrWhiteSpace(scalarValue);

    public Task<DeploymentArtifactStoreStatus> GetDeploymentArtifactStoreStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        cancellationToken.ThrowIfCancellationRequested();
        if (!deploymentArtifactOptions.Enabled)
        {
            return Task.FromResult(new DeploymentArtifactStoreStatus(
                false,
                DeploymentArtifactStoreKinds.Disabled,
                0,
                []));
        }

        return Task.FromResult(RequireDeploymentArtifactStore().GetStatus());
    }

    public async Task<IReadOnlyList<DeploymentArtifactLayoutDescriptor>> ListDeploymentArtifactLayoutsAsync(
        string resourceId,
        DeploymentArtifactLayoutQuery query,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureDeploymentArtifactsEnabled();

        var layouts = new List<DeploymentArtifactLayoutDescriptor>();
        foreach (var provider in deploymentArtifactLayoutProviders.Where(provider =>
            provider.TypeId == query.ResourceTypeId))
        {
            layouts.AddRange(await provider.GetDeploymentArtifactLayoutsAsync(query, cancellationToken));
        }

        return layouts
            .OrderByDescending(layout => layout.IsDefault)
            .ThenBy(layout => layout.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Task<DeploymentArtifactUploadSession> CreateDeploymentArtifactUploadSessionAsync(
        CreateDeploymentArtifactUploadSessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCanUploadDeploymentArtifacts();
        return RequireDeploymentArtifactStore().CreateUploadSessionAsync(
            command with { ResourceId = RequireValue(command.ResourceId, nameof(command.ResourceId)) },
            cancellationToken);
    }

    public Task UploadDeploymentArtifactContentAsync(
        string resourceId,
        string uploadId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        EnsureCanUploadDeploymentArtifacts();
        return RequireDeploymentArtifactStore().WriteUploadContentAsync(resourceId, uploadId, content, cancellationToken);
    }

    public Task<DeploymentArtifactRevision> CompleteDeploymentArtifactUploadAsync(
        string resourceId,
        CompleteDeploymentArtifactUploadCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        EnsureCanUploadDeploymentArtifacts();
        return RequireDeploymentArtifactStore().CompleteUploadAsync(resourceId, command, cancellationToken);
    }

    public Task<DeploymentArtifactRevision?> GetDeploymentArtifactRevisionAsync(
        string resourceId,
        string artifactId,
        string revisionId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        EnsureCanReadDeploymentArtifacts();
        return RequireDeploymentArtifactStore().GetRevisionAsync(resourceId, artifactId, revisionId, cancellationToken);
    }

    public Task<IReadOnlyList<DeploymentArtifactRevision>> ListDeploymentArtifactRevisionsAsync(
        string resourceId,
        string artifactId,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        RequireValue(artifactId, nameof(artifactId));
        EnsureCanReadDeploymentArtifacts();
        return RequireDeploymentArtifactStore().ListRevisionsAsync(resourceId, artifactId, cancellationToken);
    }

    public async Task<ResourceDefinitionValidationResult> ValidateDeploymentArtifactAsync(
        string resourceId,
        ValidateDeploymentArtifactCommand command,
        CancellationToken cancellationToken = default)
    {
        RequireValue(resourceId, nameof(resourceId));
        ArgumentNullException.ThrowIfNull(command);
        EnsureCanReadDeploymentArtifacts();

        var store = RequireDeploymentArtifactStore();
        var artifactId = RequireValue(command.ArtifactId, nameof(command.ArtifactId));
        var revisionId = RequireValue(command.RevisionId, nameof(command.RevisionId));
        var revision = await store.GetRevisionAsync(resourceId, artifactId, revisionId, cancellationToken);
        if (revision is null)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "deploymentArtifact.revisionMissing",
                    $"Deployment artifact revision '{artifactId}/revisions/{revisionId}' was not found.",
                    command.ArtifactId)
            ]);
        }

        var context = new DeploymentArtifactValidationContext(
            RequireValue(command.ResourceType, nameof(command.ResourceType)),
            RequireValue(command.ResourceName, nameof(command.ResourceName)),
            revision.ArtifactId,
            revision.RevisionId,
            revision.PackageKind,
            revision.ContentSha256,
            revision.SizeBytes,
            NormalizeOptional(command.EntryPath),
            NormalizeOptional(command.ArtifactLayoutKind) ?? revision.ArtifactLayoutKind);

        var provider = deploymentArtifactValidationProviders.FirstOrDefault(provider =>
            provider.CanValidate(context));
        if (provider is null)
        {
            return ResourceDefinitionValidationResult.FromDiagnostics(
            [
                ResourceDefinitionDiagnostic.Error(
                    "deploymentArtifact.validationProviderMissing",
                    $"No deployment artifact validation provider is registered for resource type '{context.ResourceType}' and artifact layout '{context.ArtifactLayoutKind ?? "default"}'.",
                    context.ResourceType)
            ]);
        }

        await using var content = await store.OpenRevisionContentAsync(
            resourceId,
            revision.ArtifactId,
            revision.RevisionId,
            cancellationToken);
        return await provider.ValidateDeploymentArtifactAsync(
            context,
            content,
            cancellationToken);
    }

    private async Task RegisterAppliedDefinitionsAsync(
        IReadOnlyList<ResourceDefinition> definitions,
        string? resourceGroupId,
        CancellationToken cancellationToken)
    {
        var existingRegistrations = registrations.GetRegistrations()
            .Select(registration => registration.ResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in definitions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dependencyIds = definition.StartupDependencyIds;
            var identity = CloudShell.ResourceModel.ResourceDeclarationAttributes.GetIdentityAttribute(definition)
                is { } identityAttribute
                ? ResourceModelGraphDefinitionApplyService.CreateIdentityBinding(identityAttribute)
                : null;
            if (existingRegistrations.Contains(definition.EffectiveResourceId))
            {
                await AssignResourceGroupAsync(
                    new AssignResourceGroupCommand(
                        definition.EffectiveResourceId,
                        resourceGroupId,
                        dependencyIds),
                    cancellationToken);
            }
            else
            {
                await RegisterResourceAsync(
                    new RegisterResourceCommand(
                        ResourceModelResourceProvider.DefaultProviderId,
                        definition.EffectiveResourceId,
                        resourceGroupId,
                        dependencyIds),
                    cancellationToken);
                existingRegistrations.Add(definition.EffectiveResourceId);
            }

            if (identity is not null)
            {
                await registrations.SetIdentityAsync(
                    definition.EffectiveResourceId,
                    identity,
                    cancellationToken);
            }
        }
    }

    private static string? GetTemplateResourceGroupId(ResourceDefinitionTemplate template)
    {
        if (template.Metadata is null)
        {
            return null;
        }

        return template.Metadata.TryGetValue("resourceGroup.id", out var resourceGroupId)
            ? NormalizeOptional(resourceGroupId)
            : null;
    }

    private async Task<IReadOnlyList<ResourceDefinitionDiagnostic>> ProvisionAppliedTemplateIdentitiesAsync(
        IReadOnlyList<ResourceDefinition> definitions,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        foreach (var definition in definitions.Where(ShouldProvisionIdentityOnApply))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CloudShell.ResourceModel.ResourceDeclarationAttributes.GetIdentityAttribute(definition) is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
                    "identity.provisioning.identityMissing",
                    $"Resource '{ResourceDisplayLabels.GetName(definition.EffectiveResourceId)}' requested identity provisioning but does not declare an identity.",
                    definition.EffectiveResourceId));
                continue;
            }

            try
            {
                var result = await resourceIdentityProvisioning.ProvisionResourceAsync(
                    definition.EffectiveResourceId,
                    cancellationToken);
                diagnostics.AddRange(result.ProvisioningDiagnostics.Select(ToResourceDefinitionDiagnostic));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "identity.provisioning.failed",
                    exception is ControlPlaneException controlPlaneException
                        ? controlPlaneException.Error.Message
                        : exception.Message,
                    definition.EffectiveResourceId));
            }
        }

        return diagnostics;
    }

    private static bool ShouldProvisionIdentityOnApply(ResourceDefinition definition) =>
        CloudShell.ResourceModel.ResourceDeclarationAttributes
            .GetProvisionIdentityOnStartupAttribute(definition) == true;

    private static ResourceDefinitionDiagnostic ToResourceDefinitionDiagnostic(
        ResourceIdentityProvisioningDiagnostic diagnostic) =>
        new(
            diagnostic.Severity switch
            {
                ResourceIdentityProvisioningDiagnosticSeverity.Error =>
                    ResourceDefinitionDiagnosticSeverity.Error,
                ResourceIdentityProvisioningDiagnosticSeverity.Warning =>
                    ResourceDefinitionDiagnosticSeverity.Warning,
                _ => ResourceDefinitionDiagnosticSeverity.Information
            },
            "identity.provisioning",
            diagnostic.Message,
            diagnostic.Identity?.ResourceId ?? diagnostic.ProviderId);

    public Task<IReadOnlyList<LogSource>> ListLogSourcesAsync(
        LogQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadLogs();
        return Task.FromResult(ApplyQuery(GetReadableLogSources(), query, GetScopedResourceIds(query?.ResourceId)));
    }

    public Task<LogSource?> GetLogSourceAsync(
        string logSourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadLogs();
        return Task.FromResult(GetReadableLogSource(logSourceId));
    }

    public Task<IReadOnlyList<ResourceEvent>> ListResourceEventsAsync(
        ResourceEventQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (resourceEvents is null)
        {
            return Task.FromResult<IReadOnlyList<ResourceEvent>>([]);
        }

        var visibleResourceIds = resourceManager.GetResources()
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (query?.ResourceId is not null &&
            !visibleResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<ResourceEvent>>([]);
        }

        var events = resourceEvents
            .GetEvents(query)
            .Where(resourceEvent => visibleResourceIds.Contains(resourceEvent.ResourceId))
            .ToArray();

        return Task.FromResult<IReadOnlyList<ResourceEvent>>(events);
    }

    public Task<IReadOnlyList<CloudShellNotificationInstance>> ListNotificationsAsync(
        CloudShellNotificationQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(notifications?.GetNotifications(query) ?? []);
    }

    public event EventHandler<CloudShellNotificationsChangedEventArgs>? NotificationsChanged
    {
        add
        {
            if (notifications is not null)
            {
                notifications.NotificationsChanged += value;
            }
        }
        remove
        {
            if (notifications is not null)
            {
                notifications.NotificationsChanged -= value;
            }
        }
    }

    public Task<CloudShellNotificationInstance> CreateNotificationAsync(
        CreateCloudShellNotificationCommand command,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(command);

        return Task.FromResult(notifications?.CreateNotification(command)
            ?? throw new InvalidOperationException("Notifications are not configured."));
    }

    public Task AcknowledgeNotificationAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        notifications?.AcknowledgeNotification(notificationId);
        return Task.CompletedTask;
    }

    public async Task HandleNotificationActionAsync(
        string notificationId,
        string actionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);

        var notification = notifications?.GetNotification(notificationId)
            ?? throw new ArgumentException("Notification was not found.", nameof(notificationId));
        if (notification.Actions?.Any(action => string.Equals(action.Id, actionId, StringComparison.OrdinalIgnoreCase)) != true)
        {
            throw new ArgumentException("Notification action was not found.", nameof(actionId));
        }

        foreach (var handler in notificationActionHandlers)
        {
            await handler.HandleNotificationActionAsync(notification, actionId.Trim(), cancellationToken);
        }
    }

    public Task DismissNotificationAsync(
        string notificationId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(notificationId);
        notifications?.DismissNotification(notificationId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ResourceDeploymentRecord>> ListResourceDeploymentsAsync(
        ResourceDeploymentQuery? query = null,
        CancellationToken cancellationToken = default) =>
        deployments.ListResourceDeploymentsAsync(query, cancellationToken);

    public async Task<IReadOnlyList<ResourceReplicaSlotState>> ListReplicaSlotStatesAsync(
        ResourceReplicaSlotStateQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var visibleResourceIds = resourceManager.GetResources()
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (query?.ResourceId is not null &&
            !visibleResourceIds.Contains(query.ResourceId))
        {
            return [];
        }

        var states = await replicaGroupReconciliation.ListReplicaSlotStatesAsync(query, cancellationToken);
        return states
            .Where(state => visibleResourceIds.Contains(state.ResourceId))
            .ToArray();
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        ReadLogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCanReadLogs();
        if (GetReadableLogSource(logSourceId) is null)
        {
            return Task.FromResult<IReadOnlyList<LogEntry>>([]);
        }

        return logs.ReadLogSourceAsync(
            logSourceId,
            options?.MaxEntries ?? 200,
            options?.Before,
            cancellationToken);
    }

    public IAsyncEnumerable<LogEntry> StreamLogSourceAsync(
        string logSourceId,
        StreamLogOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        EnsureCanReadLogs();
        if (GetReadableLogSource(logSourceId) is null)
        {
            return AsyncEnumerable.Empty<LogEntry>();
        }

        return logs.StreamLogSourceAsync(logSourceId, options?.InitialEntries ?? 50, cancellationToken);
    }

    public Task<IReadOnlyList<TraceSpan>> ListTraceSpansAsync(
        TraceQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadTraces();
        var readableResourceIds = GetReadableResourceIds();
        if (query?.ResourceId is not null &&
            !readableResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<TraceSpan>>([]);
        }

        return Task.FromResult<IReadOnlyList<TraceSpan>>(traces.GetSpans(
            query?.ResourceId,
            query?.TraceId,
            query?.MaxSpans ?? 200,
            query?.Scope)
            .Where(span => readableResourceIds.Contains(span.ResourceId))
            .ToArray());
    }

    public Task IngestTraceSpansAsync(
        IEnumerable<TraceSpan> spans,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        traces.AddSpans(spans);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<MetricPoint>> ListMetricPointsAsync(
        MetricQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadMetrics();
        var readableResourceIds = GetReadableResourceIds();
        if (query?.ResourceId is not null &&
            !readableResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<MetricPoint>>([]);
        }

        return Task.FromResult<IReadOnlyList<MetricPoint>>(metrics.GetPoints(
            query?.ResourceId,
            query?.MetricName,
            query?.MaxPoints ?? 200,
            query?.Scope)
            .Where(point => readableResourceIds.Contains(point.ResourceId))
            .ToArray());
    }

    public Task IngestMetricPointsAsync(
        IEnumerable<MetricPoint> points,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        metrics.AddPoints(points);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UsageSample>> ListUsageSamplesAsync(
        UsageQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadUsage();
        var readableResourceIds = GetReadableResourceIds();
        if (query?.ResourceId is not null &&
            !readableResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<UsageSample>>([]);
        }

        return Task.FromResult<IReadOnlyList<UsageSample>>(usage.GetSamples(
            query?.ResourceId,
            query?.UsageName,
            query?.MaxSamples ?? 200,
            query?.From,
            query?.To)
            .Where(sample => readableResourceIds.Contains(sample.ResourceId))
            .ToArray());
    }

    public Task<IReadOnlyList<UsageStatistic>> ListUsageStatisticsAsync(
        UsageStatisticsQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureCanReadUsage();
        var readableResourceIds = GetReadableResourceIds();
        if (query?.ResourceId is not null &&
            !readableResourceIds.Contains(query.ResourceId))
        {
            return Task.FromResult<IReadOnlyList<UsageStatistic>>([]);
        }

        return Task.FromResult<IReadOnlyList<UsageStatistic>>(usage.GetStatistics(
            query?.ResourceId,
            query?.UsageName,
            query?.From,
            query?.To,
            query?.MaxStatistics ?? 200)
            .Where(statistic => readableResourceIds.Contains(statistic.ResourceId))
            .ToArray());
    }

    public Task RecordUsageSamplesAsync(
        IEnumerable<UsageSample> samples,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        usage.AddSamples(samples);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyDictionary<string, ResourceHealthSummary>> ListResourceHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allResources = resourceManager.GetResources();
        var resources = GetProbeHealthResources(allResources);

        await RefreshStaleResourceHealthAsync(resources, cancellationToken);

        AddRuntimeHealthAggregateSummaries(allResources);
        return CreateHealthSummaryDictionary(GetHealthSummaryResources(allResources));
    }

    public async Task<ResourceHealthSummary?> GetResourceHealthAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return null;
        }

        if (IsHealthProbeResource(resource))
        {
            await RefreshStaleResourceHealthAsync([resource], cancellationToken);
            return resourceHealth.GetLatest(resource.Id) ?? CreateUnknownHealthSummary(resource);
        }

        var allResources = resourceManager.GetResources();
        var children = GetRuntimeHealthChildren(resource, allResources);
        if (children.Count == 0)
        {
            return null;
        }

        await RefreshStaleResourceHealthAsync(children, cancellationToken);
        AddRuntimeHealthAggregateSummary(resource, allResources);
        return resourceHealth.GetLatest(resource.Id);
    }

    public Task<IReadOnlyList<ResourceHealthSummary>> ListResourceHealthSnapshotsAsync(
        string resourceId,
        int maxSnapshots = 100,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(resourceHealth.GetSnapshots(
            resourceId,
            Math.Clamp(maxSnapshots, 1, 10_000)));
    }

    public async Task<IReadOnlyDictionary<string, ResourceHealthSummary>> RefreshResourceHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allResources = resourceManager.GetResources();
        var resources = GetProbeHealthResources(allResources);
        await RefreshResourceHealthCoreAsync(resources, cancellationToken);
        AddRuntimeHealthAggregateSummaries(allResources);
        return CreateHealthSummaryDictionary(GetHealthSummaryResources(allResources));
    }

    public async Task<ResourceHealthSummary?> RefreshResourceHealthAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return null;
        }

        if (IsHealthProbeResource(resource))
        {
            await RefreshResourceHealthCoreAsync([resource], cancellationToken);
            return resourceHealth.GetLatest(resource.Id);
        }

        var allResources = resourceManager.GetResources();
        var children = GetRuntimeHealthChildren(resource, allResources);
        if (children.Count == 0)
        {
            return null;
        }

        await RefreshResourceHealthCoreAsync(children, cancellationToken);
        AddRuntimeHealthAggregateSummary(resource, allResources);
        return resourceHealth.GetLatest(resource.Id);
    }

    public Task<ResourceRecoveryPolicy?> GetResourceRecoveryPolicyAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = GetRecoveryResourceForRead(resourceId);
        if (resource is null)
        {
            return Task.FromResult<ResourceRecoveryPolicy?>(null);
        }

        return Task.FromResult(GetEffectiveRecoveryPolicy(resource));
    }

    public Task<ResourceRecoveryPolicy> SetResourceRecoveryPolicyAsync(
        string resourceId,
        ResourceRecoveryPolicy policy,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(policy);

        var resource = GetRecoveryResourceForManage(resourceId);
        var normalized = NormalizeRecoveryPolicy(policy);
        resourceRecovery.SetPolicy(resource.Id, normalized);
        return Task.FromResult(normalized);
    }

    public Task ClearResourceRecoveryPolicyAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = GetRecoveryResourceForManage(resourceId);
        resourceRecovery.ClearPolicy(resource.Id);
        return Task.CompletedTask;
    }

    public Task<ResourceRecoveryStatus?> GetResourceRecoveryStatusAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = GetRecoveryResourceForRead(resourceId);
        if (resource is null)
        {
            return Task.FromResult<ResourceRecoveryStatus?>(null);
        }

        var policy = GetEffectiveRecoveryPolicy(resource) ?? ResourceRecoveryPolicy.Disabled;
        var runtime = resourceRecovery.GetRuntimeState(resource.Id);
        var state = policy.Enabled ? runtime.State : ResourceRecoveryState.Disabled;
        if (policy.Enabled && state == ResourceRecoveryState.Disabled)
        {
            state = ResourceRecoveryState.WaitingForSignal;
        }

        return Task.FromResult<ResourceRecoveryStatus?>(new ResourceRecoveryStatus(
            resource.Id,
            policy,
            state,
            runtime.ConsecutiveFailures,
            runtime.AttemptCount,
            runtime.LastCheckedAt,
            runtime.LastAttemptAt,
            runtime.NextAttemptAt,
            runtime.LastDetail));
    }

    public async Task<ResourceRecoveryStatus?> RefreshResourceRecoveryAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = GetRecoveryResourceForRead(resourceId);
        if (resource is null)
        {
            return null;
        }

        var policy = GetEffectiveRecoveryPolicy(resource) ?? ResourceRecoveryPolicy.Disabled;
        if (!policy.Enabled)
        {
            resourceRecovery.ClearRuntimeState(resource.Id);
            return new ResourceRecoveryStatus(resource.Id, policy, ResourceRecoveryState.Disabled);
        }

        var now = DateTimeOffset.UtcNow;
        var runtime = resourceRecovery.GetRuntimeState(resource.Id);
        if (!IsLivenessActive(resource))
        {
            if (CanRecoverStoppedResource(resource, runtime))
            {
                return await HandleFailedRecoverySignalAsync(
                    resource,
                    policy,
                    runtime,
                    CreateStoppedRecoverySignal(resource, policy),
                    now,
                    ResourceActionIds.Start,
                    cancellationToken);
            }

            return SetRecoveryRuntimeState(
                resource,
                policy,
                new ResourceRecoveryRuntimeState(
                    ResourceRecoveryState.WaitingForSignal,
                    LastCheckedAt: now,
                    LastDetail: $"Recovery is waiting for resource state to become {ResourceState.Running}."));
        }

        var summary = await RefreshResourceHealthAsync(resource.Id, cancellationToken);
        if (summary is null)
        {
            return SetRecoveryRuntimeState(
                resource,
                policy,
                resourceRecovery.GetRuntimeState(resource.Id) with
                {
                    State = ResourceRecoveryState.Unavailable,
                    LastCheckedAt = DateTimeOffset.UtcNow,
                    LastDetail = "No health checks are configured."
                });
        }

        var result = SelectRecoverySignal(summary, policy);
        if (result is null)
        {
            return SetRecoveryRuntimeState(
                resource,
                policy,
                runtime with
                {
                    State = ResourceRecoveryState.Unavailable,
                    LastCheckedAt = now,
                    LastDetail = "No matching liveness signal was found."
                });
        }

        if (result.Status == ResourceHealthStatus.Healthy)
        {
            return HandleHealthyRecoverySignal(resource, policy, runtime, result, now);
        }

        if (result.Status == ResourceHealthStatus.Unknown)
        {
            return SetRecoveryRuntimeState(
                resource,
                policy,
                runtime with
                {
                    State = ResourceRecoveryState.Unavailable,
                    LastCheckedAt = now,
                    LastDetail = result.Detail
                });
        }

        return await HandleFailedRecoverySignalAsync(
            resource,
            policy,
            runtime,
            result,
            now,
            ResourceActionIds.Restart,
            cancellationToken);
    }

    private async Task<ResourceRecoveryStatus> HandleFailedRecoverySignalAsync(
        Resource resource,
        ResourceRecoveryPolicy policy,
        ResourceRecoveryRuntimeState runtime,
        ResourceHealthCheckResult result,
        DateTimeOffset now,
        string actionId,
        CancellationToken cancellationToken)
    {
        var failures = runtime.ConsecutiveFailures + 1;
        var failing = runtime with
        {
            State = ResourceRecoveryState.Failing,
            ConsecutiveFailures = failures,
            LastCheckedAt = now,
            LastHealthyAt = null,
            LastDetail = result.Detail
        };

        if (failures < policy.FailureThreshold)
        {
            return SetRecoveryRuntimeState(resource, policy, failing);
        }

        var actionLabel = string.Equals(actionId, ResourceActionIds.Start, StringComparison.OrdinalIgnoreCase)
            ? "start"
            : "restart";
        AppendRecoveryEvent(
            resource,
            ResourceEventTypes.Events.Recovery.SignalFailed,
            $"Recovery signal '{result.Check.Name}' failed {failures} consecutive time(s): {result.Detail}",
            ResourceSignalSeverity.Warning);

        if (failing.AttemptCount >= policy.MaxAttempts)
        {
            var exhausted = failing with
            {
                State = ResourceRecoveryState.Exhausted,
                LastDetail = $"Maximum recovery attempts reached after {failing.AttemptCount} attempt(s)."
            };
            AppendRecoveryEvent(
                resource,
                ResourceEventTypes.Events.Recovery.RestartExhausted,
                exhausted.LastDetail,
                ResourceSignalSeverity.Error);
            return SetRecoveryRuntimeState(resource, policy, exhausted);
        }

        if (failing.NextAttemptAt is not null && failing.NextAttemptAt > now)
        {
            return SetRecoveryRuntimeState(resource, policy, failing with
            {
                State = ResourceRecoveryState.Scheduled
            });
        }

        var attempt = failing.AttemptCount + 1;
        var backoff = CalculateRecoveryBackoff(policy, attempt);
        var nextAttemptAt = now + backoff;
        var restartCause = FormatLivenessCause(result);
        AppendRecoveryEvent(
            resource,
            ResourceEventTypes.Events.Recovery.RestartScheduled,
            $"Recovery {actionLabel} attempt {attempt} scheduled after {failures} failed signal(s).",
            ResourceSignalSeverity.Warning);
        AppendRecoveryEvent(
            resource,
            ResourceEventTypes.Events.Recovery.RestartAttempted,
            $"Recovery {actionLabel} attempt {attempt} started. {restartCause}",
            ResourceSignalSeverity.Warning);

        try
        {
            var procedure = await ExecuteResourceActionAsync(
                new ExecuteResourceActionCommand(
                    resource.Id,
                    actionId,
                    IgnoreDependentWarning: true,
                    TriggeredBy: RecoveryTriggeredBy,
                    Cause: restartCause),
                cancellationToken);

            AppendRecoveryEvent(
                resource,
                ResourceEventTypes.Events.Recovery.RestartSucceeded,
                $"Recovery {actionLabel} attempt {attempt} completed. Result: {procedure.Message}",
                ResourceSignalSeverity.Success);

            return SetRecoveryRuntimeState(resource, policy, failing with
            {
                State = ResourceRecoveryState.Restarting,
                AttemptCount = attempt,
                LastAttemptAt = now,
                NextAttemptAt = nextAttemptAt,
                LastDetail = procedure.Message
            });
        }
        catch (Exception exception) when (exception is ControlPlaneException or ControlPlaneAccessDeniedException)
        {
            AppendRecoveryEvent(
                resource,
                ResourceEventTypes.Events.Recovery.RestartSkipped,
                $"Recovery {actionLabel} was skipped: {exception.Message}",
                ResourceSignalSeverity.Warning);

            return SetRecoveryRuntimeState(resource, policy, failing with
            {
                State = ResourceRecoveryState.Unavailable,
                AttemptCount = attempt,
                LastAttemptAt = now,
                NextAttemptAt = nextAttemptAt,
                LastDetail = exception.Message
            });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            AppendRecoveryEvent(
                resource,
                ResourceEventTypes.Events.Recovery.RestartFailed,
                $"Recovery {actionLabel} attempt {attempt} failed: {exception.Message}",
                ResourceSignalSeverity.Error);

            return SetRecoveryRuntimeState(resource, policy, failing with
            {
                State = ResourceRecoveryState.Unavailable,
                AttemptCount = attempt,
                LastAttemptAt = now,
                NextAttemptAt = nextAttemptAt,
                LastDetail = exception.Message
            });
        }
    }

    private static bool CanRecoverStoppedResource(
        Resource resource,
        ResourceRecoveryRuntimeState runtime) =>
        resource.State == ResourceState.Stopped &&
        resource.HasAction(ResourceActionIds.Start) &&
        runtime.LastHealthyAt is not null;

    private static ResourceHealthCheckResult CreateStoppedRecoverySignal(
        Resource resource,
        ResourceRecoveryPolicy policy)
    {
        var check = SelectRecoveryCheck(resource, policy) ??
            new ResourceHealthCheck(
                new ResourceProbeSource("resource-state"),
                ResourceProbeType.Liveness,
                "liveness");
        return new ResourceHealthCheckResult(
            check,
            ResourceHealthStatus.Unhealthy,
            "Resource stopped unexpectedly.",
            null,
            ResourceHealthCheckOutcome.NoResponse,
            DateTimeOffset.UtcNow);
    }

    public Task<bool> HasResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        return Task.FromResult(resource is not null &&
            monitoringProviders.Any(provider => provider.CanMonitor(resource)));
    }

    public async Task<ResourceMonitoringSnapshot?> GetResourceMonitoringAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return null;
        }

        foreach (var provider in monitoringProviders)
        {
            if (!provider.CanMonitor(resource))
            {
                continue;
            }

            var snapshot = await provider.GetMonitoringSnapshotAsync(resource, cancellationToken);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        return null;
    }

    private IReadOnlyList<Resource> GetActiveDependents(Resource resource) =>
        resourceManager.GetResources()
            .Where(candidate => candidate.State == ResourceState.Running)
            .Where(candidate => candidate.DependsOn.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();

    private void NotifyResourcesChanged(ResourceChangeNotification notification) =>
        ResourcesChanged?.Invoke(this, notification);

    private IReadOnlyDictionary<string, ResourceHealthSummary> CreateHealthSummaryDictionary(
        IReadOnlyList<Resource> resources)
    {
        var summaries = resourceHealth.GetLatest(resources.Select(resource => resource.Id));
        return resources.ToDictionary(
            resource => resource.Id,
            resource => summaries.GetValueOrDefault(resource.Id) ?? CreateUnknownHealthSummary(resource),
            StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<Resource> GetProbeHealthResources(IReadOnlyList<Resource> resources) =>
        resources
            .Where(IsHealthProbeResource)
            .ToArray();

    private static bool IsHealthProbeResource(Resource resource) =>
        resource.ResourceHealthChecks.Count > 0 &&
        resource.State is null or ResourceState.Running or ResourceState.Degraded;

    private IReadOnlyList<Resource> GetHealthSummaryResources(IReadOnlyList<Resource> resources) =>
        GetProbeHealthResources(resources)
            .Concat(GetRuntimeHealthAggregateParents(resources))
            .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

    private async Task RefreshStaleResourceHealthAsync(
        IReadOnlyList<Resource> resources,
        CancellationToken cancellationToken)
    {
        var staleResources = GetStaleHealthResources(resources);
        if (staleResources.Count == 0)
        {
            return;
        }

        using var refresh = await healthRefreshes.TryEnterAsync(cancellationToken);
        if (refresh is null)
        {
            return;
        }

        staleResources = GetStaleHealthResources(resources);
        if (staleResources.Count == 0)
        {
            return;
        }

        await RefreshResourceHealthCoreUnlockedAsync(staleResources, respectCheckIntervals: true, cancellationToken);
    }

    private async Task RefreshResourceHealthCoreAsync(
        IReadOnlyList<Resource> resources,
        CancellationToken cancellationToken)
    {
        using var refresh = await healthRefreshes.EnterAsync(cancellationToken);

        await RefreshResourceHealthCoreUnlockedAsync(resources, respectCheckIntervals: false, cancellationToken);
    }

    private async Task RefreshResourceHealthCoreUnlockedAsync(
        IReadOnlyList<Resource> resources,
        bool respectCheckIntervals,
        CancellationToken cancellationToken)
    {
        var allResources = resourceManager.GetResources();
        var aggregateParents = GetRuntimeHealthAggregateParents(allResources, resources);
        var lifecycleResources = resources
            .Concat(aggregateParents)
            .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var previousLivenessStates = lifecycleResources.ToDictionary(
            resource => resource.Id,
            resource => GetLivenessLifecycleProjection(resource)?.State,
            StringComparer.OrdinalIgnoreCase);
        var previousSummaries = resourceHealth.GetLatest(resources.Select(resource => resource.Id));
        var summaries = await healthProbes.CheckAsync(
            resources,
            respectCheckIntervals ? previousSummaries : null,
            respectCheckIntervals ? ShouldEvaluateHealthCheck : null,
            cancellationToken);
        resourceHealth.AddRange(summaries.Values);
        AddRuntimeHealthAggregateSummaries(allResources, aggregateParents);
        RecordLivenessLifecycleTransitions(lifecycleResources, previousLivenessStates);
        ObserveUnhealthyReplicaSlots(lifecycleResources);
    }

    private IReadOnlyList<Resource> GetRuntimeHealthAggregateParents(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<Resource>? changedChildren = null)
    {
        var sourceChildren = changedChildren ?? resources;
        var parentIds = sourceChildren
            .Where(IsRuntimeHealthChild)
            .Select(resource => resource.ParentResourceId!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (parentIds.Count == 0)
        {
            return [];
        }

        return resources
            .Where(resource => parentIds.Contains(resource.Id))
            .ToArray();
    }

    private static IReadOnlyList<Resource> GetRuntimeHealthChildren(
        Resource resource,
        IReadOnlyList<Resource> resources) =>
        resources
            .Where(child => string.Equals(child.ParentResourceId, resource.Id, StringComparison.OrdinalIgnoreCase))
            .Where(IsRuntimeHealthChild)
            .ToArray();

    private static bool IsRuntimeHealthChild(Resource resource) =>
        !string.IsNullOrWhiteSpace(resource.ParentResourceId) &&
        resource.IsRuntimeManaged &&
        resource.ResourceHealthChecks.Count > 0;

    private void AddRuntimeHealthAggregateSummaries(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<Resource>? parents = null)
    {
        foreach (var parent in parents ?? GetRuntimeHealthAggregateParents(resources))
        {
            AddRuntimeHealthAggregateSummary(parent, resources);
        }
    }

    private void AddRuntimeHealthAggregateSummary(
        Resource parent,
        IReadOnlyList<Resource> resources)
    {
        var children = GetRuntimeHealthChildren(parent, resources);
        if (children.Count == 0)
        {
            return;
        }

        var summary = CreateRuntimeHealthAggregateSummary(parent, children);
        if (summary is not null)
        {
            resourceHealth.Add(summary);
        }
    }

    private ResourceHealthSummary? CreateRuntimeHealthAggregateSummary(
        Resource parent,
        IReadOnlyList<Resource> children)
    {
        var childSummaries = children
            .Select(child => (Child: child, Summary: resourceHealth.GetLatest(child.Id) ?? CreateUnknownHealthSummary(child)))
            .ToArray();
        var results = childSummaries
            .SelectMany(item => item.Summary.Checks.Select(result => (item.Child, item.Summary, Result: result)))
            .GroupBy(
                item => (item.Result.Check.Type, item.Result.Check.Name),
                item => item)
            .Select(group => CreateRuntimeHealthAggregateResult(group.Key.Type, group.Key.Name, group))
            .ToArray();
        if (results.Length == 0)
        {
            return null;
        }

        var status = CombineHealthStatus(results.Select(result => result.Status));
        var checkedAt = childSummaries
            .Select(item => item.Summary.CheckedAt)
            .DefaultIfEmpty(DateTimeOffset.UtcNow)
            .Max();
        return new ResourceHealthSummary(parent.Id, status, checkedAt, results);
    }

    private static ResourceHealthCheckResult CreateRuntimeHealthAggregateResult(
        ResourceProbeType type,
        string name,
        IEnumerable<(Resource Child, ResourceHealthSummary Summary, ResourceHealthCheckResult Result)> items)
    {
        var results = items.ToArray();
        var status = CombineHealthStatus(results.Select(item => item.Result.Status));
        var healthy = results.Count(item => item.Result.Status == ResourceHealthStatus.Healthy);
        var total = results.Length;
        var detail = status == ResourceHealthStatus.Healthy
            ? $"All {total} runtime scope check(s) are healthy."
            : $"{healthy} of {total} runtime scope check(s) are healthy.";
        var check = results.First().Result.Check with { Name = name, Type = type };
        return new ResourceHealthCheckResult(
            check,
            status,
            detail,
            null,
            CombineHealthOutcome(results.Select(item => item.Result.Outcome)),
            results
                .Select(item => item.Result.CheckedAt ?? item.Summary.CheckedAt)
                .DefaultIfEmpty(DateTimeOffset.UtcNow)
                .Max(),
            results
                .Select(item => CreateRuntimeScopeObservation(item.Child, item.Result))
                .ToArray());
    }

    private static ResourceHealthScopeObservation CreateRuntimeScopeObservation(
        Resource child,
        ResourceHealthCheckResult result)
    {
        var attributes = new Dictionary<string, string>(child.ResourceAttributes, StringComparer.OrdinalIgnoreCase)
        {
            ["health.check.name"] = result.Check.Name,
            ["health.check.type"] = result.Check.Type.ToString()
        };
        return new ResourceHealthScopeObservation(
            child.Id,
            ResourceHealthScopeKinds.Runtime,
            result.Status,
            result.Detail,
            result.Outcome,
            child.EffectiveDisplayName,
            child.Id,
            result.CheckedAt,
            attributes);
    }

    private static ResourceHealthStatus CombineHealthStatus(IEnumerable<ResourceHealthStatus> statuses)
    {
        var values = statuses.ToArray();
        return values.Any(status => status == ResourceHealthStatus.Unhealthy)
            ? ResourceHealthStatus.Unhealthy
            : values.Any(status => status == ResourceHealthStatus.Unknown)
                ? ResourceHealthStatus.Unknown
                : ResourceHealthStatus.Healthy;
    }

    private static ResourceHealthCheckOutcome CombineHealthOutcome(IEnumerable<ResourceHealthCheckOutcome> outcomes)
    {
        var values = outcomes.ToArray();
        if (values.Any(outcome => outcome == ResourceHealthCheckOutcome.NoResponse))
        {
            return ResourceHealthCheckOutcome.NoResponse;
        }

        if (values.All(outcome => outcome == ResourceHealthCheckOutcome.Responded))
        {
            return ResourceHealthCheckOutcome.Responded;
        }

        return values.FirstOrDefault(outcome => outcome != ResourceHealthCheckOutcome.Responded);
    }

    private IReadOnlyList<Resource> GetStaleHealthResources(IReadOnlyList<Resource> resources)
    {
        var now = DateTimeOffset.UtcNow;
        return resources
            .Where(resource =>
            {
                var summary = resourceHealth.GetLatest(resource.Id);
                return summary is null ||
                    resource.ResourceHealthChecks.Any(check =>
                        ShouldEvaluateHealthCheck(
                            resource,
                            check,
                            FindHealthCheckResult(summary, check)?.CheckedAt ?? summary.CheckedAt,
                            now));
            })
            .ToArray();
    }

    private bool ShouldEvaluateHealthCheck(
        Resource resource,
        ResourceHealthCheck check,
        DateTimeOffset? lastCheckedAt) =>
        ShouldEvaluateHealthCheck(resource, check, lastCheckedAt, DateTimeOffset.UtcNow);

    private bool ShouldEvaluateHealthCheck(
        Resource resource,
        ResourceHealthCheck check,
        DateTimeOffset? lastCheckedAt,
        DateTimeOffset now)
    {
        if (lastCheckedAt is null)
        {
            return true;
        }

        return now - lastCheckedAt.Value >= GetHealthCheckInterval(check);
    }

    private TimeSpan GetHealthCheckInterval(ResourceHealthCheck check) =>
        TimeSpan.FromSeconds(
            check.IntervalSeconds ??
            orchestrationSettings.GetHealthCheckIntervalSettings().Seconds);

    private static ResourceHealthCheckResult? FindHealthCheckResult(
        ResourceHealthSummary summary,
        ResourceHealthCheck check) =>
        summary.Checks.FirstOrDefault(result =>
            ReferenceEquals(result.Check, check) ||
            result.Check == check ||
            (string.Equals(result.Check.Name, check.Name, StringComparison.OrdinalIgnoreCase) &&
             result.Check.Type == check.Type));

    private Resource? GetRecoveryResourceForRead(string resourceId)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId);
        if (resource is null)
        {
            return null;
        }

        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!CanAccessResource(resource.Id, group?.Id, CloudShellPermissions.Resources.Read))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                FormatPermissionRequirement(CloudShellPermissions.Resources.Read));
        }

        return resource;
    }

    private Resource GetRecoveryResourceForManage(string resourceId)
    {
        resourceId = RequireValue(resourceId, nameof(resourceId));
        var resource = resourceManager.GetResource(resourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        var group = resourceManager.GetGroupForResource(resource.Id);
        if (!CanAccessResource(resource.Id, group?.Id, CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                resource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        return resource;
    }

    private ResourceRecoveryStatus HandleHealthyRecoverySignal(
        Resource resource,
        ResourceRecoveryPolicy policy,
        ResourceRecoveryRuntimeState runtime,
        ResourceHealthCheckResult result,
        DateTimeOffset now)
    {
        var lastHealthyAt = runtime.LastHealthyAt ?? now;
        var resetAfter = TimeSpan.FromSeconds(policy.ResetAfterHealthySeconds);
        var shouldReset = runtime.AttemptCount == 0 ||
            policy.ResetAfterHealthySeconds == 0 ||
            now - lastHealthyAt >= resetAfter;

        var next = shouldReset
            ? new ResourceRecoveryRuntimeState(
                ResourceRecoveryState.Healthy,
                LastCheckedAt: now,
                LastHealthyAt: now,
                LastDetail: result.Detail)
            : runtime with
            {
                State = ResourceRecoveryState.Healthy,
                ConsecutiveFailures = 0,
                LastCheckedAt = now,
                LastHealthyAt = lastHealthyAt,
                LastDetail = result.Detail
            };

        if (shouldReset && (runtime.ConsecutiveFailures > 0 || runtime.AttemptCount > 0))
        {
            AppendRecoveryEvent(
                resource,
                ResourceEventTypes.Events.Recovery.Reset,
                $"Recovery state reset after signal '{result.Check.Name}' became healthy.",
                ResourceSignalSeverity.Info);
        }

        return SetRecoveryRuntimeState(resource, policy, next);
    }

    private static ResourceHealthCheckResult? SelectRecoverySignal(
        ResourceHealthSummary summary,
        ResourceRecoveryPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.ProbeName))
        {
            return summary.Checks.FirstOrDefault(result =>
                string.Equals(result.Check.Name, policy.ProbeName, StringComparison.OrdinalIgnoreCase));
        }

        return summary.Checks.FirstOrDefault(result => result.Check.Type == policy.ProbeType);
    }

    private static ResourceHealthCheck? SelectRecoveryCheck(
        Resource resource,
        ResourceRecoveryPolicy policy)
    {
        if (!string.IsNullOrWhiteSpace(policy.ProbeName))
        {
            return resource.ResourceHealthChecks.FirstOrDefault(check =>
                string.Equals(check.Name, policy.ProbeName, StringComparison.OrdinalIgnoreCase));
        }

        return resource.ResourceHealthChecks.FirstOrDefault(check => check.Type == policy.ProbeType);
    }

    private ResourceRecoveryStatus SetRecoveryRuntimeState(
        Resource resource,
        ResourceRecoveryPolicy policy,
        ResourceRecoveryRuntimeState state)
    {
        resourceRecovery.SetRuntimeState(resource.Id, state);
        return new ResourceRecoveryStatus(
            resource.Id,
            policy,
            state.State,
            state.ConsecutiveFailures,
            state.AttemptCount,
            state.LastCheckedAt,
            state.LastAttemptAt,
            state.NextAttemptAt,
            state.LastDetail);
    }

    private void AppendRecoveryEvent(
        Resource resource,
        string eventType,
        string message,
        ResourceSignalSeverity severity)
    {
        AppendResourceEvent(
            resource.Id,
            eventType,
            message,
            RecoveryTriggeredBy,
            severity);
    }

    private static TimeSpan CalculateRecoveryBackoff(ResourceRecoveryPolicy policy, int attempt)
    {
        var multiplier = Math.Pow(policy.BackoffMultiplier, Math.Max(0, attempt - 1));
        var seconds = policy.InitialBackoffSeconds * multiplier;
        return TimeSpan.FromSeconds(Math.Min(policy.MaxBackoffSeconds, seconds));
    }

    private static ResourceRecoveryPolicy NormalizeRecoveryPolicy(ResourceRecoveryPolicy policy) =>
        policy with
        {
            ProbeName = NormalizeOptional(policy.ProbeName),
            FailureThreshold = Math.Clamp(policy.FailureThreshold, 1, 100),
            StartupGracePeriodSeconds = Math.Clamp(policy.StartupGracePeriodSeconds, 0, 86_400),
            InitialBackoffSeconds = Math.Clamp(policy.InitialBackoffSeconds, 1, 86_400),
            MaxBackoffSeconds = Math.Clamp(
                Math.Max(policy.MaxBackoffSeconds, policy.InitialBackoffSeconds),
                1,
                86_400),
            BackoffMultiplier = Math.Clamp(policy.BackoffMultiplier, 1, 100),
            MaxAttempts = Math.Clamp(policy.MaxAttempts, 1, 10_000),
            ResetAfterHealthySeconds = Math.Clamp(policy.ResetAfterHealthySeconds, 0, 86_400)
        };

    private static ResourceHealthSummary CreateUnknownHealthSummary(Resource resource) =>
        new(
            resource.Id,
            ResourceHealthStatus.Unknown,
            DateTimeOffset.UtcNow,
            resource.ResourceHealthChecks
                .Select(check => new ResourceHealthCheckResult(
                    check,
                    ResourceHealthStatus.Unknown,
                    "No cached health result",
                    null,
                    ResourceHealthCheckOutcome.Unknown))
                .ToArray());

    private IReadOnlyList<Resource> ApplyLivenessStatus(IReadOnlyList<Resource> resources)
        => resources
            .Select(ApplyLivenessStatusToResource)
            .ToArray();

    private Resource? ApplyLivenessStatus(Resource? resource) =>
        resource is null
            ? null
            : ApplyLivenessStatusToResource(resource);

    private Resource ApplyLivenessStatusToResource(Resource resource)
    {
        var projection = GetLivenessLifecycleProjection(resource);
        return projection is null
            ? resource
            : resource with { State = projection.State };
    }

    private LivenessLifecycleProjection? GetLivenessLifecycleProjection(Resource resource)
    {
        if (!IsLivenessActive(resource))
        {
            return null;
        }

        var threshold = GetLivenessFailureThreshold(resource);
        var snapshots = resourceHealth.GetSnapshots(resource.Id, threshold);
        if (snapshots.Count < threshold ||
            !snapshots.Take(threshold).All(HasUnhealthyLiveness))
        {
            return null;
        }

        var failedLiveness = GetUnhealthyLiveness(snapshots[0]);
        if (failedLiveness is null)
        {
            return null;
        }

        var state = ProjectLivenessState(failedLiveness);

        return new LivenessLifecycleProjection(state, failedLiveness);
    }

    private static ResourceState ProjectLivenessState(ResourceHealthCheckResult failedLiveness)
    {
        if (failedLiveness.ScopeObservations.Any(observation =>
            string.Equals(observation.ScopeKind, ResourceHealthScopeKinds.Runtime, StringComparison.OrdinalIgnoreCase)))
        {
            return ResourceState.Degraded;
        }

        if (failedLiveness.ScopeObservations.Any(observation =>
            observation.Status == ResourceHealthStatus.Healthy))
        {
            return ResourceState.Degraded;
        }

        return failedLiveness.Outcome == ResourceHealthCheckOutcome.NoResponse
            ? ResourceState.Stopped
            : ResourceState.Degraded;
    }

    private static ResourceHealthCheckResult? GetUnhealthyLiveness(ResourceHealthSummary? summary) =>
        summary?.Checks.FirstOrDefault(check =>
            check.Check.Type == ResourceProbeType.Liveness &&
            check.Status == ResourceHealthStatus.Unhealthy);

    private static bool HasUnhealthyLiveness(ResourceHealthSummary? summary) =>
        GetUnhealthyLiveness(summary) is not null;

    private static bool IsLivenessActive(Resource resource) =>
        resource.State is ResourceState.Running or ResourceState.Degraded;

    private int GetLivenessFailureThreshold(Resource resource)
    {
        var policy = GetEffectiveRecoveryPolicy(resource) ?? new ResourceRecoveryPolicy();
        return NormalizeRecoveryPolicy(policy).FailureThreshold;
    }

    private ResourceRecoveryPolicy? GetEffectiveRecoveryPolicy(Resource resource)
    {
        var stored = resourceRecovery.GetPolicy(resource.Id);
        if (stored is not null)
        {
            return stored;
        }

        var declared = resource.ResourceRecoveryPolicies.FirstOrDefault();
        if (declared is null)
        {
            return null;
        }

        var normalized = NormalizeRecoveryPolicy(declared);
        resourceRecovery.SetPolicy(resource.Id, normalized);
        return normalized;
    }

    private void RecordLivenessLifecycleTransitions(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<string, ResourceState?> previousStates)
    {
        foreach (var resource in resources)
        {
            var projection = GetLivenessLifecycleProjection(resource);
            if (projection is null ||
                previousStates.GetValueOrDefault(resource.Id) == projection.State)
            {
                continue;
            }

            var eventType = projection.State == ResourceState.Stopped
                ? ResourceEventTypes.Events.Lifecycle.StoppedUnexpectedly
                : ResourceEventTypes.Events.Lifecycle.Degraded;
            var severity = projection.State == ResourceState.Stopped
                ? ResourceSignalSeverity.Error
                : ResourceSignalSeverity.Warning;
            var message = projection.State == ResourceState.Stopped
                ? $"Resource stopped unexpectedly. {FormatLivenessCause(projection.Result)}"
                : $"Resource degraded. {FormatLivenessCause(projection.Result)}";

            AppendResourceEvent(
                resource.Id,
                eventType,
                message,
                LivenessTriggeredBy,
                severity);
        }
    }

    private void ObserveUnhealthyReplicaSlots(IReadOnlyList<Resource> resources)
    {
        foreach (var resource in resources)
        {
            var projection = GetLivenessLifecycleProjection(resource);
            if (projection is null ||
                projection.State != ResourceState.Degraded)
            {
                continue;
            }

            foreach (var observation in GetFailedReplicaSlotObservations(projection.Result))
            {
                if (!TryGetReplicaSlotOrdinal(observation, out var slotOrdinal))
                {
                    continue;
                }

                replicaGroupReconciliation.ObserveUnhealthyReplicaSlot(
                    resource,
                    slotOrdinal,
                    observation.Detail,
                    ReplicaManagementTriggeredBy);
            }
        }
    }

    private static IEnumerable<ResourceHealthScopeObservation> GetFailedReplicaSlotObservations(
        ResourceHealthCheckResult result) =>
        result.Check.Type == ResourceProbeType.Liveness
            ? result.ScopeObservations.Where(observation =>
                string.Equals(observation.ScopeKind, ResourceHealthScopeKinds.Runtime, StringComparison.OrdinalIgnoreCase) &&
                observation.Status == ResourceHealthStatus.Unhealthy)
            : [];

    private static bool TryGetReplicaSlotOrdinal(
        ResourceHealthScopeObservation observation,
        out int slotOrdinal)
    {
        slotOrdinal = 0;
        return observation.ObservationAttributes.TryGetValue(
                ResourceAttributeNames.RuntimeReplicaOrdinal,
                out var value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out slotOrdinal) &&
            slotOrdinal > 0;
    }

    private static string FormatLivenessCause(ResourceHealthCheckResult result) =>
        $"Liveness check '{result.Check.Name}' failed: {result.Detail}";

    private sealed record LivenessLifecycleProjection(
        ResourceState State,
        ResourceHealthCheckResult Result);

    private static bool ShouldWarnDependents(ResourceAction action) =>
        action.Kind is ResourceActionKind.Stop or ResourceActionKind.Restart or ResourceActionKind.Pause;

    private async Task<ResourceOperationCapabilities> CreateCapabilitiesAsync(
        Resource resource,
        CancellationToken cancellationToken)
    {
        var group = resourceManager.GetGroupForResource(resource.Id);
        var canManage = authorization.CanAccessResource(
            resource.Id,
            group?.Id,
            CloudShellPermissions.Resources.Manage);
        var procedureProvider = GetProcedureProvider(resource);
        var actionCapabilities = await Task.WhenAll(resource.ResourceActions
            .Select(action => CreateActionCapabilityAsync(
                resource,
                action,
                CanAccessResource(
                    resource.Id,
                    group?.Id,
                    ResourceActionPermissions.GetRequiredPermission(action)),
                procedureProvider is not null,
                cancellationToken)));
        var executableActionIds = actionCapabilities
            .Where(capability => capability.CanExecute)
            .Select(capability => capability.ActionId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ResourceOperationCapabilities(
            resource.Id,
            canManage,
            canManage && GetDirectProcedureProvider(resource) is not null,
            executableActionIds,
            actionCapabilities);
    }

    private async Task<ResourceActionCapability> CreateActionCapabilityAsync(
        Resource resource,
        ResourceAction action,
        bool canExecute,
        bool hasProcedureProvider,
        CancellationToken cancellationToken)
    {
        if (!canExecute)
        {
            var permission = ResourceActionPermissions.GetRequiredPermission(action);
            return new ResourceActionCapability(
                action.Id,
                false,
                $"The '{FormatPermissionRequirement(permission)}' permission is required for resource '{ResourceDisplayLabels.GetName(resource)}'.");
        }

        if (!hasProcedureProvider)
        {
            return new ResourceActionCapability(
                action.Id,
                false,
                "The resource provider does not support procedures.");
        }

        var unavailableReason = GetActionUnavailableReason(resource, action);
        unavailableReason ??= await orchestration.GetActionUnavailableReasonAsync(
            resource,
            action,
            cancellationToken);
        return new ResourceActionCapability(
            action.Id,
            unavailableReason is null,
            unavailableReason);
    }

    private bool CanAccessResource(
        string resourceId,
        string? resourceGroupId,
        string permission,
        ResourceIdentityReference? actingIdentity = null)
    {
        if (actingIdentity is null)
        {
            return authorization.CanAccessResource(resourceId, resourceGroupId, permission) ||
                !string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase) &&
                authorization.CanAccessResource(resourceId, resourceGroupId, CloudShellPermissions.Resources.Manage);
        }

        return ResourceIdentityCanAccessResource(actingIdentity, resourceId, permission);
    }

    private void EnsureCanReadLogs() =>
        EnsureHasAnyPermission(
            ObservabilityAuthorization.LogsReadPermissions,
            "logs");

    private void EnsureCanReadTraces() =>
        EnsureHasAnyPermission(
            ObservabilityAuthorization.TracesReadPermissions,
            "traces");

    private void EnsureCanReadMetrics() =>
        EnsureHasAnyPermission(
            ObservabilityAuthorization.MetricsReadPermissions,
            "metrics");

    private void EnsureCanReadUsage() =>
        EnsureHasAnyPermission(
            UsageAuthorization.UsageReadPermissions,
            "usage");

    private void EnsureCanUploadDeploymentArtifacts()
    {
        EnsureDeploymentArtifactsEnabled();

        if (authorization.HasPermission(CloudShellPermissions.Deployments.Artifacts.Upload))
        {
            return;
        }

        throw new ControlPlaneAccessDeniedException(new ControlPlaneError(
            ControlPlaneErrorCodes.InsufficientPermission,
            $"The '{CloudShellPermissions.Deployments.Artifacts.Upload}' permission is required to upload deployment artifacts."));
    }

    private void EnsureCanReadDeploymentArtifacts()
    {
        EnsureDeploymentArtifactsEnabled();

        if (authorization.HasPermission(CloudShellPermissions.Deployments.Artifacts.Read) ||
            authorization.HasPermission(CloudShellPermissions.Deployments.Artifacts.Upload))
        {
            return;
        }

        throw new ControlPlaneAccessDeniedException(new ControlPlaneError(
            ControlPlaneErrorCodes.InsufficientPermission,
            $"The '{CloudShellPermissions.Deployments.Artifacts.Read}' or '{CloudShellPermissions.Deployments.Artifacts.Upload}' permission is required to read deployment artifact revisions."));
    }

    private void EnsureDeploymentArtifactsEnabled()
    {
        if (deploymentArtifactOptions.Enabled)
        {
            return;
        }

        throw new ControlPlaneException(ControlPlaneError.FeatureDisabled(
            "Deployment artifact operations are disabled for this Control Plane host. Set DeploymentArtifacts:Enabled to true and configure a deployment artifact store to enable application artifact upload and revision operations."));
    }

    private IDeploymentArtifactStore RequireDeploymentArtifactStore() =>
        deploymentArtifacts ??
        throw new InvalidOperationException(
            "No deployment artifact store is registered for this Control Plane host.");

    private void EnsureHasAnyPermission(
        IReadOnlyList<string> permissions,
        string area)
    {
        if (authorization.HasAnyPermission(permissions))
        {
            return;
        }

        throw new ControlPlaneAccessDeniedException(new ControlPlaneError(
            ControlPlaneErrorCodes.InsufficientPermission,
            $"One of the following permissions is required to read observability {area}: {string.Join(", ", permissions.Select(permission => $"'{permission}'"))}."));
    }

    private IReadOnlyList<LogSource> GetReadableLogSources()
    {
        var readableResourceIds = GetReadableResourceIds();
        return logs.GetLogSources()
            .Where(log => log.ResourceId is null || readableResourceIds.Contains(log.ResourceId))
            .ToArray();
    }

    private LogSource? GetReadableLogSource(string logSourceId) =>
        GetReadableLogSources()
            .FirstOrDefault(source => string.Equals(source.Id, logSourceId, StringComparison.OrdinalIgnoreCase));

    private HashSet<string> GetReadableResourceIds()
    {
        var resources = resourceManager.GetResources();
        var readable = resources
            .Where(resource =>
                authorization.GetResourceAccessLevel(
                    resource.Id,
                    resourceManager.GetGroupForResource(resource.Id)?.Id)
                    .AllowsRead())
            .Select(resource => resource.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in resources)
        {
            if ((!string.IsNullOrWhiteSpace(resource.ParentResourceId) && readable.Contains(resource.ParentResourceId)) ||
                (!string.IsNullOrWhiteSpace(resource.OwnerResourceId) && readable.Contains(resource.OwnerResourceId)))
            {
                readable.Add(resource.Id);
            }
        }

        return readable;
    }

    private void AuthorizeResourceIdentityProvisioning(ResourceIdentityProvisioningPlan plan)
    {
        foreach (var provisioningResourceId in plan.Requests
            .Select(request => request.Provider.ProvisioningResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provisioningResource = resourceManager.GetResource(provisioningResourceId!)
                ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provisioningResourceId!));
            var group = resourceManager.GetGroupForResource(provisioningResource.Id);
            if (!authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities) &&
                !authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw ControlPlaneAccessDeniedException.ForResource(
                    provisioningResource.Id,
                    FormatPermissionRequirement(ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities));
            }
        }
    }

    private void AuthorizeResourceIdentityProvisioningStatus(ResourceIdentityProvisioningPlan plan)
    {
        foreach (var provisioningResourceId in plan.Requests
            .Select(request => request.Provider.ProvisioningResourceId)
            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var provisioningResource = resourceManager.GetResource(provisioningResourceId!)
                ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provisioningResourceId!));
            var group = resourceManager.GetGroupForResource(provisioningResource.Id);
            if (!authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Read) &&
                !authorization.CanAccessResource(
                    provisioningResource.Id,
                    group?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw ControlPlaneAccessDeniedException.ForResource(
                    provisioningResource.Id,
                    FormatPermissionRequirement(CloudShellPermissions.Resources.Read));
            }
        }
    }

    private void AuthorizeResourceIdentityProviderSetup(ResourceIdentityProviderDefinition provider)
    {
        if (string.IsNullOrWhiteSpace(provider.ProvisioningResourceId))
        {
            if (!authorization.HasPermission(CloudShellPermissions.Resources.Manage))
            {
                throw new ControlPlaneAccessDeniedException(new ControlPlaneError(
                    ControlPlaneErrorCodes.InsufficientPermission,
                    $"The '{CloudShellPermissions.Resources.Manage}' permission is required to set up resource identity provider '{provider.Id}'."));
            }

            return;
        }

        var provisioningResource = resourceManager.GetResource(provider.ProvisioningResourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(provider.ProvisioningResourceId));
        var group = resourceManager.GetGroupForResource(provisioningResource.Id);
        if (!authorization.CanAccessResource(
                provisioningResource.Id,
                group?.Id,
                ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities) &&
            !authorization.CanAccessResource(
                provisioningResource.Id,
                group?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                provisioningResource.Id,
                FormatPermissionRequirement(ResourceIdentityProvisioningOperationPermissions.ProvisionIdentities));
        }
    }

    private bool ResourceIdentityCanAccessResource(
        ResourceIdentityReference identity,
        string resourceId,
        string permission)
    {
        var evaluator = declarations.CreatePermissionGrantEvaluator();
        return evaluator.Evaluate(identity, resourceId, permission).IsAllowed ||
            !string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase) &&
            evaluator.Evaluate(identity, resourceId, CloudShellPermissions.Resources.Manage).IsAllowed;
    }

    private ICloudShellAuthorizationService CreateAuthorizationService(ResourceIdentityReference? actingIdentity) =>
        actingIdentity is null
            ? authorization
            : new ResourcePermissionGrantAuthorizationService(this, actingIdentity);

    private static string FormatPermissionRequirement(string permission) =>
        string.Equals(permission, CloudShellPermissions.Resources.Manage, StringComparison.OrdinalIgnoreCase)
            ? permission
            : $"{permission}' or '{CloudShellPermissions.Resources.Manage}";

    private static string? GetActionUnavailableReason(
        Resource resource,
        ResourceAction action) =>
        action.Kind switch
        {
            ResourceActionKind.Start when resource.State is not (
                ResourceState.Stopped or
                ResourceState.Paused or
                ResourceState.Unknown) =>
                $"Resource '{resource.Name}' cannot start while it is {FormatState(resource.State)}.",
            ResourceActionKind.Stop when resource.State is not (
                ResourceState.Running or
                ResourceState.Starting or
                ResourceState.Stopping or
                ResourceState.Paused or
                ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot stop while it is {FormatState(resource.State)}.",
            ResourceActionKind.Pause when resource.State is not (ResourceState.Running or ResourceState.Degraded) =>
                $"Resource '{resource.Name}' cannot pause while it is {FormatState(resource.State)}.",
            ResourceActionKind.Restart when resource.State is ResourceState.Stopping or ResourceState.Stopped or ResourceState.Paused or ResourceState.Unknown =>
                $"Resource '{resource.Name}' cannot restart while it is {FormatState(resource.State)}.",
            _ => null
        };

    private static string FormatState(ResourceState? state) =>
        state?.ToString().ToLowerInvariant() ?? "no lifecycle status";

    private IResourceProcedureProvider? GetProcedureProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceProcedureProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private IResourceProcedureProvider? GetDirectProcedureProvider(Resource resource)
    {
        var registration = registrations.GetRegistration(resource.Id);
        if (registration is null)
        {
            return null;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private IResourceImageUpdateProvider? GetImageUpdateProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceImageUpdateProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceImageUpdateProvider;
    }

    private IResourceReplicaUpdateProvider? GetReplicaUpdateProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceReplicaUpdateProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceReplicaUpdateProvider;
    }

    private IResourceOrchestratorDeploymentProvider? GetDeploymentProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceOrchestratorDeploymentProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceOrchestratorDeploymentProvider;
    }

    private IResourceOrchestratorDeploymentTearDownProvider? GetDeploymentTearDownProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceOrchestratorDeploymentTearDownProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceOrchestratorDeploymentTearDownProvider;
    }

    private IResourceOrchestratorDeploymentAppliedProvider? GetDeploymentAppliedProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceOrchestratorDeploymentAppliedProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceOrchestratorDeploymentAppliedProvider;
    }

    private IResourceOrchestratorDeploymentFailureProvider? GetDeploymentFailureProvider(Resource resource)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        if (registration is not null)
        {
            return resourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceOrchestratorDeploymentFailureProvider;
        }

        return resourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceOrchestratorDeploymentFailureProvider;
    }

    private ResourceProcedureContext CreateProcedureContext(
        Resource resource,
        string? triggeredBy = null,
        string? cause = null)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        var group = resourceManager.GetGroupForResource(resource.Id);
        return new ResourceProcedureContext(
            resource,
            registration,
            group?.Id,
            registrations,
            resourceManager,
            orchestration.PreferredContainerHostId,
            triggeredBy,
            cause,
            resourceEvents);
    }

    private ResourceRegistration? GetRegistrationForResourceOrAncestor(Resource resource)
    {
        var current = resource;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (visited.Add(current.Id))
        {
            var registration = registrations.GetRegistration(current.Id);
            if (registration is not null)
            {
                return registration;
            }

            if (current.ParentResourceId is null)
            {
                return null;
            }

            var parent = resourceManager.GetResource(current.ParentResourceId);
            if (parent is null)
            {
                return null;
            }

            current = parent;
        }

        return null;
    }

    private void EnsureProviderExists(string providerId)
    {
        if (!resourceManager.Providers.Any(provider =>
                string.Equals(provider.Id, providerId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceProviderNotFound(providerId));
        }
    }

    private void EnsureAvailableResourceExists(string resourceId)
    {
        if (!resourceManager.GetAvailableResources().Any(resource =>
                string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceNotAvailable(resourceId));
        }
    }

    private void EnsureRegisteredResourceExists(string resourceId)
    {
        if (resourceManager.GetResource(resourceId) is null)
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(resourceId));
        }
    }

    private void EnsureResourceGroupExists(string? resourceGroupId)
    {
        if (resourceGroupId is null)
        {
            return;
        }

        if (!resourceManager.GetResourceGroups().Any(group =>
                string.Equals(group.Id, resourceGroupId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ControlPlaneException(ControlPlaneError.ResourceGroupNotFound(resourceGroupId));
        }
    }

    private ResourceClass? ResolveCreationResourceClass(
        string resourceId,
        string resourceType,
        ResourceClass? resourceClass)
    {
        var expectedResourceClass = resourceManager.GetResourceTypeClass(resourceType);
        if (expectedResourceClass is null)
        {
            return resourceClass;
        }

        var result = ResourceModelValidation.ResolveResourceClass(
            resourceId,
            resourceType,
            expectedResourceClass.Value,
            resourceClass,
            "creation request");

        return result.Succeeded
            ? result.ResourceClass
            : throw new ControlPlaneException(
                ControlPlaneError.ResourceClassMismatch(result.Diagnostic!.Message));
    }

    private IReadOnlyList<string>? NormalizeOptionalDependencies(
        string resourceId,
        IReadOnlyList<string>? dependsOn) =>
        dependsOn is null
            ? null
            : NormalizeDependencies(resourceId, dependsOn);

    private IReadOnlyList<string> NormalizeRequiredDependencies(
        string resourceId,
        IReadOnlyList<string> dependsOn) =>
        NormalizeDependencies(resourceId, dependsOn);

    private IReadOnlyList<string> NormalizeDependencies(
        string resourceId,
        IReadOnlyList<string> dependsOn)
    {
        var dependencies = new List<string>(dependsOn.Count);
        foreach (var dependency in dependsOn)
        {
            var dependencyId = RequireValue(dependency, nameof(dependsOn));
            if (string.Equals(dependencyId, resourceId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ControlPlaneException(ControlPlaneError.ResourceSelfDependency(resourceId));
            }

            EnsureAvailableResourceExists(dependencyId);
            dependencies.Add(dependencyId);
        }

        return dependencies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string RequireValue(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest($"{name} is required."));
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyDictionary<string, string>? NormalizeAttributes(
        IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[name.Trim()] = value.Trim();
        }

        return normalized.Count == 0 ? null : normalized;
    }

    private static JsonElement RequireConfiguration(JsonElement configuration)
    {
        if (configuration.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            throw new ControlPlaneException(ControlPlaneError.InvalidRequest("Configuration is required."));
        }

        return configuration;
    }

    private IReadOnlyList<Resource> ApplyQuery(
        IReadOnlyList<Resource> resources,
        ResourceQuery? query)
    {
        if (query is null)
        {
            return resources;
        }

        var filtered = resources.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.ResourceGroupId))
        {
            filtered = filtered.Where(resource =>
                string.Equals(
                    resourceManager.GetGroupForResource(resource.Id)?.Id,
                    query.ResourceGroupId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ParentResourceId))
        {
            filtered = filtered.Where(resource =>
                string.Equals(
                    resource.ParentResourceId,
                    query.ParentResourceId,
                    StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.ResourceType))
        {
            filtered = filtered.Where(resource =>
                string.Equals(resource.EffectiveTypeId, query.ResourceType, StringComparison.OrdinalIgnoreCase));
        }

        if (query.IsRegistered is not null)
        {
            filtered = filtered.Where(resource =>
                resourceManager.IsRegistered(resource.Id) == query.IsRegistered);
        }

        if (query.ResourceClass is not null)
        {
            filtered = filtered.Where(resource => resource.ResourceClass == query.ResourceClass);
        }

        return filtered.ToArray();
    }

    private IReadOnlyList<ResourcePrincipal> CreateResourceIdentityPrincipals(
        ResourcePrincipalQuery? query,
        ResourceIdentityProviderCatalog effectiveIdentityProviders)
    {
        if (query?.PrincipalKinds.Count > 0 &&
            !query.PrincipalKinds.Contains(ResourcePrincipalKind.ResourceIdentity))
        {
            return [];
        }

        return resourceManager.GetResources()
            .Where(resource => resource.IdentityBinding is not null)
            .Select(resource =>
            {
                var identity = resource.IdentityBinding!;
                var providerId = identity.ProviderId;
                if (string.IsNullOrWhiteSpace(providerId))
                {
                    var resolution = effectiveIdentityProviders.Resolve(identity);
                    providerId = resolution.Provider?.Id;
                }

                var reference = ResourcePrincipalReference.ForResourceIdentity(
                    resource.Id,
                    identity.Name,
                    resource.DisplayName ?? resource.Name,
                    providerId);
                return new ResourcePrincipal(
                    reference,
                    resource.DisplayName ?? resource.Name,
                    resource.EffectiveTypeId,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["resourceId"] = resource.Id,
                        ["resourceName"] = resource.Name,
                        ["resourceType"] = resource.EffectiveTypeId,
                        ["resourceClass"] = resource.ResourceClass.ToString()
                    });
            })
            .Where(principal => MatchesPrincipalQuery(principal, query))
            .ToArray();
    }

    private static bool MatchesPrincipalQuery(ResourcePrincipal principal, ResourcePrincipalQuery? query)
    {
        if (query is null)
        {
            return true;
        }

        if (query.PrincipalKinds.Count > 0 &&
            !query.PrincipalKinds.Contains(principal.Reference.Kind))
        {
            return false;
        }

        if (!MatchesProvider(query, principal.Reference.ProviderId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query.SearchText))
        {
            return true;
        }

        var search = query.SearchText;
        return Contains(principal.DisplayName, search) ||
            Contains(principal.Description, search) ||
            Contains(principal.Reference.Id, search) ||
            Contains(principal.Reference.DisplayName, search) ||
            Contains(principal.Reference.SourceResourceId, search) ||
            Contains(principal.Reference.SourceIdentityName, search) ||
            principal.PrincipalAttributes.Any(attribute =>
                Contains(attribute.Key, search) || Contains(attribute.Value, search));
    }

    private static bool MatchesProvider(ResourcePrincipalQuery? query, string? providerId) =>
        string.IsNullOrWhiteSpace(query?.ProviderId) ||
        string.Equals(providerId, query.ProviderId, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldQueryDirectoryProviders(ResourcePrincipalQuery? query) =>
        query?.PrincipalKinds.Count is not > 0 ||
        query.PrincipalKinds.Any(kind => kind != ResourcePrincipalKind.ResourceIdentity);

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<ResourcePermissionGrant> ApplyQuery(
        IReadOnlyList<ResourcePermissionGrant> grants,
        ResourcePermissionGrantQuery? query)
    {
        if (query is null)
        {
            return grants;
        }

        var filtered = grants.AsEnumerable();
        if (query.Principal is not null)
        {
            filtered = filtered.Where(grant =>
                MatchesPrincipalFilter(grant.Principal, query.Principal));
        }

        if (!string.IsNullOrWhiteSpace(query.TargetResourceId))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.TargetResourceId, query.TargetResourceId, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Permission))
        {
            filtered = filtered.Where(grant =>
                string.Equals(grant.Permission, query.Permission, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToArray();
    }

    private static bool MatchesPrincipalFilter(
        ResourcePrincipalReference grantPrincipal,
        ResourcePrincipalReference principalFilter) =>
        grantPrincipal.Kind == principalFilter.Kind &&
        string.Equals(grantPrincipal.Id, principalFilter.Id, StringComparison.OrdinalIgnoreCase) &&
        (string.IsNullOrWhiteSpace(principalFilter.ProviderId) ||
         string.Equals(grantPrincipal.ProviderId, principalFilter.ProviderId, StringComparison.OrdinalIgnoreCase));

    private async Task<ResourcePermissionGrantStatus> GetPermissionGrantStatusAsync(
        ResourcePermissionGrant grant,
        CancellationToken cancellationToken)
    {
        var targetResource = resourceManager.GetResource(grant.TargetResourceId);
        if (targetResource is null)
        {
            return new ResourcePermissionGrantStatus(
                grant,
                ResourcePermissionGrantEffectivenessState.Unknown,
                $"Target resource '{ResourceDisplayLabels.GetName(grant.TargetResourceId)}' is not available.");
        }

        var request = new ResourcePermissionGrantStatusRequest(targetResource, grant);
        foreach (var provider in permissionGrantStatusProviders)
        {
            if (provider.CanGetStatus(request))
            {
                return await provider.GetStatusAsync(request, cancellationToken);
            }
        }

        return new ResourcePermissionGrantStatus(
            grant,
            ResourcePermissionGrantEffectivenessState.Unknown,
            "No provider reports effective access status for this grant.");
    }

    private ResourcePermissionGrant CreatePermissionGrant(
        ResourcePrincipalReference principal,
        string targetResourceId,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(principal);
        targetResourceId = RequireValue(targetResourceId, nameof(targetResourceId));
        permission = RequireValue(permission, nameof(permission));
        var targetResource = resourceManager.GetResource(targetResourceId)
            ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(targetResourceId));

        if (principal.Kind == ResourcePrincipalKind.ResourceIdentity)
        {
            var identityResourceId = RequireValue(principal.SourceResourceId, nameof(principal.SourceResourceId));
            var identityResource = resourceManager.GetResource(identityResourceId)
                ?? throw new ControlPlaneException(ControlPlaneError.ResourceNotRegistered(identityResourceId));
            if (identityResource.IdentityBinding is null)
            {
                throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                    $"Resource '{identityResource.Id}' does not have an identity binding."));
            }

            var normalizedIdentityName = string.IsNullOrWhiteSpace(principal.SourceIdentityName)
                ? identityResource.IdentityBinding.Name
                : principal.SourceIdentityName.Trim();
            if (!string.Equals(identityResource.IdentityBinding.Name, normalizedIdentityName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ControlPlaneException(ControlPlaneError.InvalidRequest(
                    $"Resource '{identityResource.Id}' does not have identity '{normalizedIdentityName}'."));
            }

            var identityGroup = resourceManager.GetGroupForResource(identityResource.Id);
            if (!authorization.CanAccessResource(
                    identityResource.Id,
                    identityGroup?.Id,
                    CloudShellPermissions.Resources.Manage))
            {
                throw ControlPlaneAccessDeniedException.ForResource(
                    identityResource.Id,
                    CloudShellPermissions.Resources.Manage);
            }

            principal = ResourcePrincipalReference.ForResourceIdentity(
                identityResource.Id,
                normalizedIdentityName,
                principal.DisplayName,
                principal.ProviderId);
        }

        var targetGroup = resourceManager.GetGroupForResource(targetResource.Id);
        if (!authorization.CanAccessResource(
                targetResource.Id,
                targetGroup?.Id,
                CloudShellPermissions.Resources.Manage))
        {
            throw ControlPlaneAccessDeniedException.ForResource(
                targetResource.Id,
                CloudShellPermissions.Resources.Manage);
        }

        return new ResourcePermissionGrant(
            principal,
            targetResource.Id,
            permission);
    }

    private static IReadOnlyList<string> GetGrantAffectedResourceIds(ResourcePermissionGrant grant)
    {
        var affected = new List<string>();
        if (grant.ResourceIdentity is { } identity)
        {
            affected.Add(identity.ResourceId);
        }

        affected.Add(grant.TargetResourceId);
        return affected
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IReadOnlySet<string>? GetScopedResourceIds(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return null;
        }

        var normalizedResourceId = resourceId.Trim();
        var scoped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            normalizedResourceId
        };
        foreach (var resource in resourceManager.GetResources())
        {
            if (string.Equals(resource.ParentResourceId, normalizedResourceId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(resource.OwnerResourceId, normalizedResourceId, StringComparison.OrdinalIgnoreCase))
            {
                scoped.Add(resource.Id);
            }
        }

        return scoped;
    }

    private static IReadOnlyList<LogSource> ApplyQuery(
        IReadOnlyList<LogSource> sources,
        LogQuery? query,
        IReadOnlySet<string>? scopedResourceIds = null)
    {
        if (query is null)
        {
            return sources;
        }

        var filtered = sources.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(query.ResourceId))
        {
            filtered = filtered.Where(source =>
                !string.IsNullOrWhiteSpace(source.ResourceId) &&
                (scopedResourceIds?.Contains(source.ResourceId) == true ||
                    string.Equals(source.ResourceId, query.ResourceId, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(query.ArtifactId))
        {
            filtered = filtered.Where(source =>
                string.Equals(source.ArtifactId, query.ArtifactId, StringComparison.OrdinalIgnoreCase));
        }

        if (query.SourceKind is not null)
        {
            filtered = filtered.Where(source => source.SourceKind == query.SourceKind);
        }

        return filtered.ToArray();
    }

    private sealed class ResourcePermissionGrantAuthorizationService(
        InProcessControlPlane controlPlane,
        ResourceIdentityReference identity) : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) =>
            controlPlane.ResourceIdentityCanAccessResource(identity, resourceId, permission);
    }
}
