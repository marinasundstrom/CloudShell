using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private async Task<string?> GetSettingResolutionUnavailableReasonAsync(
        ApplicationResourceDefinition application,
        string? resourceGroupId,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await ResolveConfiguredEnvironmentVariablesAsync(
                application,
                resourceGroupId,
                cancellationToken);
            return null;
        }
        catch (ResourceSettingResolutionException exception)
        {
            return exception.Message;
        }
    }
}
