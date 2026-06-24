namespace CloudShell.ResourceDefinitions;

public sealed class ResourceResolver
{
    private readonly IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> _classDefinitions;
    private readonly IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> _typeDefinitions;
    private readonly IReadOnlyList<IResourceAttributeValidator> _attributeValidators;

    public ResourceResolver(
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

    public Resource Resolve(
        ResourceDefinition definition,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Resolve(
            ResourceState.FromDefinition(definition),
            context);
    }

    public Resource Resolve(
        ResourceState state,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var typeDefinition = ResolveTypeDefinition(state, diagnostics);
        var classDefinition = ResolveClassDefinition(typeDefinition, diagnostics);
        ValidateAttributeDefinitionDefaults(
            classDefinition.Attributes,
            ResourceDefinitionValueSource.ClassDefinition,
            diagnostics);
        ValidateAttributeDefinitionDefaults(
            typeDefinition.Attributes,
            ResourceDefinitionValueSource.TypeDefinition,
            diagnostics);
        var resourceClass = ResolveResourceClass(classDefinition);
        var resourceType = ResolveResourceType(resourceClass, typeDefinition, diagnostics);
        var attributes = ResolveAttributes(resourceType, state);
        var capabilities = ResolveCapabilities(resourceType, state);
        var operations = ResolveOperations(resourceType, state, diagnostics);

        ValidateRequiredAttributes(classDefinition, typeDefinition, attributes, diagnostics);
        ValidateRequiredCapabilities(capabilities, diagnostics);
        ValidateAttributes(
            state,
            classDefinition,
            typeDefinition,
            attributes,
            context ?? ResourceDefinitionResolutionContext.Empty,
            diagnostics);

        return new(
            state,
            resourceClass,
            resourceType,
            attributes,
            capabilities,
            operations,
            diagnostics);
    }

    private ResourceTypeDefinition ResolveTypeDefinition(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (_typeDefinitions.TryGetValue(state.TypeId, out var typeDefinition))
        {
            return typeDefinition;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            ResourceDefinitionDiagnosticCodes.UnknownResourceType,
            $"Resource type '{state.TypeId}' is not registered.",
            state.TypeId));

        return new(state.TypeId, ResourceDefinitionClassIds.Generic);
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

    private static ResourceClass ResolveResourceClass(ResourceClassDefinition classDefinition) =>
        new(
            classDefinition,
            ResolveClassAttributes(classDefinition),
            ResolveClassCapabilities(classDefinition),
            ResolveClassOperations(classDefinition));

    private static ResourceType ResolveResourceType(
        ResourceClass resourceClass,
        ResourceTypeDefinition typeDefinition,
        List<ResourceDefinitionDiagnostic> diagnostics) =>
        new(
            typeDefinition,
            resourceClass,
            ResolveTypeAttributes(resourceClass.Definition, typeDefinition),
            ResolveTypeCapabilities(resourceClass.Definition, typeDefinition),
            ResolveTypeOperations(resourceClass.Definition, typeDefinition, diagnostics));

    private static ResourceAttributeSet ResolveClassAttributes(ResourceClassDefinition classDefinition)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeResolution>();
        MergeAttributeDefinitions(
            attributes,
            classDefinition.Attributes,
            ResourceDefinitionValueSource.ClassDefinition);
        return new(attributes.Values);
    }

    private static ResourceCapabilitySet ResolveClassCapabilities(ResourceClassDefinition classDefinition)
    {
        var capabilities = new Dictionary<ResourceCapabilityId, ResourceCapabilityResolution>();
        MergeCapabilities(capabilities, classDefinition.Capabilities, ResourceDefinitionValueSource.ClassDefinition);
        return new(capabilities.Values);
    }

    private static ResourceOperationSet ResolveClassOperations(ResourceClassDefinition classDefinition)
    {
        var operations = new Dictionary<ResourceOperationId, ResourceOperationResolution>();
        MergeOperations(operations, classDefinition.Operations, ResourceDefinitionValueSource.ClassDefinition, []);
        return new(operations.Values);
    }

