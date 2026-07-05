using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Client;
using CloudShell.ResourceModel;

namespace CloudShell.Cli;

internal static class ResourceTemplateApplyClient
{
    public static async Task<ResourceTemplateApplyResult> ApplyAsync(
        Uri controlPlaneUrl,
        string templatePath,
        ResourceDefinitionApplyMode mode,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        var template = await ReadTemplateAsync(templatePath, cancellationToken);
        using var client = await ControlPlaneClientFactory.CreateAsync(
            controlPlaneUrl,
            bearerToken,
            cancellationToken);
        return await client.ControlPlane.ApplyResourceTemplateAsync(
            new ResourceTemplateApplyRequest(template, mode),
            cancellationToken);
    }

    private static async Task<ResourceTemplate> ReadTemplateAsync(
        string templatePath,
        CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(templatePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"The resource template '{fullPath}' does not exist.", fullPath);
        }

        var document = await File.ReadAllTextAsync(fullPath, cancellationToken);
        return ResourceTemplateSerializer.DeserializeTemplate(
            document,
            ResourceTemplateSerializer.GetFormatFromFilePath(fullPath));
    }
}
