namespace CloudShell.ControlPlane.Providers;

public sealed class SecretsVaultResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "secretsVault";
    public static readonly ResourceTypeId ResourceTypeId = "secrets.vault";
    public const string ProviderId = "secrets-vault";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Kind = "kind";
        public static readonly ResourceAttributeId Endpoint = "endpoint";
        public static readonly ResourceAttributeId Secrets = "seed.secrets";
        public static readonly ResourceAttributeId Certificates = "seed.certificates";
        public static readonly ResourceAttributeId SecretCount = "secretCount";
        public static readonly ResourceAttributeId CertificateCount = "certificateCount";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId Inspect = "secrets.vault.inspect";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Kind] = new(
                DefaultValue: "vault",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Endpoint] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Secrets] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Secret name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["value"] = new(
                        Required: true,
                        RequiredMessage: "Secret value is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["version"] = new(ValueType: ResourceAttributeValueType.String)
                }),
                description: "Create-only secrets used to seed a new Secrets Vault resource."),
            [Attributes.Certificates] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Certificate name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["value"] = new(
                        Required: true,
                        RequiredMessage: "Certificate value is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["version"] = new(ValueType: ResourceAttributeValueType.String),
                    ["contentType"] = new(ValueType: ResourceAttributeValueType.String),
                    ["thumbprint"] = new(ValueType: ResourceAttributeValueType.String),
                    ["subject"] = new(ValueType: ResourceAttributeValueType.String),
                    ["notBefore"] = new(ValueType: ResourceAttributeValueType.String),
                    ["expires"] = new(ValueType: ResourceAttributeValueType.String),
                    ["hasPrivateKey"] = new(ValueType: ResourceAttributeValueType.Boolean)
                }),
                description: "Create-only certificates used to seed a new Secrets Vault resource."),
            [Attributes.SecretCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged),
            [Attributes.CertificateCount] = new(
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
                        endpointName: "secrets"),
                    ResourceHealthCheckDefinition.HttpLiveness(
                        "/healthz",
                        endpointName: "secrets")
                ])))
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart),
            new(Operations.Inspect)
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
        ValidateCreateOnlySecrets(changes, diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(
                    changes,
                    StripSeedSecrets(changes.ProposedState),
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
                    "Accept Secrets Vault definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize Secrets Vault resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateSecretCount(
            resource.Attributes.GetString(Attributes.SecretCount),
            diagnostics);
        ValidateCertificateCount(
            resource.Attributes.GetString(Attributes.CertificateCount),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.SecretCount, out var secretCount))
        {
            ValidateSecretCount(secretCount, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.Secrets))
        {
            ValidateSeedSecrets(state, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.Certificates))
        {
            ValidateSeedCertificates(state, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateCreateOnlySecrets(
        ResourceChangeSet changes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!changes.IsNewResource &&
            (changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Secrets) ||
             changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Certificates)))
        {
            if (changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Secrets))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.secretsSeedUpdateNotAllowed",
                    "Secrets can only be supplied when creating a Secrets Vault resource.",
                    Attributes.Secrets));
            }

            if (changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Certificates))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.certificatesSeedUpdateNotAllowed",
                    "Certificates can only be supplied when creating a Secrets Vault resource.",
                    Attributes.Certificates));
            }
        }
    }

    private static void ValidateSeedSecrets(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var secrets = state.ResourceAttributeValues.GetObject<SecretsVaultSeedSecret[]>(
            Attributes.Secrets) ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var secret in secrets)
        {
            if (string.IsNullOrWhiteSpace(secret.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedSecretNameRequired",
                    "Secret name is required.",
                    Attributes.Secrets));
            }

            if (secret.Value is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedSecretValueRequired",
                    "Secret value is required.",
                    Attributes.Secrets));
            }

            if (!string.IsNullOrWhiteSpace(secret.Name) &&
                !keys.Add(CreateSeedKey(secret.Name, secret.Version)))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedSecretDuplicate",
                    CreateDuplicateMessage("Secret", secret.Name, secret.Version),
                    Attributes.Secrets));
            }
        }
    }

    private static ResourceState StripSeedSecrets(
        ResourceState state)
    {
        if (!state.ResourceAttributeValues.ContainsKey(Attributes.Secrets) &&
            !state.ResourceAttributeValues.ContainsKey(Attributes.Certificates))
        {
            return state;
        }

        var secrets = state.ResourceAttributeValues.GetObject<SecretsVaultSeedSecret[]>(
            Attributes.Secrets) ?? [];
        var certificates = state.ResourceAttributeValues.GetObject<SecretsVaultSeedCertificate[]>(
            Attributes.Certificates) ?? [];
        var attributes = state.ResourceAttributeValues
            .Where(attribute =>
                attribute.Key != Attributes.Secrets &&
                attribute.Key != Attributes.Certificates)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        attributes[Attributes.SecretCount] = ResourceAttributeValue.Integer(secrets.Length);
        attributes[Attributes.CertificateCount] = ResourceAttributeValue.Integer(certificates.Length);

        return state with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
    }

    private static void ValidateSeedCertificates(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var certificates = state.ResourceAttributeValues.GetObject<SecretsVaultSeedCertificate[]>(
            Attributes.Certificates) ?? [];
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var certificate in certificates)
        {
            if (string.IsNullOrWhiteSpace(certificate.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedCertificateNameRequired",
                    "Certificate name is required.",
                    Attributes.Certificates));
            }

            if (certificate.Value is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedCertificateValueRequired",
                    "Certificate value is required.",
                    Attributes.Certificates));
            }

            if (!string.IsNullOrWhiteSpace(certificate.Name) &&
                !keys.Add(CreateSeedKey(certificate.Name, certificate.Version)))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "secrets.vault.seedCertificateDuplicate",
                    CreateDuplicateMessage("Certificate", certificate.Name, certificate.Version),
                    Attributes.Certificates));
            }
        }
    }

    private static string CreateSeedKey(string name, string? version) =>
        $"{name.Trim()}\u001f{(string.IsNullOrWhiteSpace(version) ? string.Empty : version.Trim())}";

    private static string CreateDuplicateMessage(
        string artifactKind,
        string name,
        string? version) =>
        string.IsNullOrWhiteSpace(version)
            ? $"{artifactKind} '{name}' is declared more than once."
            : $"{artifactKind} '{name}' version '{version}' is declared more than once.";

    private static void ValidateSecretCount(
        string? secretCount,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(secretCount) &&
            (!int.TryParse(secretCount, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "secrets.vault.secretCountInvalid",
                "Secrets Vault secret count must be a non-negative integer.",
                Attributes.SecretCount));
        }
    }

    private static void ValidateCertificateCount(
        string? certificateCount,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(certificateCount) &&
            (!int.TryParse(certificateCount, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "secrets.vault.certificateCountInvalid",
                "Secrets Vault certificate count must be a non-negative integer.",
                Attributes.CertificateCount));
        }
    }
}
