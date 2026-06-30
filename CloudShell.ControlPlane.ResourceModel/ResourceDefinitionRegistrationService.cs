using CloudShell.Abstractions.ControlPlane;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.ResourceModel;

public interface IResourceDefinitionRegistrationService
{
    Task<ResourceDefinitionRegistrationResult> RegisterAsync(
        IResourceDefinitionBuilder resource,
        string? resourceGroupId = null,
        ResourceGraphCommitContext? commitContext = null,
        CancellationToken cancellationToken = default);
}

public sealed class ResourceDefinitionRegistrationService(
    ResourceModelGraphDefinitionApplyService definitionApply,
    IResourceManager resourceManager,
    IServiceProvider services) : IResourceDefinitionRegistrationService
{
    public async Task<ResourceDefinitionRegistrationResult> RegisterAsync(
        IResourceDefinitionBuilder resource,
        string? resourceGroupId = null,
        ResourceGraphCommitContext? commitContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);

        var convention = services.GetService<IResourceIdConvention>() ??
            DefaultResourceIdConvention.Instance;
        var graph = new ResourceGraphBuilder(convention);
        graph.Add(resource);
        var definition = graph.BuildGraph().Resources.Single();
        var result = await definitionApply.ApplyTemplateAsync(
            graph.BuildTemplate(
                $"add-resource:{definition.Name}",
                environmentId: commitContext?.EnvironmentId),
            commitContext ?? new ResourceGraphCommitContext(),
            ResourceModelGraphDefinitionApplyOptions.CreateMissing,
            cancellationToken);

        if (result.HasErrors || !result.IsCommitted)
        {
            return new(definition.EffectiveResourceId, result, Registered: false);
        }

        await RegisterOrUpdateRegistrationAsync(
            definition,
            resourceGroupId,
            cancellationToken);

        return new(definition.EffectiveResourceId, result, Registered: true);
    }

    private async Task RegisterOrUpdateRegistrationAsync(
        ResourceDefinition definition,
        string? resourceGroupId,
        CancellationToken cancellationToken)
    {
        var normalizedResourceGroupId = NormalizeOptional(resourceGroupId);
        var dependencyIds = definition.StartupDependencyIds;
        var registrations = await resourceManager.ListResourceRegistrationsAsync(cancellationToken);
        if (registrations.Any(registration => string.Equals(
                registration.ResourceId,
                definition.EffectiveResourceId,
                StringComparison.OrdinalIgnoreCase)))
        {
            await resourceManager.AssignResourceGroupAsync(
                new AssignResourceGroupCommand(
                    definition.EffectiveResourceId,
                    normalizedResourceGroupId,
                    dependencyIds),
                cancellationToken);
            return;
        }

        await resourceManager.RegisterResourceAsync(
            new RegisterResourceCommand(
                ResourceModelResourceProvider.DefaultProviderId,
                definition.EffectiveResourceId,
                normalizedResourceGroupId,
                dependencyIds),
            cancellationToken);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record ResourceDefinitionRegistrationResult(
    string ResourceId,
    ResourceModelGraphDefinitionApplyResult ApplyResult,
    bool Registered);
