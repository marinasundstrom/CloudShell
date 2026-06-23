using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private ResourceObservability GetEffectiveObservability(ApplicationResourceDefinition definition) =>
        definition.Observability ??
        (options.EnableObservabilityByDefault
            ? ResourceObservability.Default
            : ResourceObservability.None);

    private ResourceObservability CreateResourceObservability(ApplicationResourceDefinition definition)
    {
        var observability = GetEffectiveObservability(definition);
        if (!ApplicationResourceTypes.IsContainerApp(definition.ResourceType) ||
            !IsReplicaModeEnabled(definition) ||
            !observability.HasAnySignal)
        {
            return observability;
        }

        var deployment = CreateDefaultContainerOrchestratorDeployment(
            definition,
            GetState(definition.Id),
            runtimeRevisionScoped: true);
        var scopes = CreateDefaultContainerServiceInstances(deployment.Spec.Service)
            .Select(instance =>
            {
                var scopeAttributes = CreateRuntimeContainerTelemetryAttributes(
                    definition,
                    instance,
                    deployment.RevisionId);
                return new TelemetryScopeDescriptor(
                    CreateRuntimeContainerResourceId(definition.Id, instance.ReplicaOrdinal),
                    $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}",
                    "runtime",
                    $"Runtime container replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)} of {instance.ReplicaCount.ToString(CultureInfo.InvariantCulture)}.",
                    deployment.RevisionId,
                    scopeAttributes);
            })
            .ToArray();

        return observability with
        {
            Scopes = observability.TelemetryScopes
                .Concat(scopes)
                .GroupBy(scope => scope.ScopeResourceId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray()
        };
    }

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

    private static IReadOnlyDictionary<string, string> CreateRuntimeContainerTelemetryAttributes(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorServiceInstance instance,
        string revision)
    {
        var resourceId = CreateRuntimeContainerResourceId(definition.Id, instance.ReplicaOrdinal);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TelemetryAttributeNames.ScopeResourceId] = resourceId,
            [TelemetryAttributeNames.ScopeName] = $"Replica {instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture)}",
            [TelemetryAttributeNames.ScopeKind] = "runtime",
            [TelemetryAttributeNames.RuntimeReplicaOrdinal] = instance.ReplicaOrdinal.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeReplicaCount] = instance.ReplicaCount.ToString(CultureInfo.InvariantCulture),
            [TelemetryAttributeNames.RuntimeContainerName] = instance.Name,
            [TelemetryAttributeNames.DeploymentRevision] = revision
        };
    }

    private IReadOnlyList<EnvironmentVariableAssignment> ApplyRuntimeContainerTelemetryScopeEnvironmentVariables(
        ApplicationResourceDefinition definition,
        ResourceOrchestratorServiceInstance instance,
        IReadOnlyList<EnvironmentVariableAssignment> variables)
    {
        var observability = GetEffectiveObservability(definition);
        if (!observability.HasAnySignal ||
            !ApplicationResourceTypes.IsContainerApp(definition.ResourceType) ||
            !IsReplicaModeEnabled(definition))
        {
            return variables;
        }

        var scopeAttributes = CreateRuntimeContainerTelemetryAttributes(
            definition,
            instance,
            GetEffectiveContainerRevision(definition));
        var encodedScopeAttributes = string.Join(
            ',',
            scopeAttributes.Select(attribute => $"{attribute.Key}={EscapeOtelAttributeValue(attribute.Value)}"));
        var merged = variables.ToList();
        var existingIndex = merged.FindIndex(variable =>
            string.Equals(variable.Name, "OTEL_RESOURCE_ATTRIBUTES", StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            merged[existingIndex] = merged[existingIndex] with
            {
                Value = string.IsNullOrWhiteSpace(merged[existingIndex].Value)
                    ? encodedScopeAttributes
                    : $"{merged[existingIndex].Value},{encodedScopeAttributes}"
            };
        }
        else
        {
            merged.Add(new EnvironmentVariableAssignment("OTEL_RESOURCE_ATTRIBUTES", encodedScopeAttributes));
        }

        return merged;
    }

    private static string EscapeOtelAttributeValue(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("=", "\\=", StringComparison.Ordinal);
}
