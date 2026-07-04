namespace CloudShell.ControlPlane.Providers;

public sealed class JavaAppResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "project";
    public static readonly ResourceTypeId ResourceTypeId = "application.java-app";
    public const string ProviderId = "applications.java-app";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ProjectPath = "project.path";
        public static readonly ResourceAttributeId Command = "java.command";
        public static readonly ResourceAttributeId BuildTool = "java.buildTool";
        public static readonly ResourceAttributeId BuildArguments = "java.buildArguments";
        public static readonly ResourceAttributeId ArtifactPath = "java.artifactPath";
        public static readonly ResourceAttributeId MainClass = "java.mainClass";
        public static readonly ResourceAttributeId ClassPath = "java.classPath";
        public static readonly ResourceAttributeId JvmArguments = "java.jvmArguments";
        public static readonly ResourceAttributeId Arguments = "java.arguments";
        public static readonly ResourceAttributeId EndpointRequests = "project.endpointRequests";
        public static readonly ResourceAttributeId EnvironmentVariables = "project.environmentVariables";
        public static readonly ResourceAttributeId ServiceDiscoveryName = "project.serviceDiscoveryName";
        public static readonly ResourceAttributeId References = "project.references";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.ProjectPath] = new(
                Required: true,
                RequiredMessage: "Java app project path is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Command] = new(
                DefaultValue: "java",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.BuildTool] = new(
                Description: "Optional Java project build tool to run before starting the app. Supported values are 'maven' and 'gradle'.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.BuildArguments] = new(
                Description: "Optional build-tool arguments. Defaults to 'package' for Maven and 'build' for Gradle.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ArtifactPath] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.MainClass] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ClassPath] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.JvmArguments] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Arguments] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest),
            [Attributes.EnvironmentVariables] = new(
                Description: "Process environment variables keyed by variable name. Values are resolved when the resource starts.",
                ValueType: ResourceAttributeValueType.ComplexType),
            [Attributes.ServiceDiscoveryName] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.References] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ResourceReference)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.EndpointSource),
            new(ResourceCommonCapabilityIds.Monitoring),
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue),
            new(
                ResourceLogSourceCapabilityIds.LogSources,
                ResourceDefinitionJson.FromValue(ResourceLogSourceDefinitionSet.DefaultConsole()))
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateProjectPath(
            resource.Attributes.GetString(Attributes.ProjectPath),
            diagnostics);
        ValidateLaunchTarget(
            resource.Attributes.GetString(Attributes.ArtifactPath),
            resource.Attributes.GetString(Attributes.MainClass),
            diagnostics);
        ValidateBuildTool(
            resource.Attributes.GetString(Attributes.BuildTool),
            diagnostics);

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
        ValidateProjectPath(
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.ProjectPath),
            diagnostics);
        ValidateLaunchTarget(
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.ArtifactPath),
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.MainClass),
            diagnostics);
        ValidateBuildTool(
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.BuildTool),
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
                    "Accept Java app definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize Java app resource '{resource.Name}'.")
            ],
            []));

    private static void ValidateProjectPath(
        string? projectPath,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.javaApp.pathRequired",
                "Java app project path is required.",
                Attributes.ProjectPath));
        }
    }

    private static void ValidateLaunchTarget(
        string? artifactPath,
        string? mainClass,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) &&
            string.IsNullOrWhiteSpace(mainClass))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.javaApp.launchTargetRequired",
                "Java app artifact path or main class is required.",
                Attributes.ArtifactPath));
        }
    }

    private static void ValidateBuildTool(
        string? buildTool,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(buildTool) ||
            JavaAppBuildTools.IsSupported(buildTool))
        {
            return;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            "application.javaApp.buildToolUnsupported",
            "Java app build tool must be 'maven' or 'gradle'.",
            Attributes.BuildTool));
    }
}

public static class JavaAppBuildTools
{
    public const string Maven = "maven";
    public const string Gradle = "gradle";

    public static bool IsSupported(string buildTool) =>
        string.Equals(buildTool.Trim(), Maven, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(buildTool.Trim(), Gradle, StringComparison.OrdinalIgnoreCase);
}
