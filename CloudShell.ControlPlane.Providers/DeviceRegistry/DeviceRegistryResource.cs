namespace CloudShell.ControlPlane.Providers;

public sealed class DeviceRegistryResource(
    Resource resource) : IResourceProjection
{
    public Resource Resource { get; } = resource;

    public string? Kind =>
        Resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.Kind);

    public string? Endpoint =>
        Resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.Endpoint);

    public int EnrolledDeviceCount =>
        int.TryParse(
            Resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.EnrolledDeviceCount),
            out var count)
                ? count
                : 0;

    public IReadOnlyList<ResourceCertificateReference> TrustedCertificates =>
        Resource.Attributes.GetObject<ResourceCertificateReference[]>(
            DeviceRegistryResourceTypeProvider.Attributes.TrustedCertificates) ?? [];

    public IReadOnlyList<string> AllowedSubjectPrefixes =>
        Resource.Attributes.GetObject<string[]>(
            DeviceRegistryResourceTypeProvider.Attributes.AllowedSubjectPrefixes) ?? [];

    public IReadOnlyList<DeviceEnrollmentRequiredClaim> RequiredClaims =>
        Resource.Attributes.GetObject<DeviceEnrollmentRequiredClaim[]>(
            DeviceRegistryResourceTypeProvider.Attributes.RequiredClaims) ?? [];

    public IReadOnlyList<DeviceEnrollmentProfile> EnrollmentProfiles =>
        Resource.Attributes.GetObject<DeviceEnrollmentProfile[]>(
            DeviceRegistryResourceTypeProvider.Attributes.EnrollmentProfiles) ?? [];

    public int? HeartbeatStaleAfterSeconds =>
        int.TryParse(
            Resource.Attributes.GetString(DeviceRegistryResourceTypeProvider.Attributes.HeartbeatStaleAfterSeconds),
            out var seconds)
                ? seconds
                : null;
}

public sealed class DeviceRegistryResourceProjectionProvider : IResourceProjectionProvider
{
    public ResourceTypeId TypeId => DeviceRegistryResourceTypeProvider.ResourceTypeId;

    public bool CanProject(Resource resource) =>
        resource.Type.TypeId == DeviceRegistryResourceTypeProvider.ResourceTypeId;

    public ValueTask<IResourceProjection> ProjectAsync(
        Resource resource,
        ResourceProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceProjection>(
            new DeviceRegistryResource(resource));
}
