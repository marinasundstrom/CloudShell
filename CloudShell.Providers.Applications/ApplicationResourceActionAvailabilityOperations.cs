using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceActionAvailabilityOperations(
    IApplicationResourceDefinitionSource definitions,
    IApplicationResourceRunningStateOperations runningState,
    ApplicationResourceSettingResolver settingResolver,
    ApplicationWorkloadConfigurationProvider workloadConfigurations,
    ApplicationContainerHostResolver containerHosts,
    ApplicationProviderOptions options,
    IHostEnvironment environment,
    ResourceDeclarationStore declarations) :
    IApplicationResourceActionAvailabilityOperations
{
    private static readonly ApplicationContainerOrchestratorDeploymentFactory ContainerOrchestratorDeploymentFactory = new();
    private readonly ApplicationResourcePortResolver ports = new(options);

    public bool CanEvaluateAction(Resource resource, ResourceAction action) =>
        ApplicationResourceTypes.IsApplication(resource.EffectiveTypeId) &&
        definitions.GetApplication(resource.Id) is not null &&
        (action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart ||
         string.Equals(action.Id, ApplicationResourceActionIds.ReconcileSqlServerAccess, StringComparison.OrdinalIgnoreCase));

    public async Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(action.Id, ApplicationResourceActionIds.ReconcileSqlServerAccess, StringComparison.OrdinalIgnoreCase))
        {
            var sqlServer = definitions.GetApplication(context.Resource.Id);
            if (sqlServer is null ||
                !string.Equals(sqlServer.ResourceType, ApplicationResourceTypes.SqlServer, StringComparison.OrdinalIgnoreCase))
            {
                return "Only SQL Server resources can reconcile database access.";
            }

            if (!runningState.IsRunning(sqlServer.Id))
            {
                return $"SQL Server resource '{FormatApplicationResourceName(sqlServer)}' must be running before database access can be reconciled.";
            }

            return sqlServer.SqlDatabases.Count == 0
                ? $"SQL Server resource '{FormatApplicationResourceName(sqlServer)}' has no declared databases to reconcile access for."
                : null;
        }

        if (action.Kind is not (ResourceActionKind.Start or ResourceActionKind.Restart))
        {
            return null;
        }

        var application = definitions.GetApplication(context.Resource.Id);
        if (application is null)
        {
            return null;
        }

        var referenceReason = GetReferenceUnavailableReason(application, context);
        if (!string.IsNullOrWhiteSpace(referenceReason))
        {
            return referenceReason;
        }

        var settingResolutionReason = await settingResolver.GetSettingResolutionUnavailableReasonAsync(
            application,
            context.ResourceGroupId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(settingResolutionReason))
        {
            return settingResolutionReason;
        }

        var localProcessReason = GetLocalProcessUnavailableReason(application);
        if (!string.IsNullOrWhiteSpace(localProcessReason))
        {
            return localProcessReason;
        }

        var projectReason = GetProjectUnavailableReason(application);
        if (!string.IsNullOrWhiteSpace(projectReason))
        {
            return projectReason;
        }

        var containerHost = await TryResolveContainerHostForAvailabilityAsync(
            application,
            context.ResourceManager,
            context.PreferredContainerHostId,
            cancellationToken);
        var containerHostReason = await GetContainerHostUnavailableReasonAsync(
            application,
            context.ResourceManager,
            context.PreferredContainerHostId,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(containerHostReason))
        {
            return containerHostReason;
        }

        if (containerHost is not null)
        {
            var registryCredentialReason = GetRegistryCredentialUnavailableReason(application);
            if (!string.IsNullOrWhiteSpace(registryCredentialReason))
            {
                return registryCredentialReason;
            }
        }

        var volumeReason = ApplicationResourceVolumeMounts.GetVolumeMountUnavailableReason(
            application.VolumeMounts,
            context.ResourceManager,
            environment.ContentRootPath,
            containerHost);
        if (!string.IsNullOrWhiteSpace(volumeReason))
        {
            return volumeReason;
        }

        return GetEndpointUnavailableReason(application, action.Kind);
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
        var identity = settingResolver.ResolveIdentity(definition.Id);
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

        var targetLabel = ResourceDisplayLabels.GetLabel(target, referencedResourceId);
        return $"Setting '{settingName}' references '{targetLabel}', but identity '{FormatIdentity(identity, definition)}' " +
            $"is not allowed to read {readableItemLabel}. Grant '{permission}' on resource '{targetLabel}'.";
    }

    private string? GetProjectUnavailableReason(ApplicationResourceDefinition application)
    {
        if (!IsProjectBacked(application))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(application.ProjectPath))
        {
            return $"Project-backed application resource '{FormatApplicationResourceName(application)}' does not declare a project path.";
        }

        var projectPath = application.ProjectPath.Trim();
        var resolvedPath = ResolveProjectPath(application);
        return File.Exists(resolvedPath) || Directory.Exists(resolvedPath)
            ? null
            : $"Project-backed application resource '{FormatApplicationResourceName(application)}' cannot start because project path '{projectPath}' was not found at '{resolvedPath}'.";
    }

    private string? GetLocalProcessUnavailableReason(ApplicationResourceDefinition application)
    {
        if (IsContainerBacked(application))
        {
            return null;
        }

        var workingDirectory = ResolveConfiguredWorkingDirectory(application);
        if (!Directory.Exists(workingDirectory))
        {
            return $"Application resource '{FormatApplicationResourceName(application)}' cannot start because working directory '{workingDirectory}' was not found.";
        }

        if (IsProjectBacked(application))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(application.ExecutablePath))
        {
            return $"Executable application resource '{FormatApplicationResourceName(application)}' does not declare an executable path.";
        }

        var executablePath = application.ExecutablePath.Trim();
        if (!IsExplicitExecutablePath(executablePath))
        {
            return null;
        }

        var resolvedPath = ResolveConfiguredExecutablePath(application, workingDirectory);
        return File.Exists(resolvedPath)
            ? null
            : $"Executable application resource '{FormatApplicationResourceName(application)}' cannot start because executable path '{executablePath}' was not found at '{resolvedPath}'.";
    }

    private static string? GetRegistryCredentialUnavailableReason(ApplicationResourceDefinition application)
    {
        if (!IsContainerBacked(application))
        {
            return null;
        }

        var credentials = ContainerRegistryCredentials.Normalize(application.ContainerRegistryCredentials);
        if (credentials is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(Environment.GetEnvironmentVariable(credentials.NormalizedPasswordEnvironmentVariable))
            ? $"Container app resource '{FormatApplicationResourceName(application)}' cannot access registry '{GetImageRegistryAddress(GetEffectiveContainerRegistry(application))}' because credential environment variable '{credentials.NormalizedPasswordEnvironmentVariable}' is not configured."
            : null;
    }

    private string? GetEndpointUnavailableReason(
        ApplicationResourceDefinition application,
        ResourceActionKind actionKind)
    {
        if (actionKind == ResourceActionKind.Restart &&
            runningState.IsRunning(application.Id))
        {
            return null;
        }

        return IsContainerBacked(application)
            ? GetContainerEndpointUnavailableReason(application)
            : GetLocalProcessEndpointUnavailableReason(application);
    }

    private string? GetLocalProcessEndpointUnavailableReason(ApplicationResourceDefinition application)
    {
        foreach (var mapping in CreateEndpointNetworkMappings(application))
        {
            if (!TryGetLoopbackEndpoint(mapping, out var addresses, out var port))
            {
                continue;
            }

            if (addresses.Any(address => !IsTcpPortAvailable(address, port)))
            {
                return
                    $"Endpoint mapping '{mapping.Name}' for application resource '{application.Id}' cannot use {mapping.Address} because the address is already in use.";
            }
        }

        return null;
    }

    private string? GetContainerEndpointUnavailableReason(ApplicationResourceDefinition application)
    {
        var occupiedPorts = new HashSet<int>();
        var service = CreateDefaultContainerOrchestratorService(application);
        foreach (var port in service.ServicePorts)
        {
            var localPort = ports.ResolveLocalPort(application.Id, port);
            if (!occupiedPorts.Add(localPort))
            {
                return $"Endpoint '{port.Name}' for container app resource '{application.Id}' cannot use local port {localPort.ToString(CultureInfo.InvariantCulture)} because another endpoint on the resource already uses that port.";
            }

            if (!IsLocalHostPortAvailable(localPort))
            {
                return $"Endpoint '{port.Name}' for container app resource '{application.Id}' cannot use local port {localPort.ToString(CultureInfo.InvariantCulture)} because the address is already in use.";
            }
        }

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        foreach (var instance in replicaGroup.Instances)
        {
            foreach (var port in GetRuntimeContainerProbePorts(application, service))
            {
                var localPort = ports.ResolveReplicaProbeLocalPort(application.Id, port, instance);
                if (!occupiedPorts.Add(localPort))
                {
                    return $"Replica probe endpoint '{port.Name}' for container app resource '{application.Id}' cannot use local port {localPort.ToString(CultureInfo.InvariantCulture)} because another endpoint on the resource already uses that port.";
                }

                if (!IsLocalHostPortAvailable(localPort))
                {
                    return $"Replica probe endpoint '{port.Name}' for container app resource '{application.Id}' cannot use local port {localPort.ToString(CultureInfo.InvariantCulture)} because the address is already in use.";
                }
            }
        }

        return null;
    }

    private async Task<ContainerHostDescriptor?> TryResolveContainerHostForAvailabilityAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken)
    {
        if (!IsContainerBacked(definition) ||
            resourceManager is null)
        {
            return null;
        }

        try
        {
            return await containerHosts.ResolveAsync(
                definition.ContainerHostId,
                preferredContainerHostId,
                resourceManager,
                ContainerHostCapabilityIds.ContainerImage,
                cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<string?> GetContainerHostUnavailableReasonAsync(
        ApplicationResourceDefinition definition,
        IResourceManagerStore? resourceManager,
        string? preferredContainerHostId,
        CancellationToken cancellationToken)
    {
        if (!IsContainerBacked(definition))
        {
            return null;
        }

        if (resourceManager is null)
        {
            return $"Container resource '{definition.Name}' requires resource manager context to resolve a container host.";
        }

        try
        {
            _ = await containerHosts.ResolveAsync(
                definition.ContainerHostId,
                preferredContainerHostId,
                resourceManager,
                ContainerHostCapabilityIds.ContainerImage,
                cancellationToken);

            if (definition.ProjectContainerBuild)
            {
                _ = await containerHosts.ResolveAsync(
                    definition.ContainerHostId,
                    preferredContainerHostId,
                    resourceManager,
                    ContainerHostCapabilityIds.ContainerBuild,
                    cancellationToken);
            }
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }

        return null;
    }

    private IReadOnlyList<ResourceEndpointNetworkMapping> CreateEndpointNetworkMappings(
        ApplicationResourceDefinition application)
    {
        if (application.EndpointPorts.Count > 0)
        {
            return application.EndpointPorts
                .Select(port => ResourceEndpointNetworkMapping.ForEndpoint(
                    application.Id,
                    port.Name,
                    CreateServiceEndpointAddress(application.Id, port),
                    port.Exposure,
                    networkResourceId: NormalizeNullable(port.NetworkResourceId),
                    sourceEndpointName: port.Name))
                .ToArray();
        }

        if (string.IsNullOrWhiteSpace(application.Endpoint))
        {
            return [];
        }

        return
        [
            ResourceEndpointNetworkMapping.ForEndpoint(
                application.Id,
                "application",
                application.Endpoint,
                ResourceExposureScope.Public,
                sourceEndpointName: "application")
        ];
    }

    private string CreateServiceEndpointAddress(string resourceId, ServicePort port)
    {
        var protocol = NormalizeProtocol(port.Protocol);
        var host = FirstNonEmpty(port.IPAddress, port.Host, "localhost")!;
        return $"{protocol}://{host}:{ports.ResolveLocalPort(resourceId, port).ToString(CultureInfo.InvariantCulture)}";
    }

    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        ContainerOrchestratorDeploymentFactory.CreateService(
            application,
            workloadConfigurations.Create(application));

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

    private static bool IsHttpProbePort(ServicePort port) =>
        NormalizeProtocol(port.Protocol) is "http" or "https";

    private static bool TryGetLoopbackEndpoint(
        ResourceEndpointNetworkMapping mapping,
        out IReadOnlyList<IPAddress> addresses,
        out int port)
    {
        addresses = [];
        port = 0;
        if (!mapping.TryGetUri(out var uri) ||
            !mapping.TryGetPort(out port))
        {
            return false;
        }

        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            addresses = [IPAddress.Loopback, IPAddress.IPv6Loopback];
        }
        else if (IPAddress.TryParse(uri.Host, out var parsedAddress) &&
            IPAddress.IsLoopback(parsedAddress))
        {
            addresses = [parsedAddress];
        }
        else
        {
            return false;
        }

        return true;
    }

    private static bool IsTcpPortAvailable(IPAddress address, int port)
    {
        try
        {
            var listener = new TcpListener(address, port)
            {
                ExclusiveAddressUse = true
            };
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    private static bool IsLocalHostPortAvailable(int port) =>
        IsTcpPortAvailable(IPAddress.Any, port) &&
        IsTcpPortAvailable(IPAddress.IPv6Any, port) &&
        IsTcpPortAvailable(IPAddress.Loopback, port) &&
        IsTcpPortAvailable(IPAddress.IPv6Loopback, port);

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

    private static string ResolveConfiguredExecutablePath(
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

    private static string FormatIdentity(
        ResourceIdentityReference identity,
        ApplicationResourceDefinition? definition = null)
    {
        var resourceName = definition is not null &&
            string.Equals(identity.ResourceId, definition.Id, StringComparison.OrdinalIgnoreCase)
                ? FormatApplicationResourceName(definition)
                : identity.ResourceId;
        return string.IsNullOrWhiteSpace(identity.Name)
            ? resourceName
            : $"{resourceName}/{identity.Name}";
    }

    private static string FormatApplicationResourceName(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.Name)
            ? application.Id
            : application.Name;

    private static bool IsProjectBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsAspNetCoreProject(application.ResourceType) ||
        !string.IsNullOrWhiteSpace(application.ProjectPath);

    private static bool IsContainerBacked(ApplicationResourceDefinition application) =>
        ApplicationResourceProjectionSupport.IsContainerBacked(application);

    private static bool IsReplicaModeEnabled(ApplicationResourceDefinition application) =>
        ApplicationResourceTypes.IsContainerApp(application.ResourceType) &&
        application.ReplicasEnabled;

    private static string GetEffectiveContainerRegistry(ApplicationResourceDefinition application) =>
        string.IsNullOrWhiteSpace(application.ContainerRegistry)
            ? ContainerRegistryDefaults.Default
            : application.ContainerRegistry.Trim();

    private static string GetImageRegistryAddress(string registry) =>
        Uri.TryCreate(registry, UriKind.Absolute, out var uri) &&
            !string.IsNullOrWhiteSpace(uri.Authority)
            ? uri.Authority
            : registry.Trim().TrimEnd('/');

    private static string NormalizeProtocol(string? protocol) =>
        string.IsNullOrWhiteSpace(protocol) ? "tcp" : protocol.Trim().ToLowerInvariant();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
