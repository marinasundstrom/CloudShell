namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "iot.device-registry";
    public const string ProviderId = "iot.device-registry";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Kind = "kind";
        public static readonly ResourceAttributeId Endpoint = "endpoint";
        public static readonly ResourceAttributeId TrustedCertificates = "trust.certificates";
        public static readonly ResourceAttributeId AllowedSubjectPrefixes = "enrollmentPolicy.subjectPrefixes";
        public static readonly ResourceAttributeId RequiredClaims = "enrollmentPolicy.requiredClaims";
        public static readonly ResourceAttributeId EnrollmentProfiles = "enrollment.profiles";
        public static readonly ResourceAttributeId EnrolledDeviceCount = "enrolledDeviceCount";
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
            [Attributes.Kind] = new(
                DefaultValue: "registry",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Endpoint] = new(ValueType: ResourceAttributeValueType.String),
            [Attributes.TrustedCertificates] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["vaultResourceId"] = new(
                        Required: true,
                        RequiredMessage: "Trusted certificate vault resource ID is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Trusted certificate name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["version"] = new(ValueType: ResourceAttributeValueType.String)
                }),
                description: "Vault-backed certificate references trusted for device factory enrollment."),
            [Attributes.AllowedSubjectPrefixes] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.String,
                description: "Accepted device subject prefixes for enrollment."),
            [Attributes.RequiredClaims] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Required claim name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["value"] = new(
                        Required: true,
                        RequiredMessage: "Required claim value is required.",
                        ValueType: ResourceAttributeValueType.String)
                }),
                description: "Claims that must be present on enrollment requests."),
            [Attributes.EnrollmentProfiles] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Enrollment profile name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["kind"] = new(ValueType: ResourceAttributeValueType.String),
                    ["policy"] = new(ValueType: ResourceAttributeValueType.ComplexType),
                    ["permissionGrants"] = ResourceAttributeDefinition.Collection(
                        ResourceAttributeValueType.ComplexType,
                        itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                        {
                            ["targetResourceId"] = new(
                                Required: true,
                                RequiredMessage: "Enrollment profile permission target resource ID is required.",
                                ValueType: ResourceAttributeValueType.String),
                            ["permission"] = new(
                                Required: true,
                                RequiredMessage: "Enrollment profile permission is required.",
                                ValueType: ResourceAttributeValueType.String)
                        }))
                }),
                description: "Enrollment profiles that provision device identity access grants."),
            [Attributes.EnrolledDeviceCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.Monitoring),
            new(ResourceCommonCapabilityIds.EndpointSource),
            new(
                ResourceHealthCheckCapabilityIds.HealthChecks,
                ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                [
                    ResourceHealthCheckDefinition.Http(
                        "/healthz",
                        endpointName: "registry"),
                    ResourceHealthCheckDefinition.HttpLiveness(
                        "/healthz",
                        endpointName: "registry")
                ])))
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
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(
            ValidateResolvedResource(resource)));

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);
        diagnostics.AddRange(ValidateExplicitState(changes.ProposedState));

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(
                    changes,
                    EnsureProviderAttributes(changes.ProposedState),
                    diagnostics));
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
                    "Accept Device Registry definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize Device Registry resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateDeviceCount(
            resource.Attributes.GetString(Attributes.EnrolledDeviceCount),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.EnrolledDeviceCount, out var count))
        {
            ValidateDeviceCount(count, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.TrustedCertificates))
        {
            ValidateTrustedCertificates(state, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.AllowedSubjectPrefixes))
        {
            ValidateSubjectPrefixes(state, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.RequiredClaims))
        {
            ValidateRequiredClaims(state, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.EnrollmentProfiles))
        {
            ValidateEnrollmentProfiles(state, diagnostics);
        }

        return diagnostics;
    }

    private static ResourceState EnsureProviderAttributes(ResourceState state)
    {
        if (state.ResourceAttributeValues.ContainsKey(Attributes.EnrolledDeviceCount))
        {
            return state;
        }

        var attributes = state.ResourceAttributeValues.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value);
        attributes[Attributes.EnrolledDeviceCount] = ResourceAttributeValue.Integer(0);

        return state with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
    }

    private static void ValidateTrustedCertificates(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var certificates = state.ResourceAttributeValues.GetObject<ResourceCertificateReference[]>(
            Attributes.TrustedCertificates) ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var certificate in certificates)
        {
            if (string.IsNullOrWhiteSpace(certificate.VaultResourceId))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.trustedCertificateVaultRequired",
                    "Trusted certificate vault resource ID is required.",
                    Attributes.TrustedCertificates));
            }

            if (string.IsNullOrWhiteSpace(certificate.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.trustedCertificateNameRequired",
                    "Trusted certificate name is required.",
                    Attributes.TrustedCertificates));
            }

            if (!string.IsNullOrWhiteSpace(certificate.VaultResourceId) &&
                !string.IsNullOrWhiteSpace(certificate.Name) &&
                !keys.Add(CreateTrustedCertificateKey(certificate)))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.trustedCertificateDuplicate",
                    $"Trusted certificate '{certificate.Name}' from vault '{certificate.VaultResourceId}' is declared more than once.",
                    Attributes.TrustedCertificates));
            }
        }
    }

    private static void ValidateSubjectPrefixes(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var prefixes = state.ResourceAttributeValues.GetObject<string[]>(
            Attributes.AllowedSubjectPrefixes) ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var prefix in prefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.subjectPrefixRequired",
                    "Enrollment subject prefixes cannot be empty.",
                    Attributes.AllowedSubjectPrefixes));
            }
            else if (!keys.Add(prefix.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.subjectPrefixDuplicate",
                    $"Enrollment subject prefix '{prefix}' is declared more than once.",
                    Attributes.AllowedSubjectPrefixes));
            }
        }
    }

    private static void ValidateRequiredClaims(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var claims = state.ResourceAttributeValues.GetObject<DeviceEnrollmentRequiredClaim[]>(
            Attributes.RequiredClaims) ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.requiredClaimNameRequired",
                    "Enrollment required claim name is required.",
                    Attributes.RequiredClaims));
            }

            if (string.IsNullOrWhiteSpace(claim.Value))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.requiredClaimValueRequired",
                    "Enrollment required claim value is required.",
                    Attributes.RequiredClaims));
            }

            if (!string.IsNullOrWhiteSpace(claim.Name) &&
                !keys.Add(claim.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.requiredClaimDuplicate",
                    $"Enrollment required claim '{claim.Name}' is declared more than once.",
                    Attributes.RequiredClaims));
            }
        }
    }

    private static void ValidateDeviceCount(
        string? countValue,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(countValue) &&
            (!int.TryParse(countValue, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "iot.deviceRegistry.enrolledDeviceCountInvalid",
                "Device Registry enrolled device count must be a non-negative integer.",
                Attributes.EnrolledDeviceCount));
        }
    }

    private static void ValidateEnrollmentProfiles(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var profiles = state.ResourceAttributeValues.GetObject<DeviceEnrollmentProfile[]>(
            Attributes.EnrollmentProfiles) ?? [];
        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileNameRequired",
                    "Enrollment profile name is required.",
                    Attributes.EnrollmentProfiles));
            }
            else if (!profileNames.Add(profile.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileDuplicate",
                    $"Enrollment profile '{profile.Name}' is declared more than once.",
                    Attributes.EnrollmentProfiles));
            }

            ValidateEnrollmentProfilePolicy(profile, diagnostics);
            ValidateEnrollmentProfileGrants(profile, diagnostics);
        }
    }

    private static void ValidateEnrollmentProfilePolicy(
        DeviceEnrollmentProfile profile,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(profile.Kind) &&
            !string.Equals(profile.Kind, DeviceEnrollmentProfileKinds.Group, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(profile.Kind, DeviceEnrollmentProfileKinds.Individual, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "iot.deviceRegistry.enrollmentProfileKindInvalid",
                $"Enrollment profile kind '{profile.Kind}' is not supported.",
                Attributes.EnrollmentProfiles));
        }

        if (string.Equals(profile.Kind, DeviceEnrollmentProfileKinds.Individual, StringComparison.OrdinalIgnoreCase) &&
            profile.Policy.Subjects.Count != 1)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "iot.deviceRegistry.individualEnrollmentSubjectRequired",
                "Individual enrollment profiles must declare exactly one device subject.",
                Attributes.EnrollmentProfiles));
        }

        var subjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subject in profile.Policy.Subjects)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileSubjectRequired",
                    "Enrollment profile subjects cannot be empty.",
                    Attributes.EnrollmentProfiles));
            }
            else if (!subjects.Add(subject.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileSubjectDuplicate",
                    $"Enrollment profile subject '{subject}' is declared more than once.",
                    Attributes.EnrollmentProfiles));
            }
        }

        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in profile.Policy.SubjectPrefixes)
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileSubjectPrefixRequired",
                    "Enrollment profile subject prefixes cannot be empty.",
                    Attributes.EnrollmentProfiles));
            }
            else if (!prefixes.Add(prefix.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileSubjectPrefixDuplicate",
                    $"Enrollment profile subject prefix '{prefix}' is declared more than once.",
                    Attributes.EnrollmentProfiles));
            }
        }

        var claims = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in profile.Policy.RequiredClaims)
        {
            if (string.IsNullOrWhiteSpace(claim.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileRequiredClaimNameRequired",
                    "Enrollment profile required claim name is required.",
                    Attributes.EnrollmentProfiles));
            }

            if (string.IsNullOrWhiteSpace(claim.Value))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileRequiredClaimValueRequired",
                    "Enrollment profile required claim value is required.",
                    Attributes.EnrollmentProfiles));
            }

            if (!string.IsNullOrWhiteSpace(claim.Name) &&
                !claims.Add(claim.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfileRequiredClaimDuplicate",
                    $"Enrollment profile required claim '{claim.Name}' is declared more than once.",
                    Attributes.EnrollmentProfiles));
            }
        }
    }

    private static void ValidateEnrollmentProfileGrants(
        DeviceEnrollmentProfile profile,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var grants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grant in profile.PermissionGrants)
        {
            if (string.IsNullOrWhiteSpace(grant.TargetResourceId))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfilePermissionTargetRequired",
                    "Enrollment profile permission target resource ID is required.",
                    Attributes.EnrollmentProfiles));
            }

            if (string.IsNullOrWhiteSpace(grant.Permission))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfilePermissionRequired",
                    "Enrollment profile permission is required.",
                    Attributes.EnrollmentProfiles));
            }

            if (!string.IsNullOrWhiteSpace(grant.TargetResourceId) &&
                !string.IsNullOrWhiteSpace(grant.Permission) &&
                !grants.Add($"{grant.TargetResourceId.Trim()}\u001f{grant.Permission.Trim()}"))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "iot.deviceRegistry.enrollmentProfilePermissionDuplicate",
                    $"Enrollment profile permission '{grant.Permission}' for resource '{grant.TargetResourceId}' is declared more than once.",
                    Attributes.EnrollmentProfiles));
            }
        }
    }

    private static string CreateTrustedCertificateKey(ResourceCertificateReference certificate) =>
        string.Join(
            '\u001f',
            certificate.VaultResourceId.Trim(),
            certificate.Name.Trim(),
            certificate.Version?.Trim() ?? string.Empty);
}
