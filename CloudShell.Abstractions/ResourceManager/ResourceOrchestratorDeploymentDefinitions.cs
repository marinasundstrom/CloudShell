using System.Globalization;
using System.Text.Json;

namespace CloudShell.Abstractions.ResourceManager;

public static class ResourceOrchestratorDeploymentDefinitionTypes
{
    public const string Service = "cloudshell.orchestrator.service";

    public const string ReplicaGroup = "cloudshell.replica-group";

    public const string ServiceRoutingBinding = "cloudshell.service-routing-binding";

    public const string Replica = "cloudshell.replica";
}

public sealed record ResourceOrchestratorDeploymentDefinition(
    string DefinitionVersion,
    IReadOnlyList<ResourceOrchestratorServiceDefinition>? Services = null,
    IReadOnlyList<ResourceOrchestratorResourceDefinition>? Resources = null)
{
    public const string CurrentDefinitionVersion = "1";

    public IReadOnlyList<ResourceOrchestratorServiceDefinition> DeploymentServices => Services ?? [];

    public IReadOnlyList<ResourceOrchestratorResourceDefinition> DeploymentResources => Resources ?? [];

    public static ResourceOrchestratorDeploymentDefinition FromService(
        ResourceOrchestratorService service,
        string workloadVersion,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadVersion);

        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        return new ResourceOrchestratorDeploymentDefinition(
            CurrentDefinitionVersion,
            Services:
            [
                new ResourceOrchestratorServiceDefinition(
                    service.Name,
                    ResourceOrchestratorDeploymentDefinitionTypes.Service,
                    CurrentDefinitionVersion,
                    Attributes: attributes,
                    Resources:
                    [
                        ResourceOrchestratorReplicaGroupDefinition
                            .FromReplicaGroup(replicaGroup, workloadVersion)
                            .ToResourceDefinition()
                    ])
            ]);
    }
}

public enum ResourceOrchestratorScaleOutRoutingMode
{
    AfterAddedReplicas,
    BeforeAddedReplicas
}

public enum ResourceOrchestratorScaleInRoutingMode
{
    BeforeRemovedReplicas,
    AfterRemovedReplicas
}

public enum ResourceOrchestratorReplacementRoutingMode
{
    AfterNewReplicaGroupMaterialized,
    BeforeNewReplicaGroupMaterialized
}

public sealed record ResourceOrchestratorReplicaGroupReconciliationPolicy(
    ResourceOrchestratorScaleOutRoutingMode ScaleOutRoutingMode =
        ResourceOrchestratorScaleOutRoutingMode.AfterAddedReplicas,
    ResourceOrchestratorScaleInRoutingMode ScaleInRoutingMode =
        ResourceOrchestratorScaleInRoutingMode.BeforeRemovedReplicas,
    ResourceOrchestratorReplacementRoutingMode ReplacementRoutingMode =
        ResourceOrchestratorReplacementRoutingMode.AfterNewReplicaGroupMaterialized,
    int RetainPreviousReplicaSlots = 0)
{
    public static ResourceOrchestratorReplicaGroupReconciliationPolicy Default { get; } = new();
}

