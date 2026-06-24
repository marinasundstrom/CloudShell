namespace CloudShell.ResourceDefinitions;

public sealed class ResourceDefinitionResolver
{
    private readonly IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> _classDefinitions;
    private readonly IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> _typeDefinitions;
    private readonly IReadOnlyList<IResourceAttributeValidator> _attributeValidators;

    public ResourceDefinitionResolver(
        IEnumerable<ResourceClassDefinition> classDefinitions,
        IEnumerable<ResourceTypeDefinition> typeDefinitions,
        IEnumerable<IResourceAttributeValidator>? attributeValidators = null)
    {
        ArgumentNullException.ThrowIfNull(classDefinitions);
        ArgumentNullException.ThrowIfNull(typeDefinitions);

        _classDefinitions = classDefinitions.ToDictionary(
            definition => definition.ClassId);
        _typeDefinitions = typeDefinitions.ToDictionary(
            definition => definition.TypeId);
        _attributeValidators = attributeValidators?.ToArray() ?? [];
    }

    public ResolvedResourceDefinition Resolve(
        ResourceDefinition definition,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var typeDefinition = ResolveTypeDefinition(definition, diagnostics);
        var classDefinition = ResolveClassDefinition(typeDefinition, diagnostics);
        var attributes = ResolveAttributes(classDefinition, typeDefinition, definition);
        var capabilities = ResolveCapabilities(classDefinition, typeDefinition, definition);
        var operations = ResolveOperations(classDefinition, typeDefinition, definition, diagnostics);

        ValidateRequiredAttributes(classDefinition, typeDefinition, attributes, diagnostics);
        ValidateRequiredCapabilities(capabilities, diagnostics);
        ValidateAttributes(
            definition,
            classDefinition,
            typeDefinition,
            attributes,
            context ?? ResourceDefinitionResolutionContext.Empty,
            diagnostics);

        return new(
            definition,
            classDefinition,
            typeDefinition,
            attributes,
            capabilities,
            operations,
            diagnostics);
    }

    private ResourceTypeDefinition ResolveTypeDefinition(
        ResourceDefinition definition,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (_typeDefinitions.TryGetValue(definition.TypeId, out var typeDefinition))
        {
            return typeDefinition;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.UnknownResourceType,
            $"Resource type '{definition.TypeId}' is not registered.",
            definition.TypeId));

