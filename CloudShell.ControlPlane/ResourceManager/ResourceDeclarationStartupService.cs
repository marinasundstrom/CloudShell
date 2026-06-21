using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Persistence;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed record ResourceDeclarationStartupResult(
    IReadOnlyList<ResourceDeclarationStartupDiagnostic> Diagnostics)
{
    public int ErrorCount => Diagnostics.Count(diagnostic => diagnostic.IsError);

    public int WarningCount => Diagnostics.Count(diagnostic => diagnostic.IsWarning);

    public int InformationCount => Diagnostics.Count(diagnostic => diagnostic.IsInformation);

    public bool HasErrors => ErrorCount > 0;

    public bool HasWarnings => WarningCount > 0;
}

public sealed record ResourceDeclarationStartupDiagnostic(
    string Severity,
    string ResourceId,
    string Message)
{
    public bool IsError => string.Equals(Severity, "Error", StringComparison.OrdinalIgnoreCase);

    public bool IsWarning => string.Equals(Severity, "Warning", StringComparison.OrdinalIgnoreCase);

    public bool IsInformation => !IsError && !IsWarning;

    public static ResourceDeclarationStartupDiagnostic Warning(string resourceId, string message) =>
        new("Warning", resourceId, message);

    public static ResourceDeclarationStartupDiagnostic Error(string resourceId, string message) =>
        new("Error", resourceId, message);
}

