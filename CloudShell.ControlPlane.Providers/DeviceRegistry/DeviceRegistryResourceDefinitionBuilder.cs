namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<DeviceRegistryResourceDefinitionBuilder>(name)
{
    private readonly List<ResourceCertificateReference> _trustedCertificates = [];
    private readonly List<DeviceEnrollmentProfile> _enrollmentProfiles = [];

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

    public DeviceRegistryResourceDefinitionBuilder UseEnrollmentProfile(
        Action<DeviceEnrollmentProfileBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var profile = new DeviceEnrollmentProfileBuilder();
        configure(profile);
        var built = profile.Build();
        _enrollmentProfiles.Add(built);
        SetObjectAttribute(
            DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles,
            _enrollmentProfiles.ToArray());

        if (_enrollmentProfiles.Count == 1 &&
            built.Policy is { } policy)
        {
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
        }

        return this;
    }
}

public class DeviceEnrollmentPolicyBuilder
{
    protected readonly List<string> SubjectValues = [];
    protected readonly List<string> SubjectPrefixValues = [];
    protected readonly List<DeviceEnrollmentRequiredClaim> RequiredClaimValues = [];

    public IReadOnlyList<string> Subjects => SubjectValues;

    public IReadOnlyList<string> SubjectPrefixes => SubjectPrefixValues;

    public IReadOnlyList<DeviceEnrollmentRequiredClaim> RequiredClaims => RequiredClaimValues;

    public DeviceEnrollmentPolicyBuilder AllowSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);

        SubjectValues.Add(subject.Trim());
        return this;
    }

    public DeviceEnrollmentPolicyBuilder AllowSubjectPrefix(string subjectPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectPrefix);

        SubjectPrefixValues.Add(subjectPrefix.Trim());
        return this;
    }

    public DeviceEnrollmentPolicyBuilder RequireClaim(
        string name,
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        RequiredClaimValues.Add(new(name.Trim(), value.Trim()));
        return this;
    }

    internal DeviceEnrollmentPolicy BuildPolicy() =>
        new()
        {
            Subjects = Subjects.ToArray(),
            SubjectPrefixes = SubjectPrefixes.ToArray(),
            RequiredClaims = RequiredClaims.ToArray()
        };
}

public sealed class DeviceEnrollmentProfileBuilder : DeviceEnrollmentPolicyBuilder
{
    private readonly List<DeviceEnrollmentPermissionGrant> _permissionGrants = [];
    private string _name = "default";
    private string _kind = DeviceEnrollmentProfileKinds.Group;

    public IReadOnlyList<DeviceEnrollmentPermissionGrant> PermissionGrants => _permissionGrants;

    public DeviceEnrollmentProfileBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _name = name.Trim();
        return this;
    }

    public DeviceEnrollmentProfileBuilder AsGroupEnrollment()
    {
        _kind = DeviceEnrollmentProfileKinds.Group;
        return this;
    }

    public DeviceEnrollmentProfileBuilder AsIndividualEnrollment(string subject)
    {
        _kind = DeviceEnrollmentProfileKinds.Individual;
        AllowSubject(subject);
        return this;
    }

    public new DeviceEnrollmentProfileBuilder AllowSubject(string subject)
    {
        base.AllowSubject(subject);
        return this;
    }

    public new DeviceEnrollmentProfileBuilder AllowSubjectPrefix(string subjectPrefix)
    {
        base.AllowSubjectPrefix(subjectPrefix);
        return this;
    }

    public new DeviceEnrollmentProfileBuilder RequireClaim(
        string name,
        string value)
    {
        base.RequireClaim(name, value);
        return this;
    }

    public DeviceEnrollmentProfileBuilder GrantAccess(
        IResourceDefinitionBuilder target,
        string permission)
    {
        ArgumentNullException.ThrowIfNull(target);

        return GrantAccess(target.EffectiveResourceId, permission);
    }

    public DeviceEnrollmentProfileBuilder GrantAccess(
        string targetResourceId,
        string permission)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        _permissionGrants.Add(new(targetResourceId.Trim(), permission.Trim()));
        return this;
    }

    public DeviceEnrollmentProfile Build() =>
        new()
        {
            Name = _name,
            Kind = _kind,
            Policy = BuildPolicy(),
            PermissionGrants = PermissionGrants.ToArray()
        };
}

public sealed record DeviceEnrollmentProfile
{
    public string Name { get; init; } = "default";

    public string Kind { get; init; } = DeviceEnrollmentProfileKinds.Group;

    public DeviceEnrollmentPolicy Policy { get; init; } = new();

    public IReadOnlyList<DeviceEnrollmentPermissionGrant> PermissionGrants { get; init; } = [];
}

public static class DeviceEnrollmentProfileKinds
{
    public const string Individual = "individual";
    public const string Group = "group";
}

public sealed record DeviceEnrollmentPolicy
{
    public IReadOnlyList<string> Subjects { get; init; } = [];

    public IReadOnlyList<string> SubjectPrefixes { get; init; } = [];

    public IReadOnlyList<DeviceEnrollmentRequiredClaim> RequiredClaims { get; init; } = [];
}

public sealed record DeviceEnrollmentRequiredClaim(
    string Name,
    string Value);

public sealed record DeviceEnrollmentPermissionGrant(
    string TargetResourceId,
    string Permission);

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
