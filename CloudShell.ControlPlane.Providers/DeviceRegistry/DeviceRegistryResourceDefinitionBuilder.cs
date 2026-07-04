namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<DeviceRegistryResourceDefinitionBuilder>(name)
{
    private readonly List<ResourceCertificateReference> _trustedCertificates = [];

    protected override ResourceTypeId TypeId =>
        DeviceRegistryResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        DeviceRegistryResourceTypeProvider.ProviderId;

    public DeviceRegistryResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public DeviceRegistryResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(DeviceRegistryResourceTypeProvider.Attributes.Endpoint, endpoint);

    public DeviceRegistryResourceDefinitionBuilder TrustCertificate(
        ResourceCertificateReference certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        _trustedCertificates.Add(certificate);
        SetObjectAttribute(
            DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates,
            _trustedCertificates.ToArray());

        if (!string.IsNullOrWhiteSpace(certificate.VaultResourceId))
        {
            DependsOn(
                certificate.VaultResourceId,
                SecretsVaultResourceTypeProvider.ResourceTypeId,
                SecretsVaultResourceTypeProvider.ProviderId);
        }

        return this;
    }

    public DeviceRegistryResourceDefinitionBuilder UseEnrollmentPolicy(
        Action<DeviceEnrollmentPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var policy = new DeviceEnrollmentPolicyBuilder();
        configure(policy);

        if (policy.SubjectPrefixes.Count > 0)
        {
            SetObjectAttribute(
                DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes,
                policy.SubjectPrefixes.ToArray());
        }

        if (policy.RequiredClaims.Count > 0)
        {
            SetObjectAttribute(
                DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims,
                policy.RequiredClaims.ToArray());
        }

        return this;
    }
}

public sealed class DeviceEnrollmentPolicyBuilder
{
    private readonly List<string> _subjectPrefixes = [];
    private readonly List<DeviceEnrollmentRequiredClaim> _requiredClaims = [];

    public IReadOnlyList<string> SubjectPrefixes => _subjectPrefixes;

    public IReadOnlyList<DeviceEnrollmentRequiredClaim> RequiredClaims => _requiredClaims;

    public DeviceEnrollmentPolicyBuilder AllowSubjectPrefix(string subjectPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectPrefix);

        _subjectPrefixes.Add(subjectPrefix.Trim());
        return this;
    }

    public DeviceEnrollmentPolicyBuilder RequireClaim(
        string name,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _requiredClaims.Add(new(name.Trim(), value.Trim()));
        return this;
    }
}

public sealed record DeviceEnrollmentRequiredClaim(
    string Name,
    string Value);

public static class DeviceRegistryResourceDefinitionBuilderExtensions
{
    public static DeviceRegistryResourceDefinitionBuilder AddDeviceRegistry(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new DeviceRegistryResourceDefinitionBuilder(name)
            .WithRuntimeMonitoring();
        graph.Add(builder);
        return builder;
    }
}
