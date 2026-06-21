using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceActionReadinessDiagnostics
{
    public static IReadOnlyList<ResourceDiagnosticView> GetDiagnostics(
        Resource resource,
        ResourceOperationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(capabilities);

        var action = GetReadinessAction(resource);
        if (action is null ||
            capabilities.CanExecuteAction(action.Id))
        {
            return [];
        }

        var reason = capabilities.GetActionUnavailableReason(action.Id);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return [];
        }

        return
        [
            new ResourceDiagnosticView(
                ResourceSignalSeverity.Warning,
                $"{action.DisplayName} readiness",
                FormatReadinessReason(resource, reason))
        ];
    }

    private static string FormatReadinessReason(
        Resource resource,
        string reason)
    {
        var trimmed = reason.Trim();
        foreach (var label in GetCurrentResourceLabels(resource))
        {
            foreach (var prefix in GetCurrentResourceReasonPrefixes(label))
            {
                if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var contextualReason = trimmed[prefix.Length..].TrimStart();
                return string.IsNullOrWhiteSpace(contextualReason)
                    ? trimmed
                    : Capitalize(contextualReason);
            }
        }

        return trimmed;
    }

    private static IEnumerable<string> GetCurrentResourceReasonPrefixes(string label)
    {
        yield return $"Resource '{label}' ";
        yield return $"Application resource '{label}' ";
        yield return $"Project-backed application resource '{label}' ";
        yield return $"Executable application resource '{label}' ";
        yield return $"Container app resource '{label}' ";
        yield return $"SQL Server resource '{label}' ";
    }

    private static IEnumerable<string> GetCurrentResourceLabels(Resource resource)
    {
        if (!string.IsNullOrWhiteSpace(resource.Name))
        {
            yield return resource.Name;
        }

        if (!string.IsNullOrWhiteSpace(resource.DisplayName))
        {
            yield return resource.DisplayName;
        }

        if (!string.IsNullOrWhiteSpace(resource.EffectiveDisplayName))
        {
            yield return resource.EffectiveDisplayName;
        }

        if (!string.IsNullOrWhiteSpace(resource.Id))
        {
            yield return resource.Id;
        }

        var resourceName = ResourceDisplayLabels.GetName(resource);
        if (!string.IsNullOrWhiteSpace(resourceName))
        {
            yield return resourceName;
        }
    }

    private static string Capitalize(string value) =>
        value.Length == 0 || char.IsUpper(value[0])
            ? value
            : string.Create(
                value.Length,
                value,
                static (span, source) =>
                {
                    span[0] = char.ToUpperInvariant(source[0]);
                    source.AsSpan(1).CopyTo(span[1..]);
                });

    private static ResourceAction? GetReadinessAction(Resource resource)
    {
        var preferredKind = resource.State is ResourceState.Running or ResourceState.Starting or ResourceState.Degraded
            ? ResourceActionKind.Restart
            : ResourceActionKind.Start;

        return resource.ResourceActions.FirstOrDefault(action => action.Kind == preferredKind) ??
            resource.ResourceActions.FirstOrDefault(action =>
                action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart);
    }
}