    private static ResourceAttributeSet ResolveTypeAttributes(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeResolution>();
        MergeAttributeDefinitions(
            attributes,
            classDefinition.Attributes,
            ResourceDefinitionValueSource.ClassDefinition);
        MergeAttributeDefinitions(
            attributes,
            typeDefinition.Attributes,
            ResourceDefinitionValueSource.TypeDefinition);
        return new(attributes.Values);
    }

    private static ResourceCapabilitySet ResolveTypeCapabilities(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition)
    {
        var capabilities = new Dictionary<ResourceCapabilityId, ResourceCapabilityResolution>();
        MergeCapabilities(capabilities, classDefinition.Capabilities, ResourceDefinitionValueSource.ClassDefinition);
        MergeCapabilities(capabilities, typeDefinition.Capabilities, ResourceDefinitionValueSource.TypeDefinition);
        return new(capabilities.Values);
    }

    private static ResourceOperationSet ResolveTypeOperations(
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var operations = new Dictionary<ResourceOperationId, ResourceOperationResolution>();
        MergeOperations(operations, classDefinition.Operations, ResourceDefinitionValueSource.ClassDefinition, diagnostics);
        MergeOperations(operations, typeDefinition.Operations, ResourceDefinitionValueSource.TypeDefinition, diagnostics);
        return new(operations.Values);
    }

    private static ResourceAttributeSet ResolveAttributes(
        ResourceType resourceType,
        ResourceState state)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeResolution>();

        foreach (var attribute in resourceType.Attributes)
        {
            attributes[attribute.Name] = attribute;
        }

        MergeAttributes(attributes, state.ResourceAttributes, ResourceDefinitionValueSource.ResourceState);

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

    private static void MergeAttributeDefinitions(
        Dictionary<ResourceAttributeId, ResourceAttributeResolution> target,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions,
        ResourceDefinitionValueSource source)
    {
        if (attributeDefinitions is null)
        {
            return;
        }

        foreach (var (name, attributeDefinition) in attributeDefinitions)
        {
            if (attributeDefinition.DefaultValue is null ||
                !attributeDefinition.DefaultValue.TryGetScalarString(out var value))
            {
                continue;
            }

            target[name] = new(
                name,
                value,
                source);
        }
    }

    private static ResourceCapabilitySet ResolveCapabilities(
        ResourceType resourceType,
        ResourceState state)
    {
        var capabilities = new Dictionary<ResourceCapabilityId, ResourceCapabilityResolution>();

        foreach (var capability in resourceType.Capabilities)
        {
            capabilities[capability.Id] = capability;
        }

        foreach (var (id, payload) in state.CapabilityPayloads)
        {
            capabilities[id] = new(
                id,
                ResourceDefinitionJson.Clone(payload),
                ResourceDefinitionValueSource.ResourceState);
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
        ResourceType resourceType,
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var operations = new Dictionary<ResourceOperationId, ResourceOperationResolution>();

        foreach (var operation in resourceType.Operations)
        {
            operations[operation.Id] = operation;
        }

        foreach (var (id, payload) in state.OperationPayloads)
        {
            if (operations.TryGetValue(id, out var inherited) && !inherited.AllowOverride)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed,
                    $"Operation '{id}' cannot be overridden by resource '{state.Name}'.",
                    id));
                continue;
            }

            operations[id] = new(
                id,
                ResourceDefinitionJson.Clone(payload),
                ResourceDefinitionValueSource.ResourceState,
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
        var requirements = new Dictionary<ResourceAttributeId, ResourceAttributeRequirement>();

        foreach (var requirement in classDefinition.RequiredAttributes ?? [])
        {
            requirements[requirement.Name] = requirement;
        }

        if (classDefinition.Attributes is not null)
        {
            foreach (var (name, attributeDefinition) in classDefinition.Attributes)
            {
                if (attributeDefinition.Required)
                {
                    requirements[name] = new(
                        name,
                        attributeDefinition.RequiredMessage);
                }
            }
        }

        foreach (var requirement in typeDefinition.RequiredAttributes ?? [])
        {
            requirements[requirement.Name] = requirement;
        }

        if (typeDefinition.Attributes is not null)
        {
            foreach (var (name, attributeDefinition) in typeDefinition.Attributes)
            {
                if (attributeDefinition.Required)
                {
                    requirements[name] = new(
                        name,
                        attributeDefinition.RequiredMessage);
                }
            }
        }

        foreach (var requirement in requirements.Values)
        {
            yield return requirement;
        }
    }