public sealed record ResourceOrchestratorReplicaGroupDefinition(
    string Name,
    string DefinitionVersion,
    string ServiceName,
    string? RuntimeRevisionId,
    int RequestedReplicaSlots,
    int RequestedReplicas,
    ResourceOrchestratorReplicaManagementPolicy? ManagementPolicy = null,
    ResourceOrchestratorReplicaGroupReconciliationPolicy? ReconciliationPolicy = null,
    ResourceOrchestratorResourceDefinition? Template = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ReplicaGroupAttributes => Attributes ?? EmptyAttributes;

    public static ResourceOrchestratorReplicaGroupDefinition FromReplicaGroup(
        ResourceOrchestratorReplicaGroup replicaGroup,
        string workloadVersion,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        ArgumentNullException.ThrowIfNull(replicaGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(workloadVersion);

        var mergedAttributes = new Dictionary<string, string>(
            attributes ?? EmptyAttributes,
            StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentWorkloadVersion] = workloadVersion
        };

        return new(
            replicaGroup.Id,
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            replicaGroup.ServiceId,
            replicaGroup.RuntimeRevisionId,
            replicaGroup.RequestedReplicaSlots,
            replicaGroup.RequestedReplicas,
            replicaGroup.EffectiveManagementPolicy,
            ResourceOrchestratorReplicaGroupReconciliationPolicy.Default,
            CreateReplicaTemplate(replicaGroup, workloadVersion, mergedAttributes),
            mergedAttributes);
    }

    public ResourceOrchestratorResourceDefinition ToResourceDefinition()
    {
        var policy = ManagementPolicy ?? ResourceOrchestratorReplicaManagementPolicy.Default;
        var reconciliation = ReconciliationPolicy ?? ResourceOrchestratorReplicaGroupReconciliationPolicy.Default;
        var attributes = new Dictionary<string, string>(
            ReplicaGroupAttributes,
            StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.RuntimeRevision] =
                RuntimeRevisionId ?? string.Empty,
            [ResourceAttributeNames.DeploymentRequestedReplicaSlots] =
                RequestedReplicaSlots.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.DeploymentRequestedReplicas] =
                RequestedReplicas.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.DeploymentRoutingScaleOutMode] =
                reconciliation.ScaleOutRoutingMode.ToString(),
            [ResourceAttributeNames.DeploymentRoutingScaleInMode] =
                reconciliation.ScaleInRoutingMode.ToString(),
            [ResourceAttributeNames.DeploymentRoutingReplacementMode] =
                reconciliation.ReplacementRoutingMode.ToString(),
            [ResourceAttributeNames.DeploymentReplacementRetainPreviousReplicaSlots] =
                Math.Max(0, reconciliation.RetainPreviousReplicaSlots).ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.DeploymentReplicaRestartMode] =
                policy.RestartMode.ToString(),
            [ResourceAttributeNames.DeploymentReplicaFailureThreshold] =
                policy.FailureThreshold.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.DeploymentReplicaMaxAttempts] =
                policy.MaxAttempts.ToString(CultureInfo.InvariantCulture)
        };

        return new(
            Name,
            ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
            DefinitionVersion,
            Definition: Template is null
                ? null
                : JsonSerializer.SerializeToElement(Template, SerializerOptions),
            Attributes: attributes);
    }

    public ResourceOrchestratorReplicaGroup ToReplicaGroup(
        ResourceOrchestratorService service)
    {
        ArgumentNullException.ThrowIfNull(service);

        var requestedSlots = Math.Max(1, RequestedReplicaSlots);
        var targetService = service with
        {
            RuntimeRevisionId = RuntimeRevisionId,
            ReplicaManagementPolicy = ManagementPolicy,
            Workload = service.Workload with
            {
                Replicas = requestedSlots,
                ReplicasEnabled = requestedSlots > 1 || service.Workload.ReplicasEnabled
            }
        };
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(targetService);
        return replicaGroup with { Id = Name };
    }

    public static bool TryFromResourceDefinition(
        ResourceOrchestratorServiceDefinition service,
        ResourceOrchestratorResourceDefinition resource,
        out ResourceOrchestratorReplicaGroupDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(resource);

        definition = null!;
        if (!string.Equals(
                resource.Type,
                ResourceOrchestratorDeploymentDefinitionTypes.ReplicaGroup,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var attributes = resource.ResourceAttributes;
        definition = new(
            resource.Name,
            resource.DefinitionVersion,
            service.Name,
            RuntimeRevisionId: attributes.TryGetValue(ResourceAttributeNames.RuntimeRevision, out var revision) &&
                !string.IsNullOrWhiteSpace(revision)
                    ? revision.Trim()
                    : null,
            RequestedReplicaSlots: GetInt(
                attributes,
                ResourceAttributeNames.DeploymentRequestedReplicaSlots,
                fallback: 1),
            RequestedReplicas: GetInt(
                attributes,
                ResourceAttributeNames.DeploymentRequestedReplicas,
                fallback: 1),
            ManagementPolicy: new ResourceOrchestratorReplicaManagementPolicy(
                RestartMode: GetRestartMode(attributes),
                FailureThreshold: GetInt(
                    attributes,
                    ResourceAttributeNames.DeploymentReplicaFailureThreshold,
                    ResourceOrchestratorReplicaManagementPolicy.Default.FailureThreshold),
                MaxAttempts: GetInt(
                    attributes,
                    ResourceAttributeNames.DeploymentReplicaMaxAttempts,
                    ResourceOrchestratorReplicaManagementPolicy.Default.MaxAttempts)),
            ReconciliationPolicy: new ResourceOrchestratorReplicaGroupReconciliationPolicy(
                ScaleOutRoutingMode: GetEnum(
                    attributes,
                    ResourceAttributeNames.DeploymentRoutingScaleOutMode,
                    ResourceOrchestratorReplicaGroupReconciliationPolicy.Default.ScaleOutRoutingMode),
                ScaleInRoutingMode: GetEnum(
                    attributes,
                    ResourceAttributeNames.DeploymentRoutingScaleInMode,
                    ResourceOrchestratorReplicaGroupReconciliationPolicy.Default.ScaleInRoutingMode),
                ReplacementRoutingMode: GetEnum(
                    attributes,
                    ResourceAttributeNames.DeploymentRoutingReplacementMode,
                    ResourceOrchestratorReplicaGroupReconciliationPolicy.Default.ReplacementRoutingMode),
                RetainPreviousReplicaSlots: GetNonNegativeInt(
                    attributes,
                    ResourceAttributeNames.DeploymentReplacementRetainPreviousReplicaSlots,
                    ResourceOrchestratorReplicaGroupReconciliationPolicy.Default.RetainPreviousReplicaSlots)),
            Template: TryReadTemplate(resource.Definition),
            Attributes: attributes);
        return true;
    }

    private static ResourceOrchestratorResourceDefinition CreateReplicaTemplate(
        ResourceOrchestratorReplicaGroup replicaGroup,
        string workloadVersion,
        IReadOnlyDictionary<string, string> attributes)
    {
        var templateAttributes = new Dictionary<string, string>(
            attributes,
            StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentReplicaGroupId] = replicaGroup.Id,
            [ResourceAttributeNames.DeploymentWorkloadVersion] = workloadVersion,
            [ResourceAttributeNames.RuntimeRevision] = replicaGroup.RuntimeRevisionId ?? string.Empty
        };

        return new(
            $"{replicaGroup.Id}-replica-template",
            ResourceOrchestratorDeploymentDefinitionTypes.Replica,
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            Attributes: templateAttributes);
    }

    private static ResourceOrchestratorResourceDefinition? TryReadTemplate(JsonElement? definition)
    {
        if (definition is null || definition.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        try
        {
            return definition.Value.Deserialize<ResourceOrchestratorResourceDefinition>(SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int GetInt(
        IReadOnlyDictionary<string, string> attributes,
        string name,
        int fallback) =>
        attributes.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(1, parsed)
            : fallback;

    private static int GetNonNegativeInt(
        IReadOnlyDictionary<string, string> attributes,
        string name,
        int fallback) =>
        attributes.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? Math.Max(0, parsed)
            : fallback;

    private static ResourceOrchestratorReplicaRestartMode GetRestartMode(
        IReadOnlyDictionary<string, string> attributes) =>
        GetEnum(
            attributes,
            ResourceAttributeNames.DeploymentReplicaRestartMode,
            ResourceOrchestratorReplicaManagementPolicy.Default.RestartMode);

    private static TValue GetEnum<TValue>(
        IReadOnlyDictionary<string, string> attributes,
        string name,
        TValue fallback)
        where TValue : struct =>
        attributes.TryGetValue(name, out var value) &&
        Enum.TryParse<TValue>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;
}

public sealed record ResourceOrchestratorServiceRoutingBindingDefinition(
    string Name,
    string DefinitionVersion,
    string ServiceName,
    string ReplicaGroupName,
    ResourceEndpointReference SourceEndpoint,
    string? LoadBalancerResourceId = null,
    string? RouteId = null,
    string? EndpointMappingId = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> RoutingBindingAttributes => Attributes ?? EmptyAttributes;

    public ResourceOrchestratorResourceDefinition ToResourceDefinition()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(DefinitionVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceName);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReplicaGroupName);
        ArgumentNullException.ThrowIfNull(SourceEndpoint);

        var attributes = new Dictionary<string, string>(
            RoutingBindingAttributes,
            StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentServiceId] = ServiceName,
            [ResourceAttributeNames.DeploymentReplicaGroupId] = ReplicaGroupName,
            [ResourceAttributeNames.DeploymentRoutingSourceResourceId] = SourceEndpoint.ResourceId,
            [ResourceAttributeNames.DeploymentRoutingEndpointName] = SourceEndpoint.EndpointName
        };

        AddOptionalAttribute(
            attributes,
            ResourceAttributeNames.DeploymentRoutingLoadBalancerResourceId,
            LoadBalancerResourceId);
        AddOptionalAttribute(
            attributes,
            ResourceAttributeNames.DeploymentRoutingRouteId,
            RouteId);
        AddOptionalAttribute(
            attributes,
            ResourceAttributeNames.DeploymentRoutingEndpointMappingId,
            EndpointMappingId);

        return new(
            Name,
            ResourceOrchestratorDeploymentDefinitionTypes.ServiceRoutingBinding,
            DefinitionVersion,
            Attributes: attributes);
    }

    public static bool TryFromResourceDefinition(
        ResourceOrchestratorServiceDefinition service,
        ResourceOrchestratorResourceDefinition resource,
        out ResourceOrchestratorServiceRoutingBindingDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(resource);

        definition = null!;
        if (!string.Equals(
                resource.Type,
                ResourceOrchestratorDeploymentDefinitionTypes.ServiceRoutingBinding,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var attributes = resource.ResourceAttributes;
        if (!TryGetRequiredAttribute(
                attributes,
                ResourceAttributeNames.DeploymentReplicaGroupId,
                out var replicaGroupName) ||
            !TryGetRequiredAttribute(
                attributes,
                ResourceAttributeNames.DeploymentRoutingSourceResourceId,
                out var sourceResourceId) ||
            !TryGetRequiredAttribute(
                attributes,
                ResourceAttributeNames.DeploymentRoutingEndpointName,
                out var endpointName))
        {
            return false;
        }

        var serviceName = TryGetOptionalAttribute(
            attributes,
            ResourceAttributeNames.DeploymentServiceId) ?? service.Name;

        definition = new(
            resource.Name,
            resource.DefinitionVersion,
            serviceName,
            replicaGroupName,
            ResourceEndpointReference.ForEndpoint(sourceResourceId, endpointName),
            LoadBalancerResourceId: TryGetOptionalAttribute(
                attributes,
                ResourceAttributeNames.DeploymentRoutingLoadBalancerResourceId),
            RouteId: TryGetOptionalAttribute(
                attributes,
                ResourceAttributeNames.DeploymentRoutingRouteId),
            EndpointMappingId: TryGetOptionalAttribute(
                attributes,
                ResourceAttributeNames.DeploymentRoutingEndpointMappingId),
            Attributes: attributes);
        return true;
    }

    private static void AddOptionalAttribute(
        IDictionary<string, string> attributes,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            attributes[name] = value.Trim();
        }
    }

    private static bool TryGetRequiredAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string name,
        out string value)
    {
        value = string.Empty;
        if (!attributes.TryGetValue(name, out var candidate) ||
            string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        value = candidate.Trim();
        return true;
    }

    private static string? TryGetOptionalAttribute(
        IReadOnlyDictionary<string, string> attributes,
        string name) =>
        attributes.TryGetValue(name, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
}

public sealed record ResourceOrchestratorServiceDefinition(
    string Name,
    string Type,
    string DefinitionVersion,
    JsonElement? Definition = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ResourceOrchestratorResourceDefinition>? Resources = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ServiceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyList<ResourceOrchestratorResourceDefinition> ServiceResources => Resources ?? [];

    public IReadOnlyList<ResourceOrchestratorReplicaGroupDefinition> ReplicaGroupDefinitions =>
        ServiceResources
            .Where(resource => ResourceOrchestratorReplicaGroupDefinition.TryFromResourceDefinition(
                this,
                resource,
                out _))
            .Select(resource =>
            {
                ResourceOrchestratorReplicaGroupDefinition.TryFromResourceDefinition(
                    this,
                    resource,
                    out var definition);
                return definition;
            })
            .ToArray();

    public IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition> RoutingBindingDefinitions =>
        ServiceResources
            .Where(resource => ResourceOrchestratorServiceRoutingBindingDefinition.TryFromResourceDefinition(
                this,
                resource,
                out _))
            .Select(resource =>
            {
                ResourceOrchestratorServiceRoutingBindingDefinition.TryFromResourceDefinition(
                    this,
                    resource,
                    out var definition);
                return definition;
            })
            .ToArray();
}

public sealed record ResourceOrchestratorResourceDefinition(
    string Name,
    string Type,
    string DefinitionVersion,
    JsonElement? Definition = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;
}
