using CloudShell.ResourceModel;
using GraphResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ThirdPartyIdentity;

public interface IThirdPartyIdentityResourceModelSetupBridge
{
    ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> SetupAsync(
        GraphResource resource,
        CancellationToken cancellationToken = default);
}