    private static void ValidateAttributeDefinitionDefaults(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions,
        ResourceDefinitionValueSource source,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (attributeDefinitions is null)
        {
            return;
        }

        foreach (var (name, attributeDefinition) in attributeDefinitions)
        {
            if (attributeDefinition.DefaultValue is null ||
                attributeDefinition.ValueShape is null)
            {
                continue;
            }

            ValidateAttributeDefinitionDefault(
                name.ToString(),
                name,
                attributeDefinition.DefaultValue,
                attributeDefinition.ValueShape,
                source,
                diagnostics);
        }
    }

    private static void ValidateAttributeDefinitionDefault(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        ResourceAttributeValueShape shape,
        ResourceDefinitionValueSource source,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!AttributeValueMatchesShape(value, shape))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid,
                $"Attribute default '{path}' from {source} has value kind '{value.Kind}' but declares shape '{shape.Kind}'.",
                target));
            return;
        }

        if (shape.Kind == ResourceAttributeValueKind.Object &&
            shape.Fields is not null)
        {
            var objectValue = value.ObjectValue ?? new Dictionary<string, ResourceAttributeValue>();

            foreach (var field in shape.Fields)
            {
                if (!objectValue.TryGetValue(field.Name, out var fieldValue))
                {
                    if (field.Required)
                    {
                        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                            ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid,
                            $"Attribute default '{path}' from {source} is missing required field '{field.Name}'.",
                            $"{target}.{field.Name}"));
                    }

                    continue;
                }

                ValidateAttributeDefinitionDefault(
                    $"{path}.{field.Name}",
                    target,
                    fieldValue,
                    field.ValueShape,
                    source,
                    diagnostics);
            }
        }

        if (shape.Kind == ResourceAttributeValueKind.Array &&
            shape.ElementShape is not null)
        {
            var index = 0;

            foreach (var element in value.ArrayValue ?? [])
            {
                ValidateAttributeDefinitionDefault(
                    $"{path}[{index}]",
                    target,
                    element,
                    shape.ElementShape,
                    source,
                    diagnostics);
                index++;
            }
        }
    }

    private static bool AttributeValueMatchesShape(
        ResourceAttributeValue value,
        ResourceAttributeValueShape shape) =>
        shape.Kind switch
        {
            ResourceAttributeValueKind.Decimal => value.Kind is
                ResourceAttributeValueKind.Decimal or ResourceAttributeValueKind.Integer,
            _ => value.Kind == shape.Kind
        };

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
        ResourceState state,
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        ResourceAttributeSet attributes,
        ResourceDefinitionResolutionContext context,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var validationContext = new ResourceAttributeValidationContext(
            state,
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
    public const string ResourceProjectionProviderMissing = "resourceDefinition.projectionProviderMissing";
    public const string ResourceChangeApplyProviderMissing = "resourceDefinition.changeApplyProviderMissing";
    public const string ResourceGraphVersionConflict = "resourceDefinition.graphVersionConflict";
    public const string ResourceGraphResourceMissing = "resourceDefinition.graphResourceMissing";
    public const string ResourceDependencyCycle = "resourceDefinition.dependencyCycle";
    public const string ResourceCapabilityProjectionMissing = "resourceDefinition.capabilityProjectionMissing";
    public const string ResourceOperationProjectionMissing = "resourceDefinition.operationProjectionMissing";
    public const string AttributeDefinitionDefaultInvalid = "resourceDefinition.attributeDefinitionDefaultInvalid";
}

public static class ResourceDefinitionClassIds
{
    public static readonly ResourceClassId Generic = ResourceClassId.Create("generic");
}