        return new(definition.TypeId, ResourceDefinitionClassIds.Generic);
    }

    private ResourceClassDefinition ResolveClassDefinition(
        ResourceTypeDefinition typeDefinition,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (_classDefinitions.TryGetValue(typeDefinition.ClassId, out var classDefinition))
        {
            return classDefinition;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.UnknownResourceClass,
            $"Resource class '{typeDefinition.ClassId}' is not registered.",
            typeDefinition.ClassId));

        return new(typeDefinition.ClassId);
    }

    private static ResourceAttributeSet ResolveAttributes(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceDefinition definition)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeResolution>();

        MergeAttributes(attributes, classDefinition.Attributes, ResourceDefinitionValueSource.ClassDefinition);
        MergeAttributes(attributes, typeDefinition.Attributes, ResourceDefinitionValueSource.TypeDefinition);
        MergeAttributes(attributes, definition.ResourceAttributes, ResourceDefinitionValueSource.ResourceDefinition);

        return new(attributes.Values);
    }

    private static void MergeAttributes(
        Dictionary<ResourceAttributeId, ResourceAttributeResolution> target,
        IReadOnlyDictionary<ResourceAttributeId, string>? attributes,
        ResourceDefinitionValueSource source)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var (name, value) in attributes)
        {
            target[name] = new(name, value, source);
        }
    }

    private static ResourceCapabilitySet ResolveCapabilities(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceDefinition definition)
    {
        var capabilities = new Dictionary<ResourceCapabilityId, ResourceCapabilityResolution>();

        MergeCapabilities(capabilities, classDefinition.Capabilities, ResourceDefinitionValueSource.ClassDefinition);
        MergeCapabilities(capabilities, typeDefinition.Capabilities, ResourceDefinitionValueSource.TypeDefinition);
        foreach (var (id, payload) in definition.CapabilityPayloads)
        {
            capabilities[id] = new(
                id,
                ResourceDefinitionJson.Clone(payload),
                ResourceDefinitionValueSource.ResourceDefinition);
        }

        return new(capabilities.Values);
    }

    private static void MergeCapabilities(
        Dictionary<ResourceCapabilityId, ResourceCapabilityResolution> target,
        IReadOnlyList<ResourceCapabilityDeclaration>? capabilities,
        ResourceDefinitionValueSource source)
    {
        if (capabilities is null)
        {
            return;
        }

        foreach (var capability in capabilities)
        {
            target[capability.Id] = new(
                capability.Id,
                capability.Payload?.Clone() ?? ResourceDefinitionJson.EmptyObject,
                source,
                capability.IsRequired);
        }
    }

    private static ResourceOperationSet ResolveOperations(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceDefinition definition,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var operations = new Dictionary<ResourceOperationId, ResourceOperationResolution>();

        MergeOperations(operations, classDefinition.Operations, ResourceDefinitionValueSource.ClassDefinition, diagnostics);
        MergeOperations(operations, typeDefinition.Operations, ResourceDefinitionValueSource.TypeDefinition, diagnostics);

        foreach (var (id, payload) in definition.OperationPayloads)
        {
            if (operations.TryGetValue(id, out var inherited) && !inherited.AllowOverride)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed,
                    $"Operation '{id}' cannot be overridden by resource definition '{definition.Name}'.",
                    id));
                continue;
            }

            operations[id] = new(
                id,
                ResourceDefinitionJson.Clone(payload),
                ResourceDefinitionValueSource.ResourceDefinition,
                IsEnabled: true,
                AllowOverride: true);
        }

        return new(operations.Values);
    }

    private static void MergeOperations(
        Dictionary<ResourceOperationId, ResourceOperationResolution> target,
        IReadOnlyList<ResourceOperationDeclaration>? operations,
        ResourceDefinitionValueSource source,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (operations is null)
        {
            return;
        }

        foreach (var operation in operations)
        {
            if (target.TryGetValue(operation.Id, out var inherited) && !inherited.AllowOverride)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed,
                    $"Operation '{operation.Id}' cannot be overridden at the {source} level.",
                    operation.Id));
                continue;
            }

            target[operation.Id] = new(
                operation.Id,
                operation.Payload?.Clone() ?? ResourceDefinitionJson.EmptyObject,
                source,
                operation.IsEnabled,
                operation.AllowOverride,
                operation.IsEnabled ? null : operation.DisabledReason);
        }
    }

    private static void ValidateRequiredAttributes(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceAttributeSet attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var requirement in EnumerateRequirements(classDefinition, typeDefinition))
        {
            if (!attributes.Has(requirement.Name) ||
                string.IsNullOrWhiteSpace(attributes.GetString(requirement.Name)))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing,
                    requirement.Message ?? $"Required attribute '{requirement.Name}' is missing.",
                    requirement.Name));
            }
        }
    }

    private static IEnumerable<ResourceAttributeRequirement> EnumerateRequirements(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition)
    {
        foreach (var requirement in classDefinition.RequiredAttributes ?? [])
        {
            yield return requirement;
        }

        foreach (var requirement in typeDefinition.RequiredAttributes ?? [])
        {
            yield return requirement;
        }
    }

    private static void ValidateRequiredCapabilities(
        ResourceCapabilitySet capabilities,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var capability in capabilities)
        {
            if (capability.IsRequired && capability.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.RequiredCapabilityMissing,
                    $"Required capability '{capability.Id}' is missing.",
                    capability.Id));
            }
        }
    }

    private void ValidateAttributes(
        ResourceDefinition definition,
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceAttributeSet attributes,
        ResourceDefinitionResolutionContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var validationContext = new ResourceAttributeValidationContext(
            definition,
            classDefinition,
            typeDefinition,
            context);

        foreach (var attribute in attributes)
        {
            foreach (var validator in _attributeValidators.Where(validator => validator.CanValidate(attribute, validationContext)))
            {
                diagnostics.AddRange(validator.Validate(attribute, validationContext).Diagnostics);
            }
        }
    }
}

public sealed record ResourceDefinitionResolutionContext(
    string? EnvironmentId = null,
    string? PrincipalId = null)
{
    public static ResourceDefinitionResolutionContext Empty { get; } = new();
}

public static class ResourceDefinitionDiagnosticCodes
{
    public const string UnknownResourceType = "resourceDefinition.unknownResourceType";
    public const string UnknownResourceClass = "resourceDefinition.unknownResourceClass";
    public const string RequiredAttributeMissing = "resourceDefinition.requiredAttributeMissing";
    public const string RequiredCapabilityMissing = "resourceDefinition.requiredCapabilityMissing";
    public const string OperationOverrideNotAllowed = "resourceDefinition.operationOverrideNotAllowed";
    public const string CapabilityProviderMissing = "resourceDefinition.capabilityProviderMissing";
    public const string OperationProviderMissing = "resourceDefinition.operationProviderMissing";
    public const string ResourceTypeProviderMissing = "resourceDefinition.resourceTypeProviderMissing";
    public const string DuplicateResourceDefinition = "resourceDefinition.duplicateResourceDefinition";
    public const string ResourceDependencyMissing = "resourceDefinition.resourceDependencyMissing";
    public const string ResourceDefinitionApplyProviderMissing = "resourceDefinition.applyProviderMissing";
}

public static class ResourceDefinitionClassIds
{
    public static readonly ResourceClassId Generic = ResourceClassId.Create("generic");
}