public sealed class ResourceDeclarationStartupService(
    IEnumerable<IResourceProvider> providers,
    IEnumerable<IResourceOrchestrator> orchestrators,
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders,
    IEnumerable<IResourceActionAvailabilityProvider> actionAvailabilityProviders,
    IEnumerable<IContainerHostProvider> containerHostProviders,
    EfCoreResourceStore persistedResources,
    ResourceDeclarationStore declarations,
    ResourceOrchestratorSelectionStore selectionStore,
    ResourceIdentityProviderCatalog identityProviders,
    ResourceIdentityProvisioningService identityProvisioning,
    CloudShellExtensionRegistry extensionRegistry,
    ICloudShellExtensionActivationStore activationStore,
    IResourceEventSink? resourceEvents = null)
{
    public async Task<ResourceDeclarationStartupResult> StartAutoStartDeclarationsAsync(
        CancellationToken cancellationToken = default)
    {
        var registrations = new StartupResourceRegistrationStore(persistedResources, declarations);
        var resourceManager = new ResourceManagerStore(
            providers,
            persistedResources,
            registrations,
            declarations,
            identityProviders,
            extensionRegistry,
            activationStore);
        var orchestration = new ResourceOrchestrationService(
            orchestrators,
            descriptorProviders,
            resourceManager,
            registrations,
            declarations,
            selectionStore,
            containerHostProviders,
            actionAvailabilityProviders: actionAvailabilityProviders,
            resourceEvents: resourceEvents);
        var authorization = new StartupAuthorizationService();
        var diagnostics = new List<ResourceDeclarationStartupDiagnostic>();

        await ProvisionStartupIdentitiesAsync(resourceManager, diagnostics, cancellationToken);

        foreach (var declaration in declarations.GetDeclarations()
                     .Where(declaration => ShouldAutoStartOnControlPlaneStart(declaration, resourceManager)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var resource = resourceManager.GetResource(declaration.ResourceId);
            if (resource is null)
            {
                diagnostics.Add(ResourceDeclarationStartupDiagnostic.Error(
                    declaration.ResourceId,
                    $"Declared resource '{ResourceDisplayLabels.GetName(declaration.ResourceId)}' could not be found."));
                continue;
            }

            if (resource.State == ResourceState.Running)
            {
                continue;
            }

            var runAction = resource.ResourceActions.FirstOrDefault(action =>
                action.Kind == ResourceActionKind.Start);
            if (runAction is null)
            {
                continue;
            }

            try
            {
                await orchestration.ExecuteActionAsync(
                    resource,
                    runAction,
                    startDependencies: true,
                    authorization,
                    cancellationToken,
                    triggeredBy: "startup");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(ResourceDeclarationStartupDiagnostic.Error(
                    declaration.ResourceId,
                    exception is ControlPlaneException controlPlaneException
                        ? controlPlaneException.Error.Message
                        : exception.Message));
            }
        }

        return new ResourceDeclarationStartupResult(diagnostics);
    }

    private async Task ProvisionStartupIdentitiesAsync(
        IResourceManagerStore resourceManager,
        List<ResourceDeclarationStartupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in declarations.GetDeclarations()
                     .Where(declaration => declaration.ProvisionIdentityOnStartup))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (declaration.IdentityBinding is null)
            {
                diagnostics.Add(ResourceDeclarationStartupDiagnostic.Warning(
                    declaration.ResourceId,
                    $"Declared resource '{ResourceDisplayLabels.GetName(declaration.ResourceId)}' requested startup identity provisioning but does not declare an identity."));
                continue;
            }

            var resource = resourceManager.GetResource(declaration.ResourceId);
            if (resource is null)
            {
                diagnostics.Add(ResourceDeclarationStartupDiagnostic.Error(
                    declaration.ResourceId,
                    $"Declared resource '{ResourceDisplayLabels.GetName(declaration.ResourceId)}' could not be found."));
                continue;
            }

            try
            {
                var result = await identityProvisioning.ProvisionResourceAsync(
                    resource.Id,
                    cancellationToken);
                foreach (var diagnostic in result.ProvisioningDiagnostics)
                {
                    diagnostics.Add(new ResourceDeclarationStartupDiagnostic(
                        diagnostic.Severity.ToString(),
                        diagnostic.Identity?.ResourceId ?? resource.Id,
                        diagnostic.Message));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                diagnostics.Add(ResourceDeclarationStartupDiagnostic.Error(
                    declaration.ResourceId,
                    exception.Message));
            }
        }
    }

    private bool ShouldAutoStartOnControlPlaneStart(
        ResourceDeclaration declaration,
        IResourceManagerStore resourceManager) =>
        declaration.AutoStartOverride ??
        GetAutoStartPolicy(declaration, resourceManager)?.StartOnControlPlaneStart ??
        declarations.DefaultAutoStart;

    private static ResourceAutoStartPolicy? GetAutoStartPolicy(
        ResourceDeclaration declaration,
        IResourceManagerStore resourceManager)
    {
        var provider = resourceManager.Providers
            .OfType<IResourceAutoStartPolicyProvider>()
            .FirstOrDefault(provider =>
                string.Equals(provider.Id, declaration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider.CanEvaluateAutoStartPolicy(declaration));

        return provider?.GetAutoStartPolicy(declaration);
    }

    private sealed class StartupAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class StartupResourceRegistrationStore(
        EfCoreResourceStore persistedResources,
        ResourceDeclarationStore declarations) : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() =>
            persistedResources.GetRegistrations()
                .Concat(declarations.GetDeclarations()
                    .Where(declaration => persistedResources.GetRegistration(declaration.ResourceId) is null)
                    .Select(ToRegistration))
                .GroupBy(registration => registration.ResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            persistedResources.GetRegistration(resourceId) ??
            (declarations.GetDeclaration(resourceId) is { } declaration
                ? ToRegistration(declaration)
                : null);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            persistedResources.RegisterAsync(providerId, resourceId, resourceGroupId, dependsOn, cancellationToken);

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            persistedResources.RemoveAsync(resourceId, cancellationToken);

        public async Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            if (persistedResources.GetRegistration(resourceId) is null &&
                declarations.GetDeclaration(resourceId) is not null)
            {
                declarations.AssignToGroup(resourceId, resourceGroupId);
                if (dependsOn is not null)
                {
                    declarations.SetDependencies(resourceId, dependsOn);
                }

                return;
            }

            await persistedResources.AssignToGroupAsync(resourceId, resourceGroupId, dependsOn, cancellationToken);
        }

        public async Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            if (persistedResources.GetRegistration(resourceId) is null &&
                declarations.GetDeclaration(resourceId) is not null)
            {
                declarations.SetDependencies(resourceId, dependsOn);
                return;
            }

            await persistedResources.SetDependenciesAsync(resourceId, dependsOn, cancellationToken);
        }

        public async Task SetIdentityAsync(
            string resourceId,
            ResourceIdentityBinding? identity,
            CancellationToken cancellationToken = default)
        {
            if (persistedResources.GetRegistration(resourceId) is null &&
                declarations.GetDeclaration(resourceId) is not null)
            {
                declarations.SetIdentity(resourceId, identity);
                return;
            }

            await persistedResources.SetIdentityAsync(resourceId, identity, cancellationToken);
        }

        private static ResourceRegistration ToRegistration(ResourceDeclaration declaration) =>
            new(
                declaration.ResourceId,
                declaration.ProviderId,
                declaration.ResourceGroupId,
                declaration.DeclaredAt,
                declaration.DependsOn,
                declaration.IdentityBinding);
    }
}
