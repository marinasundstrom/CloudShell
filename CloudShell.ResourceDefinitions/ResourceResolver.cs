namespace CloudShell.ResourceDefinitions;

public sealed class ResourceResolver
{
    private readonly IReadOnlyDictionary<ResourceClassId, ResourceClassDefinition> _classDefinitions;
    private readonly IReadOnlyDictionary<ResourceTypeId, ResourceTypeDefinition> _typeDefinitions;
    private readonly IReadOnlyList<IResourceAttributeValidator> _attributeValidators;
    private readonly IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        _sharedAttributeValueShapes;

    public ResourceResolver(
        IEnumerable<ResourceClassDefinition> classDefinitions,
        IEnumerable<ResourceTypeDefinition> typeDefinitions,
        IEnumerable<IResourceAttributeValidator>? attributeValidators = null,
        IEnumerable<IResourceAttributeValueShapeProvider>? attributeValueShapeProviders = null)
    {
        ArgumentNullException.ThrowIfNull(classDefinitions);
        ArgumentNullException.ThrowIfNull(typeDefinitions);

        _classDefinitions = classDefinitions.ToDictionary(
            definition => definition.ClassId);
        _typeDefinitions = typeDefinitions.ToDictionary(
            definition => definition.TypeId);
        _attributeValidators = attributeValidators?.ToArray() ?? [];
        _sharedAttributeValueShapes = MergeAttributeValueShapeProviders(attributeValueShapeProviders);
    }

    public Resource Resolve(
        ResourceDefinition definition,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return Resolve(
            ResourceState.FromDefinition(definition),
            validateReadOnlyDefinitionAttributes: true,
            context);
    }

    public Resource Resolve(
        ResourceState state,
        ResourceDefinitionResolutionContext? context = null) =>
        Resolve(
            state,
            validateReadOnlyDefinitionAttributes: false,
            context);

