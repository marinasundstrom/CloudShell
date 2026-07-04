namespace CloudShell.ControlPlane.Providers;

public sealed class RabbitMQResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "application.rabbitmq";
    public const string ProviderId = "applications.rabbitmq";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Version = "version";
        public static readonly ResourceAttributeId ManagementUi = "rabbitmq.managementUi";
        public static readonly ResourceAttributeId EndpointRequests = "endpointRequests";
        public static readonly ResourceAttributeId UserName = "user.username";
        public static readonly ResourceAttributeId UserPassword = "user.password";
        public static readonly ResourceAttributeId UserManaged = "user.managed";
        public static readonly ResourceAttributeId VirtualHost = "vhost";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId ReconcileAccess =
            "application.rabbitmq.reconcile-access";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Version] = new(
                DefaultValue: "3",
                Required: true,
                RequiredMessage: "RabbitMQ version is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ManagementUi] = new(
                DefaultValue: "true",
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest),
            [Attributes.UserName] = new(
                Description: "Optional RabbitMQ bootstrap username. Omit to use the provider/runtime default.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.UserPassword] = new(
                Description: "Optional RabbitMQ bootstrap password. Omit to use the provider/runtime default.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.UserManaged] = new(
                Description: "When true, the provider generates a RabbitMQ bootstrap username and password for the resource.",
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.VirtualHost] = new(
                Description: "Optional RabbitMQ default virtual host. Omit to use the RabbitMQ default virtual host.",
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue),
            new(
                ResourceLogSourceCapabilityIds.LogSources,
                ResourceDefinitionJson.FromValue(
                    new ResourceLogSourceDefinitionSet(
                        [ResourceLogSourceDefinition.DefaultContainerConsole()])))
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart),
            new(Operations.ReconcileAccess)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateVersion(resource.Attributes.GetString(Attributes.Version), diagnostics);
        ValidateUser(resource, diagnostics);
        ValidateVirtualHost(resource.Attributes.GetString(Attributes.VirtualHost), diagnostics);

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);

        if (changes.ProposedState.ResourceAttributes.TryGetValue(Attributes.Version, out var version))
        {
            ValidateVersion(version, diagnostics);
        }

        ValidateUser(changes.ProposedState, diagnostics);
        ValidateVirtualHost(
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.VirtualHost),
            diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(changes, changes.ProposedState, diagnostics));
    }

    public bool CanPlan(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionApplyPlan> PlanApplyAsync(
        Resource resource,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ResourceDefinitionApplyPlan(
            resource,
            [
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.AcceptDefinition,
                    "Accept RabbitMQ definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize RabbitMQ resource '{resource.Name}'.")
            ],
            []));

    private static void ValidateVersion(
        string? version,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.versionRequired",
                "RabbitMQ version is required.",
                Attributes.Version));
        }
    }

    private static void ValidateUser(
        Resource resource,
        List<ResourceDefinitionDiagnostic> diagnostics) =>
        ValidateUser(
            resource.Attributes.GetString(Attributes.UserName),
            resource.Attributes.GetString(Attributes.UserPassword),
            resource.Attributes.GetString(Attributes.UserManaged),
            diagnostics);

    private static void ValidateUser(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics) =>
        ValidateUser(
            state.ResourceAttributes.GetValueOrDefault(Attributes.UserName),
            state.ResourceAttributes.GetValueOrDefault(Attributes.UserPassword),
            state.ResourceAttributes.GetValueOrDefault(Attributes.UserManaged),
            diagnostics);

    private static void ValidateUser(
        string? userName,
        string? password,
        string? managed,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var hasUserName = !string.IsNullOrWhiteSpace(userName);
        var hasPassword = !string.IsNullOrWhiteSpace(password);
        var isManaged = bool.TryParse(managed, out var managedValue) && managedValue;

        if (isManaged && (hasUserName || hasPassword))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.userManagedWithExplicitCredentials",
                "RabbitMQ user-managed credentials cannot be combined with explicit username or password attributes.",
                Attributes.UserManaged));
            return;
        }

        if (hasUserName != hasPassword)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.userCredentialPairRequired",
                "RabbitMQ username and password attributes must be declared together.",
                hasUserName ? Attributes.UserPassword : Attributes.UserName));
        }
    }

    private static void ValidateVirtualHost(
        string? virtualHost,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (virtualHost is not null &&
            string.IsNullOrWhiteSpace(virtualHost))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.rabbitmq.virtualHostRequired",
                "RabbitMQ virtual host cannot be empty when declared.",
                Attributes.VirtualHost));
        }
    }

    internal static bool TryGetContainerHostResourceId(
        ResourceState state,
        out string containerHostResourceId)
    {
        foreach (var reference in state.StartupDependencies)
        {
            if (reference.TypeId is { } typeId &&
                IsContainerHostResourceType(typeId) &&
                reference.TryGetDependsOnResourceId(out containerHostResourceId))
            {
                return true;
            }
        }

        containerHostResourceId = string.Empty;
        return false;
    }

    internal static bool IsContainerHostResourceType(ResourceTypeId typeId) =>
        typeId == ContainerHostResourceTypeProvider.ResourceTypeId ||
        typeId == DockerHostResourceTypeProvider.ResourceTypeId;
}
