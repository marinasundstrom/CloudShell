using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

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
        var resourceNamesById = resources.ToDictionary(
            resource => resource.Id,
            resource => resource.Name,
            StringComparer.OrdinalIgnoreCase);

        var exportedResources = new List<ResourceTemplateDefinition>();
        var diagnostics = new List<ResourceTemplateDiagnostic>();

        foreach (var resource in resources)
        {
            var registration = registrations.GetRegistration(resource.Id);
            if (registration is null)
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    resource.Name,
                    "Resource registration is unavailable."));
                continue;
            }

            var provider = resourceManager.Providers.FirstOrDefault(item =>
                string.Equals(item.Id, registration.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is not IResourceTemplateProvider templateProvider ||
                !templateProvider.CanExport(resource))
            {
                diagnostics.Add(ResourceTemplateDiagnostic.Warning(
                    resource.Name,
                    "Provider does not support template export for this resource."));
                continue;
            }

            var exported = await templateProvider.ExportAsync(
                resource,
                new ResourceTemplateExportContext(registration, group),
                cancellationToken);
            exportedResources.Add(exported with
            {
                DependsOn = exported.DependsOn
                    .Select(dependency => resourceNamesById.GetValueOrDefault(dependency) ?? dependency)
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
        if (!string.Equals(template.Kind, "resourceGroup", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only resource group templates can be imported.");
        }

        if (!string.Equals(template.TemplateVersion, "1.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Template version '{template.TemplateVersion}' is not supported.");
        }

        var group = await resourceGroups.CreateAsync(
            template.Name,
            template.Description ?? string.Empty,
            cancellationToken);

        var diagnostics = new List<ResourceTemplateDiagnostic>();
        var importedResources = new List<ResourceTemplateImportResult>();

        foreach (var resourceTemplate in OrderByDependencies(template.Resources, diagnostics))
        {
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
                importedResources.Add(await templateProvider.ImportAsync(
                    resourceTemplate,
                    new ResourceTemplateImportContext(group.Id, registrations),
                    cancellationToken));
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
                resolved.Add(resource.Name);
                pending.Remove(resource);
            }
        }

        return ordered;
    }
}

public sealed record ResourceGroupTemplateExportResult(
    ResourceGroupTemplate Template,
    IReadOnlyList<ResourceTemplateDiagnostic> Diagnostics);

public sealed record ResourceGroupTemplateImportResult(
    ResourceGroup ResourceGroup,
    IReadOnlyList<ResourceTemplateImportResult> ImportedResources,
    IReadOnlyList<ResourceTemplateDiagnostic> Diagnostics);

public sealed record ResourceTemplateDiagnostic(
    string Severity,
    string ResourceName,
    string Message)
{
    public static ResourceTemplateDiagnostic Warning(string resourceName, string message) =>
        new("Warning", resourceName, message);

    public static ResourceTemplateDiagnostic Error(string resourceName, string message) =>
        new("Error", resourceName, message);
}
