using System.Globalization;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Networking;

public sealed class MacOSHostNetworkProvider(
    LocalHostNetworkProvisioner provisioner,
    HostOperatingSystem hostOperatingSystem) :
    IResourceProvider,
    IProgrammaticResourceDeclarationProvider,
    IResourceAutoStartPolicyProvider
{
    public const string ProviderId = "cloudshell.hostNetworking";
    public const string ResourceId = "networking:host-macos";
    public const string ResourceType = "cloudshell.hostNetworking.macos";

    public string Id => ProviderId;

    public string DisplayName => "CloudShell Host Networking";

    public IReadOnlyList<Resource> GetResources() =>
        IsSupported ? [CreateResource(provisioner.ProvisionedMappingCount)] : [];

    public bool CanApplyDeclaration(ResourceDeclaration declaration) =>
        string.Equals(declaration.ProviderId, Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(declaration.ResourceId, ResourceId, StringComparison.OrdinalIgnoreCase);

    public Task ApplyDeclarationAsync(
        ResourceDeclaration declaration,
        IResourceRegistrationStore registrations,
        CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            throw new InvalidOperationException(
                "The macOS host networking provider is unavailable because this host is not running macOS.");
        }

        return registrations.RegisterAsync(
            Id,
            ResourceId,
            string.IsNullOrWhiteSpace(declaration.ResourceGroupId) ? null : declaration.ResourceGroupId.Trim(),
            declaration.DependsOn,
            cancellationToken);
    }

    public bool CanEvaluateAutoStartPolicy(ResourceDeclaration declaration) =>
        CanApplyDeclaration(declaration);

    public ResourceAutoStartPolicy GetAutoStartPolicy(ResourceDeclaration declaration) =>
        new(
            StartOnControlPlaneStart: false,
            StartAsDependency: true,
            StartAfterCreate: false);

    private static Resource CreateResource(int provisionedMappingCount) =>
        new(
            ResourceId,
            "macOS Host Networking",
            "Host Networking",
            "CloudShell",
            "host",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: ResourceType,
            ResourceClass: ResourceClass.Infrastructure,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InfrastructureKind] = "hostNetworking",
                [ResourceAttributeNames.NetworkHostReadiness] = "ready",
                ["host.os"] = "macos",
                ["networking.mode"] = "localProxy",
                [ResourceAttributeNames.NetworkProvisionedMappingCount] =
                    provisionedMappingCount.ToString(CultureInfo.InvariantCulture)
            },
            Capabilities:
            [
                new(ResourceCapabilityIds.NetworkingProvider),
                new(ResourceCapabilityIds.NetworkingEndpointMapper),
                new(ResourceCapabilityIds.NetworkingGateway),
                new(ResourceCapabilityIds.NetworkingIngress),
                new(ResourceCapabilityIds.NetworkingHostNetwork)
            ]);

    private bool IsSupported => hostOperatingSystem.IsMacOS;
}
