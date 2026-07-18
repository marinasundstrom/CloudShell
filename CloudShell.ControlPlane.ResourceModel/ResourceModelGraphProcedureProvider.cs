using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Resource = CloudShell.ResourceModel.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.ControlPlane.ResourceModel;

public sealed class ResourceModelGraphProcedureProvider :
    IResourceProvider,
    IResourceModelDiagnosticProvider,
    IResourceProcedureProvider,
    IResourceActionAvailabilityProvider,
    IResourceImageUpdateProvider,
    IResourceReplicaUpdateProvider,
    IResourceEnvironmentVariableConfigurationProvider,
    IResourceOrchestratorDeploymentProvider,
    IResourceOrchestratorServiceProcedureProvider
{
    private static readonly ResourceOperationId ContainerImageUpdateOperationId = "container.image.update";
    private static readonly ResourceOperationId ContainerReplicasUpdateOperationId = "container.replicas.update";
    private static readonly ResourceAttributeId ContainerImageAttributeId = "container.image";
    private static readonly ResourceAttributeId ContainerReplicasAttributeId = "container.replicas";
    private static readonly ResourceAttributeId EnvironmentVariablesAttributeId = "environment.variables";

    private readonly ResourceModelGraphResourceProvider _resourceProvider;
    private readonly ResourceModelGraphResourceResolver _resourceResolver;
    private readonly ResourceModelGraphDefinitionApplyService _definitionApply;
    private readonly ResourceDefinitionResolutionContext _resolutionContext;
    private readonly IReadOnlyList<IResourceModelGraphDeploymentDescriptor> _deploymentDescriptors;
    private readonly IReadOnlyList<IResourceModelGraphOrchestratorServiceExecutor> _serviceExecutors;

    public ResourceModelGraphProcedureProvider(
        ResourceModelGraphResourceProvider resourceProvider,
        ResourceModelGraphResourceResolver resourceResolver,
        ResourceModelGraphDefinitionApplyService definitionApply,
        ResourceDefinitionResolutionContext? resolutionContext = null,
        IEnumerable<IResourceModelGraphDeploymentDescriptor>? deploymentDescriptors = null,
        IEnumerable<IResourceModelGraphOrchestratorServiceExecutor>? serviceExecutors = null)
    {
        _resourceProvider = resourceProvider ?? throw new ArgumentNullException(nameof(resourceProvider));
        _resourceResolver = resourceResolver ?? throw new ArgumentNullException(nameof(resourceResolver));
        _definitionApply = definitionApply ?? throw new ArgumentNullException(nameof(definitionApply));
        _resolutionContext = resolutionContext ?? ResourceDefinitionResolutionContext.Empty;
        _deploymentDescriptors = (deploymentDescriptors ?? []).ToArray();
        _serviceExecutors = (serviceExecutors ?? []).ToArray();
    }

    public string Id => _resourceProvider.Id;

    public string DisplayName => _resourceProvider.DisplayName;

    public IReadOnlyList<ResourceManagerResource> GetResources() =>
        _resourceProvider.GetResources();

    public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() =>
        _resourceProvider.GetResourceModelDiagnostics();

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ResourceProcedureResult>(
            new NotSupportedException("Resource model graph resources are not deleted through this bridge provider."));

    public bool CanDescribeDeployment(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        _deploymentDescriptors.Count > 0;

    public async Task<ResourceOrchestratorDeployment?> DescribeDeploymentAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CanDescribeDeployment(context.Resource))
        {
            return null;
        }

        var resolution = await _resourceResolver.ResolveAsync(
            context.Resource.Id,
            _resolutionContext,
            cancellationToken);
        if (resolution.Target is null)
        {
            return null;
        }

        if (resolution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(resolution.Diagnostics));
        }

        var descriptor = _deploymentDescriptors.FirstOrDefault(descriptor =>
            descriptor.CanDescribeDeployment(context.Resource, resolution.Target));
        if (descriptor is null)
        {
            return null;
        }

        return await descriptor.DescribeDeploymentAsync(
            new ResourceModelGraphDeploymentDescriptionContext(
                context.Resource,
                resolution.Target,
                context),
            cancellationToken);
    }

    public bool CanExecuteOrchestratorService(
        ResourceManagerResource resource,
        ResourceAction action) =>
        IsBridgeResource(resource) &&
        _serviceExecutors.Any(executor =>
            executor.CanExecuteOrchestratorService(resource, action));

    public async Task<ResourceOrchestratorService> CreateOrchestratorServiceAsync(
        ResourceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var deployment = await DescribeDeploymentAsync(context, cancellationToken);
        if (deployment is null)
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not describe an orchestrator service.");
        }

        return deployment.Spec.Service;
    }

    public async Task PrepareOrchestratorServiceAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var graphContext = await CreateServiceProcedureContextAsync(
            context,
            action,
            cancellationToken);
        await graphContext.Executor.PrepareOrchestratorServiceAsync(
            graphContext.Context,
            action,
            cancellationToken);
    }

    public async Task ReconcileOrchestratorServiceRoutingAsync(
        ResourceOrchestratorServiceProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var graphContext = await CreateServiceProcedureContextAsync(
            context,
            ResourceAction.Start,
            cancellationToken);
        await graphContext.Executor.ReconcileOrchestratorServiceRoutingAsync(
            graphContext.Context,
            cancellationToken);
    }

    public async Task ExecuteOrchestratorServiceInstanceAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var graphContext = await CreateServiceInstanceContextAsync(
            context,
            action,
            cancellationToken);
        await graphContext.Executor.ExecuteOrchestratorServiceInstanceAsync(
            graphContext.Context,
            action,
            cancellationToken);
    }

    public bool CanUpdateImage(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        resource.HasAction(ContainerImageUpdateOperationId.ToString());

    public async Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!CanUpdateImage(context.Resource))
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not support image updates.");
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerImageAttributeId] = image
        };
        if (requestedReplicas is not null)
        {
            attributes[ContainerReplicasAttributeId] = requestedReplicas.Value;
        }

        await ApplyResourceDefinitionAttributesAsync(
            context,
            attributes,
            triggeredBy,
            cancellationToken);

        return ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
            $"Updated image for {context.Resource.Name} to '{image}'.",
            context.Resource.Id,
            "Container app image deployment must be reconciled.");
    }

    public bool CanUpdateReplicas(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        resource.HasAction(ContainerReplicasUpdateOperationId.ToString());

    public async Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (replicas < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replicas),
                replicas,
                "Replicas must be greater than or equal to 1.");
        }

        if (!CanUpdateReplicas(context.Resource))
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not support replica updates.");
        }

        await ApplyResourceDefinitionAttributesAsync(
            context,
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerReplicasAttributeId] = replicas
            },
            triggeredBy,
            cancellationToken);

        return ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
            $"Updated replicas for {context.Resource.Name} to '{replicas}'.",
            context.Resource.Id,
            "Container app replica deployment must be reconciled.");
    }

    public bool CanConfigureEnvironmentVariables(ResourceManagerResource resource) =>
        IsBridgeResource(resource) &&
        resource.HasCapability(ResourceCapabilityIds.EnvironmentVariables);

    public IReadOnlyList<EnvironmentVariableAssignment> GetConfiguredEnvironmentVariables(
        string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return [];
        }

        var resource = GetResources().FirstOrDefault(resource =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));
        if (resource is null || !CanConfigureEnvironmentVariables(resource))
        {
            return [];
        }

        return ReadEnvironmentVariables(resource.ResourceAttributes);
    }

    public async Task<ResourceProcedureResult> UpdateEnvironmentVariablesAsync(
        ResourceProcedureContext context,
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(environmentVariables);

        if (!CanConfigureEnvironmentVariables(context.Resource))
        {
            throw new NotSupportedException(
                $"Resource model graph resource '{context.Resource.Id}' does not support environment variables.");
        }

        await ApplyResourceDefinitionAttributesAsync(
            context,
            new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [EnvironmentVariablesAttributeId] = ResourceAttributeValue.Object(
                    ToEnvironmentVariableAttributeValue(environmentVariables))
            },
            context.TriggeredBy,
            cancellationToken);

        return ResourceProcedureResult.CompletedWithRestartRequired(
            $"Updated environment variables for {context.Resource.Name}.",
            context.Resource.Id,
            "Restart the resource before the environment variable changes take effect.");
    }

    public bool CanEvaluateAction(
        ResourceManagerResource resource,
        ResourceAction action) =>
        IsBridgeResource(resource) &&
        resource.HasAction(action.Id);

    public async Task<string?> GetActionUnavailableReasonAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingDiagnostics = GetBlockingActionDiagnostics(resolution.Diagnostics);

        if (blockingDiagnostics.Count > 0)
        {
            return FormatDiagnostics(blockingDiagnostics);
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            return $"Resource model operation '{resolution.OperationId}' does not support execution.";
        }

        return await executableOperation.CanExecuteAsync(cancellationToken)
            ? null
            : executableOperation.UnavailableReason ??
                $"Resource model operation '{resolution.OperationId}' cannot execute.";
    }

    public async Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(action);

        var resolution = await ResolveExecutableOperationAsync(
            context.Resource.Id,
            action,
            cancellationToken);
        var blockingDiagnostics = GetBlockingActionDiagnostics(resolution.Diagnostics);

        if (blockingDiagnostics.Count > 0)
        {
            throw new InvalidOperationException(FormatDiagnostics(blockingDiagnostics));
        }

        if (resolution.Operation is not IResourceOperationExecutorProjection executableOperation)
        {
            throw new NotSupportedException(
                $"Resource model operation '{resolution.OperationId}' does not support execution.");
        }

        if (!await executableOperation.CanExecuteAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Resource model operation '{resolution.OperationId}' cannot execute.");
        }

        var execution = await executableOperation.ExecuteAsync(cancellationToken);
        if (execution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(execution.Diagnostics));
        }

        return ResourceProcedureResult.Completed(
            CreateCompletedMessage(action, context.Resource, execution.Diagnostics));
    }

    private async Task ApplyResourceDefinitionAttributesAsync(
        ResourceProcedureContext context,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> attributes,
        string? triggeredBy,
        CancellationToken cancellationToken)
    {
        var current = await _resourceResolver.ResolveAsync(
            context.Resource.Id,
            _resolutionContext,
            cancellationToken);
        if (current.Target is null)
        {
            throw new InvalidOperationException(
                $"Resource model graph resource '{context.Resource.Id}' could not be resolved.");
        }

        if (current.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(current.Diagnostics));
        }

        var definition = new ResourceDefinition(
            current.Target.Name,
            current.Target.Type.TypeId,
            ResourceId: current.Target.EffectiveResourceId,
            Attributes: new ResourceAttributeValueMap(attributes));
        var apply = await _definitionApply.ApplyDefinitionsAsync(
            [definition],
            new ResourceGraphCommitContext(
                EnvironmentId: _resolutionContext.EnvironmentId,
                PrincipalId: string.IsNullOrWhiteSpace(triggeredBy)
                    ? _resolutionContext.PrincipalId
                    : triggeredBy),
            ResourceModelGraphDefinitionApplyOptions.Default.WithoutRuntimeReconciliation(),
            cancellationToken);
        if (apply.HasErrors || !apply.IsCommitted)
        {
            throw new InvalidOperationException(FormatDiagnostics(apply.Diagnostics));
        }
    }

    private static IReadOnlyList<EnvironmentVariableAssignment> ReadEnvironmentVariables(
        IReadOnlyDictionary<string, string> attributes)
    {
        var environmentVariables = ReadEnvironmentVariables(attributes, EnvironmentVariablesAttributeId.ToString());
        return environmentVariables.Count == 0
            ? []
            : environmentVariables
                .OrderBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private static IReadOnlyList<EnvironmentVariableAssignment> ReadEnvironmentVariables(
        IReadOnlyDictionary<string, string> attributes,
        string rootAttributeName)
    {
        var variables = new Dictionary<string, EnvironmentVariableParts>(StringComparer.OrdinalIgnoreCase);
        if (attributes.TryGetValue(rootAttributeName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            ReadEnvironmentVariableObject(value, variables);
        }

        ReadFlattenedEnvironmentVariables(attributes, rootAttributeName, variables);

        return variables
            .Select(variable => ToEnvironmentVariableAssignment(variable.Key, variable.Value))
            .Where(variable => !string.IsNullOrWhiteSpace(variable.Name))
            .ToArray();
    }

    private static void ReadEnvironmentVariableObject(
        string value,
        Dictionary<string, EnvironmentVariableParts> variables)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    variables[property.Name] = GetParts(variables, property.Name) with
                    {
                        Value = property.Value.GetString() ?? string.Empty
                    };
                    continue;
                }

                if (property.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var parts = GetParts(variables, property.Name);
                if (TryGetPropertyString(property.Value, "value", out var literalValue))
                {
                    parts = parts with { Value = literalValue };
                }

                if (TryGetObjectProperty(property.Value, "configurationSettingRef", out var configurationRef))
                {
                    parts = parts with
                    {
                        ConfigurationSetting = ReadConfigurationSettingReference(configurationRef)
                    };
                }

                if (TryGetObjectProperty(property.Value, "secretRef", out var secretRef))
                {
                    parts = parts with { Secret = ReadSecretReference(secretRef) };
                }

                variables[property.Name] = parts;
            }
        }
        catch (JsonException)
        {
        }
    }

    private static void ReadFlattenedEnvironmentVariables(
        IReadOnlyDictionary<string, string> attributes,
        string rootAttributeName,
        Dictionary<string, EnvironmentVariableParts> variables)
    {
        var prefix = rootAttributeName + ".";
        foreach (var (attributeName, value) in attributes)
        {
            if (!attributeName.StartsWith(prefix, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(value))
            {
                continue;
            }

            var path = attributeName[prefix.Length..];
            if (TryReadEnvironmentVariablePath(path, ".value", out var variableName, out _))
            {
                variables[variableName] = GetParts(variables, variableName) with { Value = value };
                continue;
            }

            if (TryReadEnvironmentVariablePath(
                    path,
                    ".configurationSettingRef.",
                    out variableName,
                    out var configurationProperty))
            {
                var parts = GetParts(variables, variableName);
                var reference = parts.ConfigurationSetting ?? new ConfigurationSettingReference(
                    string.Empty,
                    string.Empty);
                reference = configurationProperty switch
                {
                    "storeResourceId" => reference with { StoreResourceId = value },
                    "name" => reference with { SettingName = value },
                    "settingName" => reference with { SettingName = value },
                    "version" => reference with { Version = value },
                    _ => reference
                };
                variables[variableName] = parts with { ConfigurationSetting = reference };
                continue;
            }

            if (TryReadEnvironmentVariablePath(
                    path,
                    ".secretRef.",
                    out variableName,
                    out var secretProperty))
            {
                var parts = GetParts(variables, variableName);
                var reference = parts.Secret ?? new SecretReference(
                    string.Empty,
                    string.Empty);
                reference = secretProperty switch
                {
                    "vaultResourceId" => reference with { VaultResourceId = value },
                    "name" => reference with { SecretName = value },
                    "secretName" => reference with { SecretName = value },
                    "version" => reference with { Version = value },
                    _ => reference
                };
                variables[variableName] = parts with { Secret = reference };
            }
        }
    }

    private static IReadOnlyDictionary<string, ResourceAttributeValue> ToEnvironmentVariableAttributeValue(
        IReadOnlyList<EnvironmentVariableAssignment> environmentVariables)
    {
        var variables = new Dictionary<string, ResourceAttributeValue>(StringComparer.OrdinalIgnoreCase);
        foreach (var environmentVariable in environmentVariables)
        {
            var name = environmentVariable.Name.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            variables[name] = ResourceAttributeValue.Object(
                ToEnvironmentVariablePropertyValue(environmentVariable));
        }

        return variables;
    }

    private static IReadOnlyDictionary<string, ResourceAttributeValue> ToEnvironmentVariablePropertyValue(
        EnvironmentVariableAssignment environmentVariable)
    {
        if (environmentVariable.ConfigurationSetting is not null)
        {
            return new Dictionary<string, ResourceAttributeValue>
            {
                ["configurationSettingRef"] = ResourceAttributeValue.Object(
                    ToConfigurationSettingReferenceValue(environmentVariable.ConfigurationSetting))
            };
        }

        if (environmentVariable.Secret is not null)
        {
            return new Dictionary<string, ResourceAttributeValue>
            {
                ["secretRef"] = ResourceAttributeValue.Object(
                    ToSecretReferenceValue(environmentVariable.Secret))
            };
        }

        return new Dictionary<string, ResourceAttributeValue>
        {
            ["value"] = environmentVariable.Value ?? string.Empty
        };
    }

    private static IReadOnlyDictionary<string, ResourceAttributeValue> ToConfigurationSettingReferenceValue(
        ConfigurationSettingReference reference)
    {
        var values = new Dictionary<string, ResourceAttributeValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["storeResourceId"] = reference.StoreResourceId,
            ["name"] = reference.SettingName
        };
        if (!string.IsNullOrWhiteSpace(reference.Version))
        {
            values["version"] = reference.Version;
        }

        return values;
    }

    private static IReadOnlyDictionary<string, ResourceAttributeValue> ToSecretReferenceValue(
        SecretReference reference)
    {
        var values = new Dictionary<string, ResourceAttributeValue>(StringComparer.OrdinalIgnoreCase)
        {
            ["vaultResourceId"] = reference.VaultResourceId,
            ["name"] = reference.SecretName
        };
        if (!string.IsNullOrWhiteSpace(reference.Version))
        {
            values["version"] = reference.Version;
        }

        return values;
    }

    private static EnvironmentVariableAssignment ToEnvironmentVariableAssignment(
        string name,
        EnvironmentVariableParts parts)
    {
        if (parts.ConfigurationSetting is not null)
        {
            return EnvironmentVariableAssignment.FromConfiguration(
                name,
                parts.ConfigurationSetting);
        }

        if (parts.Secret is not null)
        {
            return EnvironmentVariableAssignment.FromSecret(name, parts.Secret);
        }

        return new EnvironmentVariableAssignment(name, parts.Value ?? string.Empty);
    }

    private static ConfigurationSettingReference? ReadConfigurationSettingReference(JsonElement element)
    {
        var storeResourceId = GetPropertyString(element, "storeResourceId");
        var name = GetPropertyString(element, "name") ?? GetPropertyString(element, "settingName");
        if (string.IsNullOrWhiteSpace(storeResourceId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new ConfigurationSettingReference(
            storeResourceId,
            name,
            GetPropertyString(element, "version"));
    }

    private static SecretReference? ReadSecretReference(JsonElement element)
    {
        var vaultResourceId = GetPropertyString(element, "vaultResourceId");
        var name = GetPropertyString(element, "name") ?? GetPropertyString(element, "secretName");
        if (string.IsNullOrWhiteSpace(vaultResourceId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return new SecretReference(
            vaultResourceId,
            name,
            GetPropertyString(element, "version"));
    }

    private static bool TryReadEnvironmentVariablePath(
        string path,
        string marker,
        out string variableName,
        out string propertyName)
    {
        var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            variableName = string.Empty;
            propertyName = string.Empty;
            return false;
        }

        variableName = path[..markerIndex];
        propertyName = path[(markerIndex + marker.Length)..];
        return !string.IsNullOrWhiteSpace(variableName) &&
            (string.Equals(marker, ".value", StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(propertyName));
    }

    private static EnvironmentVariableParts GetParts(
        Dictionary<string, EnvironmentVariableParts> variables,
        string name) =>
        variables.GetValueOrDefault(name) ?? new EnvironmentVariableParts();

    private static bool TryGetObjectProperty(
        JsonElement element,
        string propertyName,
        out JsonElement property)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out property) &&
            property.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetPropertyString(
        JsonElement element,
        string propertyName,
        out string? value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        value = null;
        return false;
    }

    private static string? GetPropertyString(JsonElement element, string propertyName) =>
        TryGetPropertyString(element, propertyName, out var value) ? value : null;

    private async ValueTask<ResourceModelGraphOperationResolution> ResolveExecutableOperationAsync(
        string resourceId,
        ResourceAction action,
        CancellationToken cancellationToken) =>
        await _resourceResolver.ResolveOperationAsync(
            resourceId,
            action,
            _resolutionContext,
            cancellationToken);

    private async ValueTask<ResourceModelGraphOperationResolution> ResolveExecutableOperationAsync(
        string resourceId,
        ResourceOperationId operationId,
        CancellationToken cancellationToken) =>
        await _resourceResolver.ResolveOperationAsync(
            resourceId,
            operationId,
            _resolutionContext,
            cancellationToken);

    private static IReadOnlyList<ResourceDefinitionDiagnostic> GetBlockingActionDiagnostics(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        diagnostics
            .Where(diagnostic =>
                diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error &&
                diagnostic.Code != ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing)
            .ToArray();

    private async ValueTask<ResourceModelGraphServiceExecutorResolution> CreateServiceProcedureContextAsync(
        ResourceOrchestratorServiceProcedureContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var graphResource = await ResolveGraphResourceOrThrowAsync(
            context.ResourceContext.Resource.Id,
            cancellationToken);
        var executor = ResolveServiceExecutor(context.ResourceContext.Resource, action);
        return new(
            executor,
            new ResourceModelGraphOrchestratorServiceProcedureContext(
                context.ResourceContext.Resource,
                graphResource,
                context.ResourceContext,
                context.Service,
                context.ReplicaGroup,
                context.ServiceRoutingBindings));
    }

    private async ValueTask<ResourceModelGraphServiceInstanceExecutorResolution> CreateServiceInstanceContextAsync(
        ResourceOrchestratorServiceInstanceContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var graphResource = await ResolveGraphResourceOrThrowAsync(
            context.ResourceContext.Resource.Id,
            cancellationToken);
        var executor = ResolveServiceExecutor(context.ResourceContext.Resource, action);
        return new(
            executor,
            new ResourceModelGraphOrchestratorServiceInstanceContext(
                context.ResourceContext.Resource,
                graphResource,
                context.ResourceContext,
                context.Service,
                context.Instance,
                context.ReplicaGroup));
    }

    private async ValueTask<Resource> ResolveGraphResourceOrThrowAsync(
        string resourceId,
        CancellationToken cancellationToken)
    {
        var resolution = await _resourceResolver.ResolveAsync(
            resourceId,
            _resolutionContext,
            cancellationToken);
        if (resolution.Target is null)
        {
            throw new InvalidOperationException(
                $"Resource model graph resource '{resourceId}' could not be resolved.");
        }

        if (resolution.HasErrors)
        {
            throw new InvalidOperationException(FormatDiagnostics(resolution.Diagnostics));
        }

        return resolution.Target;
    }

    private IResourceModelGraphOrchestratorServiceExecutor ResolveServiceExecutor(
        ResourceManagerResource resource,
        ResourceAction action) =>
        _serviceExecutors.FirstOrDefault(executor =>
            executor.CanExecuteOrchestratorService(resource, action)) ??
        throw new NotSupportedException(
            $"Resource model graph resource '{resource.Id}' does not support orchestrator service action '{action.Id}'.");

    private static string FormatDiagnostics(
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(
            " ",
            diagnostics.Select(diagnostic =>
                string.IsNullOrWhiteSpace(diagnostic.Target)
                    ? diagnostic.Message
                    : $"{diagnostic.Message} Target: {diagnostic.Target}."));

    private static string CreateCompletedMessage(
        ResourceAction action,
        ResourceManagerResource resource,
        IReadOnlyList<ResourceDefinitionDiagnostic> diagnostics)
    {
        var message = $"Executed {action.DisplayName} for {resource.Name}.";
        var diagnosticMessages = diagnostics
            .Where(diagnostic =>
                (diagnostic.Severity is
                    ResourceDefinitionDiagnosticSeverity.Information or
                    ResourceDefinitionDiagnosticSeverity.Warning) &&
                !string.IsNullOrWhiteSpace(diagnostic.Message))
            .Select(diagnostic => diagnostic.Message.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return diagnosticMessages.Length == 0
            ? message
            : $"{message} {string.Join(" ", diagnosticMessages)}";
    }

    private bool IsBridgeResource(ResourceManagerResource resource) =>
        resource.IsDeclaredResource &&
        resource.ResourceAttributes.TryGetValue(
            ResourceModelResourceManagerAttributeNames.BridgeProviderId,
            out var bridgeProviderId) &&
        string.Equals(bridgeProviderId, Id, StringComparison.OrdinalIgnoreCase);
}

internal sealed record ResourceModelGraphServiceExecutorResolution(
    IResourceModelGraphOrchestratorServiceExecutor Executor,
    ResourceModelGraphOrchestratorServiceProcedureContext Context);

internal sealed record ResourceModelGraphServiceInstanceExecutorResolution(
    IResourceModelGraphOrchestratorServiceExecutor Executor,
    ResourceModelGraphOrchestratorServiceInstanceContext Context);

internal sealed record EnvironmentVariableParts(
    string? Value = null,
    ConfigurationSettingReference? ConfigurationSetting = null,
    SecretReference? Secret = null);
