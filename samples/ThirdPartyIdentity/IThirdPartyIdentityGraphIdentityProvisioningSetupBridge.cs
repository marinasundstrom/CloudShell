using CloudShell.ResourceDefinitions;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ThirdPartyIdentity;

public interface IThirdPartyIdentityGraphIdentityProvisioningSetupBridge
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default);
}
