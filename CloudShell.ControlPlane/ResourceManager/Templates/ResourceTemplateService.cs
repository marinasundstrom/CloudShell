using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Templates;

public sealed class ResourceTemplateService(
    IResourceManagerStore resourceManager,
    IResourceGroupStore resourceGroups,
    IResourceRegistrationStore registrations)
{
    public async Task<ResourceGroupTemplateExportResult> ExportGroupAsync(
        string resourceGroupId,
        CancellationToken cancellationToken = default)
    {
        var group = resourceManager.GetResourceGroups()
            .FirstOrDefault(item => string.Equals(item.Id, resourceGroupId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected resource group could not be found.");

        var resources = resourceManager.GetResources()
            .Where(resource => group.ResourceIds.Contains(resource.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(resource => resource.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resourceTemplateKeysById = resources.ToDictionary(
            resource => resource.Id,
            resource => resource.Id,
            StringComparer.OrdinalIgnoreCase);

        var exportedResources = new List<ResourceTemplateDefinition>();
        var diagnostics = new List<ResourceTemplateDiagnostic>();

        foreach (var resource in resources)
        {
            var registration = registrations.GetRegistration(resource.Id);
            if (registration is null)
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    resource.EffectiveDisplayName,
                    "Resource registration is unavailable."));
                continue;
            }

            var provider = resourceManager.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is not IResourceTemplateProvider templateProvider ||
                !templateProvider.CanExport(resource))
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    resource.EffectiveDisplayName,
                    "Provider does not support template export for this resource."));
                continue;
            }

            var exported = await templateProvider.ExportAsync(
                resource,
                new ResourceTemplateExportContext(registration, group),
                cancellationToken);
            exportedResources.Add(exported with
            {
                ResourceId = string.IsNullOrWhiteSpace(exported.ResourceId)
                    ? resource.Id
                    : exported.ResourceId.Trim(),
                DependsOn = exported.DependsOn
                    .Select(dependency => resourceTemplateKeysById.GetValueOrDefault(dependency) ?? dependency)
                    .ToArray()
            });
        }

        var template = new ResourceGroupTemplate(
            "1.0",
            "resourceGroup",
            group.Name,
            group.Description,
            exportedResources);

        return new ResourceGroupTemplateExportResult(template, diagnostics);
    }

    public async Task<ResourceGroupTemplateImportResult> ImportGroupAsync(
        ResourceGroupTemplate template,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceTemplateDiagnostic>();

        if (!string.Equals(template.Kind, "resourceGroup", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ResourceTemplateDiagnostic.Error(
                template.Name,
                "Only resource group templates can be imported."));
            return new ResourceGroupTemplateImportResult(null, [], diagnostics);
        }

        if (!string.Equals(template.TemplateVersion, "1.0", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ResourceTemplateDiagnostic.Error(
                template.Name,
                $"Template version '{template.TemplateVersion}' is not supported."));
            return new ResourceGroupTemplateImportResult(null, [], diagnostics);
        }

        var group = await resourceGroups.CreateAsync(
            template.Name,
            template.Description ?? string.Empty,
            cancellationToken);

        var importedResources = new List<ResourceTemplateImportResult>();
        var importedResourceIdsByTemplateKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceTemplate in OrderByDependencies(template.Resources, diagnostics))
        {
            var explicitResourceId = GetExplicitResourceId(resourceTemplate);
            if (explicitResourceId is not null &&
                IsResourceIdInUse(explicitResourceId))
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Error(
                    resourceTemplate.Name,
                    $"Resource id '{explicitResourceId}' is already in use."));
                continue;
            }

            var provider = resourceManager.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, resourceTemplate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is not IResourceTemplateProvider templateProvider ||
                !templateProvider.CanImport(resourceTemplate))
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    resourceTemplate.Name,
                    "Provider does not support template import for this resource."));
                continue;
            }

            try
            {
                var dependsOn = resourceTemplate.DependsOn
                    .Select(dependency => importedResourceIdsByTemplateKey.GetValueOrDefault(dependency) ?? dependency)
                    .ToArray();
                var imported = await templateProvider.ImportAsync(
                    resourceTemplate,
                    new ResourceTemplateImportContext(group.Id, registrations, dependsOn),
                    cancellationToken);

                await registrations.SetDependenciesAsync(
                    imported.ResourceId,
                    dependsOn,
                    cancellationToken);

                importedResources.Add(imported);
                importedResourceIdsByTemplateKey[GetTemplateKey(resourceTemplate)] = imported.ResourceId;
                importedResourceIdsByTemplateKey[resourceTemplate.Name] = imported.ResourceId;
            }
            catch (Exception exception)
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Error(resourceTemplate.Name, exception.Message));
            }
        }

        return new ResourceGroupTemplateImportResult(group, importedResources, diagnostics);
    }

    private static IReadOnlyList<ResourceTemplateDefinition> OrderByDependencies(
        IReadOnlyList<ResourceTemplateDefinition> resources,
        List<ResourceTemplateDiagnostic> diagnostics)
    {
        var pending = resources.ToList();
        var ordered = new List<ResourceTemplateDefinition>();
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(resource => resource.DependsOn.All(resolved.Contains))
                .ToArray();

            if (ready.Length == 0)
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    "Dependencies",
                    "Some resource dependencies could not be resolved; remaining resources will import in template order."));
                ordered.AddRange(pending);
                break;
            }

            foreach (var resource in ready)
            {
                ordered.Add(resource);
                resolved.Add(GetTemplateKey(resource));
                resolved.Add(resource.Name);
                pending.Remove(resource);
            }
        }

        return ordered;
    }

    private static string GetTemplateKey(ResourceTemplateDefinition resource) =>
        string.IsNullOrWhiteSpace(resource.ResourceId)
            ? resource.Name
            : resource.ResourceId.Trim();

    private static string? GetExplicitResourceId(ResourceTemplateDefinition resource) =>
        string.IsNullOrWhiteSpace(resource.ResourceId)
            ? null
            : resource.ResourceId.Trim();

    private bool IsResourceIdInUse(string resourceId) =>
        resourceManager.GetAvailableResources().Any(resource =>
            string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase)) ||
        registrations.GetRegistration(resourceId) is not null;
}