    private Resource Resolve(
        ResourceState state,
        bool validateReadOnlyDefinitionAttributes,
        ResourceDefinitionResolutionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        var typeDefinition = ResolveTypeDefinition(state, diagnostics);
        var classDefinition = ResolveClassDefinition(typeDefinition, diagnostics);
        ValidateAttributeDefinitionDefaults(
            classDefinition.Attributes,
            MergeAttributeValueShapeDefinitions(
                _sharedAttributeValueShapes,
                classDefinition.AttributeValueShapes),
            ResourceDefinitionValueSource.ClassDefinition,
            diagnostics);
        ValidateAttributeDefinitionDefaults(
            typeDefinition.Attributes,
            MergeAttributeValueShapeDefinitions(
                _sharedAttributeValueShapes,
                classDefinition.AttributeValueShapes,
                typeDefinition.AttributeValueShapes),
            ResourceDefinitionValueSource.TypeDefinition,
            diagnostics);
        var resourceClass = ResolveResourceClass(classDefinition);
        var resourceType = ResolveResourceType(resourceClass, typeDefinition, diagnostics);
        var attributes = ResolveAttributes(resourceType, state);
        var capabilities = ResolveCapabilities(resourceType, state);
        var operations = ResolveOperations(resourceType, state, diagnostics);

        ValidateRequiredAttributes(classDefinition, typeDefinition, attributes, diagnostics);
        ValidateRequiredCapabilities(capabilities, diagnostics);
        if (validateReadOnlyDefinitionAttributes)
        {
            ValidateReadOnlyDefinitionAttributes(state, attributes, diagnostics);
        }

        ValidateResourceAttributeValues(
            state,
            classDefinition,
            typeDefinition,
            _sharedAttributeValueShapes,
            diagnostics);
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

    private static void ValidateReadOnlyDefinitionAttributes(
        ResourceState state,
        ResourceAttributeSet attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var attributeId in state.ResourceAttributeValues.Keys)
        {
            if (attributes.Resolve(attributeId)?.ReadOnly == true)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    ResourceDefinitionDiagnosticCodes.ReadOnlyAttributeChange,
                    $"Attribute '{attributeId}' is read-only and cannot be declared in a resource definition.",
                    attributeId));
            }
        }
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
        var readOnlyAttributes = ResolveReadOnlyAttributeIds(resourceType);
        var attributeMutability = ResolveAttributeMutability(resourceType);

        foreach (var attribute in resourceType.Attributes)
        {
            attributes[attribute.Name] = attribute;
        }

        MergeAttributes(
            attributes,
            state.ResourceAttributeValues,
            ResourceDefinitionValueSource.ResourceState,
            readOnlyAttributes,
            attributeMutability);

        return new(attributes.Values);
    }

    private static IReadOnlySet<ResourceAttributeId> ResolveReadOnlyAttributeIds(
        ResourceType resourceType)
    {
        var readOnlyAttributes = new HashSet<ResourceAttributeId>();
        MergeReadOnlyAttributeDefinitions(readOnlyAttributes, resourceType.Class.Definition.Attributes);
        MergeReadOnlyAttributeDefinitions(readOnlyAttributes, resourceType.Definition.Attributes);
        return readOnlyAttributes;
    }

    private static void MergeAttributes(
        Dictionary<ResourceAttributeId, ResourceAttributeResolution> target,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>? attributes,
        ResourceDefinitionValueSource source,
        IReadOnlySet<ResourceAttributeId>? readOnlyAttributes = null,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeMutability>? attributeMutability = null)
    {
        if (attributes is null)
        {
            return;
        }

        foreach (var (name, value) in attributes)
        {
            target.TryGetValue(name, out var inherited);
            var isDefined = inherited?.IsDefined == true;
            var readOnly =
                inherited?.ReadOnly == true ||
                readOnlyAttributes?.Contains(name) == true;
            var mutability =
                isDefined
                    ? inherited!.Mutability
                    : attributeMutability is not null &&
                        attributeMutability.TryGetValue(name, out var configuredMutability)
                            ? configuredMutability
                            : ResourceAttributeMutability.CallerManaged;
            target[name] = new(
                name,
                value,
                source,
                readOnly,
                mutability,
                IsDefined: isDefined);
        }
    }

    private static IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeMutability> ResolveAttributeMutability(
        ResourceType resourceType)
    {
        var attributeMutability = new Dictionary<ResourceAttributeId, ResourceAttributeMutability>();
        MergeAttributeMutability(attributeMutability, resourceType.Class.Definition.Attributes);
        MergeAttributeMutability(attributeMutability, resourceType.Definition.Attributes);
        return attributeMutability;
    }

    private static void MergeReadOnlyAttributeDefinitions(
        HashSet<ResourceAttributeId> target,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions)
    {
        if (attributeDefinitions is null)
        {
            return;
        }

        foreach (var (name, attributeDefinition) in attributeDefinitions)
        {
            if (attributeDefinition.ReadOnly == true)
            {
                target.Add(name);
            }
            else if (attributeDefinition.ReadOnly == false)
            {
                target.Remove(name);
            }
        }
    }

    private static void MergeAttributeMutability(
        Dictionary<ResourceAttributeId, ResourceAttributeMutability> target,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? attributeDefinitions)
    {
        if (attributeDefinitions is null)
        {
            return;
        }

        foreach (var (name, attributeDefinition) in attributeDefinitions)
        {
            if (attributeDefinition.Mutability.HasValue)
            {
                target[name] = attributeDefinition.Mutability.Value;
            }
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
            var value = attributeDefinition.DefaultValue;

            var valueSource =
                value is null &&
                target.TryGetValue(name, out var inherited) &&
                inherited.IsSet
                    ? inherited.Source
                    : source;
            value ??= target.TryGetValue(name, out inherited)
                ? inherited.AttributeValue
                : null;

            target[name] = new(
                name,
                value,
                valueSource,
                attributeDefinition.ReadOnly == true,
                attributeDefinition.Mutability ?? ResourceAttributeMutability.CallerManaged,
                IsDefined: true);
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
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? shapeDefinitions,
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
                attributeDefinition.ValueType is null)
            {
                continue;
            }

            ValidateAttributeDefinitionDefault(
                name.ToString(),
                name,
                attributeDefinition.DefaultValue,
                attributeDefinition,
                shapeDefinitions,
                source,
                diagnostics);
        }
    }

    private static void ValidateResourceAttributeValues(
        ResourceState state,
        ResourceClassDefinition classDefinition,
        ResourceTypeDefinition typeDefinition,
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? sharedShapeDefinitions,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var shapeDefinitions = MergeAttributeValueShapeDefinitions(
            sharedShapeDefinitions,
            classDefinition.AttributeValueShapes,
            typeDefinition.AttributeValueShapes);

        foreach (var (name, value) in state.ResourceAttributeValues)
        {
            var definition = ResolveAttributeDefinition(
                name,
                classDefinition.Attributes,
                typeDefinition.Attributes);
            if (definition is null)
            {
                continue;
            }

            ValidateAttributeValue(
                name.ToString(),
                name,
                value,
                definition,
                shapeDefinitions,
                ResourceDefinitionDiagnosticCodes.AttributeValueInvalid,
                "Attribute value",
                diagnostics);
        }
    }

    private static ResourceAttributeDefinition? ResolveAttributeDefinition(
        ResourceAttributeId name,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? classAttributes,
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? typeAttributes)
    {
        if (typeAttributes is not null &&
            typeAttributes.TryGetValue(name, out var typeDefinition))
        {
            return typeDefinition;
        }

        if (classAttributes is not null &&
            classAttributes.TryGetValue(name, out var classDefinition))
        {
            return classDefinition;
        }

        return null;
    }

    private static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        MergeAttributeValueShapeProviders(
            IEnumerable<IResourceAttributeValueShapeProvider>? shapeProviders)
    {
        var shapes = new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>();

        foreach (var provider in shapeProviders ?? [])
        {
            foreach (var (id, shape) in provider.GetAttributeValueShapes())
            {
                shapes[id] = shape;
            }
        }

        return shapes;
    }

    private static IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>?
        MergeAttributeValueShapeDefinitions(
            params IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>?[] shapeSets)
    {
        var shapes = new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>();

        foreach (var shapeSet in shapeSets)
        {
            if (shapeSet is null)
            {
                continue;
            }

            foreach (var (id, shape) in shapeSet)
            {
                shapes[id] = shape;
            }
        }

        return shapes.Count == 0 ? null : shapes;
    }

    private static void ValidateAttributeDefinitionDefault(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        ResourceAttributeDefinition definition,
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? shapeDefinitions,
        ResourceDefinitionValueSource source,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        ValidateAttributeValue(
            path,
            target,
            value,
            definition,
            shapeDefinitions,
            ResourceDefinitionDiagnosticCodes.AttributeDefinitionDefaultInvalid,
            $"Attribute default from {source}",
            diagnostics);
    }

    private static void ValidateAttributeValue(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        ResourceAttributeDefinition definition,
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? shapeDefinitions,
        string diagnosticCode,
        string diagnosticSubject,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (definition.ValueType is null)
        {
            return;
        }

        if (definition.IsCollection)
        {
            ValidateAttributeValueCollection(
                path,
                target,
                value,
                definition,
                shapeDefinitions,
                diagnosticCode,
                diagnosticSubject,
                diagnostics);
            return;
        }

        if (definition.ValueType == ResourceAttributeValueType.ResourceReference)
        {
            ValidateResourceReferenceAttributeValue(
                path,
                target,
                value,
                diagnosticCode,
                diagnosticSubject,
                diagnostics);
            return;
        }

        if (!AttributeValueMatchesType(value, definition.ValueType.Value))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                diagnosticCode,
                $"{diagnosticSubject} '{path}' has value kind '{value.Kind}' but declares type '{definition.ValueType}'.",
                target));
            return;
        }

        var shape = ResolveAttributeValueShape(definition, shapeDefinitions);

        if (definition.ValueType == ResourceAttributeValueType.ComplexType &&
            shape?.Attributes is not null)
        {
            var objectValue = value.ObjectValue ?? new Dictionary<string, ResourceAttributeValue>();

            foreach (var (fieldName, fieldDefinition) in shape.Attributes)
            {
                if (!objectValue.TryGetValue(fieldName.ToString(), out var fieldValue))
                {
                    if (fieldDefinition.Required)
                    {
                        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                            diagnosticCode,
                            $"{diagnosticSubject} '{path}' is missing required field '{fieldName}'.",
                            $"{target}.{fieldName}"));
                    }

                    continue;
                }

                ValidateAttributeValue(
                    $"{path}.{fieldName}",
                    target,
                    fieldValue,
                    fieldDefinition,
                    shapeDefinitions,
                    diagnosticCode,
                    diagnosticSubject,
                    diagnostics);
            }
        }
    }

    private static void ValidateAttributeValueCollection(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        ResourceAttributeDefinition definition,
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? shapeDefinitions,
        string diagnosticCode,
        string diagnosticSubject,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (value.Kind != ResourceAttributeValueKind.Array)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                diagnosticCode,
                $"{diagnosticSubject} '{path}' has value kind '{value.Kind}' but declares a collection.",
                target));
            return;
        }

        var values = value.ArrayValue ?? [];

        if (definition.Collection?.MinSize is not null &&
            values.Count < definition.Collection.MinSize.Value)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                diagnosticCode,
                $"{diagnosticSubject} '{path}' has {values.Count} item(s) but requires at least {definition.Collection.MinSize.Value}.",
                target));
        }

        if (definition.Collection?.MaxSize is not null &&
            values.Count > definition.Collection.MaxSize.Value)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                diagnosticCode,
                $"{diagnosticSubject} '{path}' has {values.Count} item(s) but allows at most {definition.Collection.MaxSize.Value}.",
                target));
        }

        var itemDefinition = definition with
        {
            IsCollection = false,
            Collection = null
        };
        var index = 0;

        foreach (var element in values)
        {
            ValidateAttributeValue(
                $"{path}[{index}]",
                target,
                element,
                itemDefinition,
                shapeDefinitions,
                diagnosticCode,
                diagnosticSubject,
                diagnostics);
            index++;
        }
    }

    private static ResourceAttributeValueShape? ResolveAttributeValueShape(
        ResourceAttributeDefinition definition,
        IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? shapeDefinitions)
    {
        if (definition.ValueShape is not null)
        {
            return definition.ValueShape;
        }

        if (definition.ValueShapeId is { } shapeId &&
            shapeDefinitions is not null &&
            shapeDefinitions.TryGetValue(shapeId, out var shapeDefinition))
        {
            return shapeDefinition.Shape;
        }

        return null;
    }

    private static void ValidateResourceReferenceAttributeValue(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        string diagnosticCode,
        string diagnosticSubject,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (value.Kind != ResourceAttributeValueKind.Object ||
            value.ObjectValue is null)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                diagnosticCode,
                $"{diagnosticSubject} '{path}' has value kind '{value.Kind}' but declares type '{ResourceAttributeValueType.ResourceReference}'.",
                target));
            return;
        }

        ValidateResourceReferenceRequiredField(path, target, value, "value", diagnosticCode, diagnosticSubject, diagnostics);
        ValidateResourceReferenceRequiredField(path, target, value, "relationship", diagnosticCode, diagnosticSubject, diagnostics);
        ValidateResourceReferenceRequiredField(path, target, value, "addressingMode", diagnosticCode, diagnosticSubject, diagnostics);
    }

    private static void ValidateResourceReferenceRequiredField(
        string path,
        ResourceAttributeId target,
        ResourceAttributeValue value,
        string fieldName,
        string diagnosticCode,
        string diagnosticSubject,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (value.ObjectValue is not null &&
            value.ObjectValue.TryGetValue(fieldName, out var fieldValue) &&
            fieldValue.Kind == ResourceAttributeValueKind.String &&
            !string.IsNullOrWhiteSpace(fieldValue.StringValue))
        {
            return;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            diagnosticCode,
            $"{diagnosticSubject} '{path}' is missing required resource reference field '{fieldName}'.",
            $"{target}.{fieldName}"));
    }

    private static bool AttributeValueMatchesType(
        ResourceAttributeValue value,
        ResourceAttributeValueType valueType) =>
        valueType switch
        {
            ResourceAttributeValueType.String => value.Kind == ResourceAttributeValueKind.String,
            ResourceAttributeValueType.Boolean => value.Kind == ResourceAttributeValueKind.Boolean ||
                TryParseString<bool>(value, bool.TryParse, out _),
            ResourceAttributeValueType.Integer => value.Kind == ResourceAttributeValueKind.Integer ||
                TryParseString<long>(value, long.TryParse, out _),
            ResourceAttributeValueType.FloatingPoint => value.Kind is
                ResourceAttributeValueKind.Decimal or ResourceAttributeValueKind.Integer ||
                TryParseString<decimal>(value, decimal.TryParse, out _),
            ResourceAttributeValueType.ComplexType => value.Kind == ResourceAttributeValueKind.Object,
            ResourceAttributeValueType.ResourceReference => value.TryGetResourceReference(out _),
            _ => false
        };

    private static bool TryParseString<TValue>(
        ResourceAttributeValue value,
        TryParse<TValue> tryParse,
        out TValue parsed)
    {
        if (value.Kind == ResourceAttributeValueKind.String &&
            value.StringValue is not null &&
            tryParse(value.StringValue, out parsed!))
        {
            return true;
        }

        parsed = default!;
        return false;
    }

    private delegate bool TryParse<TValue>(string value, out TValue parsed);

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
    public const string ResourceReferenceTypeMismatch = "resourceDefinition.referenceTypeMismatch";
    public const string ResourceCapabilityReferenceMissing = "resourceDefinition.capabilityReferenceMissing";
    public const string ResourceCapabilityReferenceInvalid = "resourceDefinition.capabilityReferenceInvalid";
    public const string ResourceDefinitionApplyProviderMissing = "resourceDefinition.applyProviderMissing";
    public const string ResourceProjectionProviderMissing = "resourceDefinition.projectionProviderMissing";
    public const string ResourceChangeApplyProviderMissing = "resourceDefinition.changeApplyProviderMissing";
    public const string ResourceGraphVersionConflict = "resourceDefinition.graphVersionConflict";
    public const string ResourceGraphResourceMissing = "resourceDefinition.graphResourceMissing";
    public const string ResourceDependencyCycle = "resourceDefinition.dependencyCycle";
    public const string ResourceCapabilityProjectionMissing = "resourceDefinition.capabilityProjectionMissing";
    public const string ResourceOperationProjectionMissing = "resourceDefinition.operationProjectionMissing";
    public const string AttributeDefinitionDefaultInvalid = "resourceDefinition.attributeDefinitionDefaultInvalid";
    public const string AttributeValueInvalid = "resourceDefinition.attributeValueInvalid";
    public const string ReadOnlyAttributeChange = "resourceDefinition.readOnlyAttributeChange";
    public const string ResourceDefinitionTargetMismatch = "resourceDefinition.targetMismatch";
}

public static class ResourceDefinitionClassIds
{
    public static readonly ResourceClassId Generic = ResourceClassId.Create("generic");
}
