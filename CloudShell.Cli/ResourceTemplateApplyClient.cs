using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Client;
using CloudShell.ResourceModel;
using System.Text.Json;

namespace CloudShell.Cli;

internal static class ResourceTemplateApplyClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<ResourceTemplateApplyResult> ApplyAsync(
        Uri controlPlaneUrl,
        string templatePath,
        ResourceDefinitionApplyMode mode,
        string? bearerToken,
        CancellationToken cancellationToken)
    {
        var template = await ReadTemplateAsync(templatePath, cancellationToken);
        using var client = ControlPlaneClientFactory.Create(controlPlaneUrl, bearerToken);
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

        await using var stream = File.OpenRead(fullPath);
        var template = await JsonSerializer.DeserializeAsync<ResourceTemplate>(
            stream,
            SerializerOptions,
            cancellationToken);
        return template ?? throw new InvalidOperationException(
            $"The resource template '{fullPath}' could not be read.");
    }
}
