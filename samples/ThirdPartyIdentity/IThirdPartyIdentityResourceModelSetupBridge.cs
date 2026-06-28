using CloudShell.ResourceDefinitions;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.ThirdPartyIdentity;

public interface IThirdPartyIdentityResourceModelSetupBridge
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default);
}
