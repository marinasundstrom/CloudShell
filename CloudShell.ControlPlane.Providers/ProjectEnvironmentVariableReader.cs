namespace CloudShell.ControlPlane.Providers;

internal static class ProjectEnvironmentVariableReader
{
    public static IReadOnlyDictionary<string, AspNetCoreProjectEnvironmentVariableValue> ReadAspNetCoreProject(
        ResourceAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = attributes.GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables);
        if (values is { Count: > 0 })
        {
            return values;
        }

        return ReadFlattened(attributes, AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables)
            .ToDictionary(
                variable => variable.Key,
                variable => new AspNetCoreProjectEnvironmentVariableValue(
                    variable.Value.Value,
                    variable.Value.ConfigurationEntryRef,
                    variable.Value.SecretRef),
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, JavaScriptAppEnvironmentVariableValue> ReadJavaScriptApp(
        ResourceAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = attributes.GetObject<Dictionary<string, JavaScriptAppEnvironmentVariableValue>>(
            JavaScriptAppResourceTypeProvider.Attributes.EnvironmentVariables);
        if (values is { Count: > 0 })
        {
            return values;
        }

        return ReadFlattened(attributes, JavaScriptAppResourceTypeProvider.Attributes.EnvironmentVariables)
            .ToDictionary(
                variable => variable.Key,
                variable => new JavaScriptAppEnvironmentVariableValue(
                    variable.Value.Value,
                    variable.Value.ConfigurationEntryRef,
                    variable.Value.SecretRef),
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, JavaAppEnvironmentVariableValue> ReadJavaApp(
        ResourceAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = attributes.GetObject<Dictionary<string, JavaAppEnvironmentVariableValue>>(
            JavaAppResourceTypeProvider.Attributes.EnvironmentVariables);
        if (values is { Count: > 0 })
        {
            return values;
        }

        return ReadFlattened(attributes, JavaAppResourceTypeProvider.Attributes.EnvironmentVariables)
            .ToDictionary(
                variable => variable.Key,
                variable => new JavaAppEnvironmentVariableValue(
                    variable.Value.Value,
                    variable.Value.ConfigurationEntryRef,
                    variable.Value.SecretRef),
                StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyDictionary<string, GoAppEnvironmentVariableValue> ReadGoApp(
        ResourceAttributeSet attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        var values = attributes.GetObject<Dictionary<string, GoAppEnvironmentVariableValue>>(
            GoAppResourceTypeProvider.Attributes.EnvironmentVariables);
        if (values is { Count: > 0 })
        {
            return values;
        }

        return ReadFlattened(attributes, GoAppResourceTypeProvider.Attributes.EnvironmentVariables)
            .ToDictionary(
                variable => variable.Key,
                variable => new GoAppEnvironmentVariableValue(
                    variable.Value.Value,
                    variable.Value.ConfigurationEntryRef,
                    variable.Value.SecretRef),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, EnvironmentVariableParts> ReadFlattened(
        ResourceAttributeSet attributes,
        ResourceAttributeId root)
    {
        var prefix = root + ".";
        var variables = new Dictionary<string, EnvironmentVariableParts>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in attributes)
        {
            var name = attribute.Name.ToString();
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var path = name[prefix.Length..];
            if (TryReadPath(path, ".value", out var variableName, out _) &&
                attribute.Value is { Length: > 0 } literalValue)
            {
                variables[variableName] = GetParts(variables, variableName) with { Value = literalValue };
                continue;
            }

            if (TryReadPath(path, ".configurationEntryRef.", out variableName, out var configurationProperty) &&
                attribute.Value is { Length: > 0 } configurationValue)
            {
                var parts = GetParts(variables, variableName);
                var reference = parts.ConfigurationEntryRef ?? new ResourceConfigurationEntryReference(
                    string.Empty,
                    string.Empty);
                reference = configurationProperty switch
                {
                    "storeResourceId" => reference with { StoreResourceId = configurationValue },
                    "name" => reference with { Name = configurationValue },
                    "version" => reference with { Version = configurationValue },
                    _ => reference
                };
                variables[variableName] = parts with { ConfigurationEntryRef = reference };
                continue;
            }

            if (TryReadPath(path, ".secretRef.", out variableName, out var secretProperty) &&
                attribute.Value is { Length: > 0 } secretValue)
            {
                var parts = GetParts(variables, variableName);
                var reference = parts.SecretRef ?? new ResourceSecretReference(
                    string.Empty,
                    string.Empty);
                reference = secretProperty switch
                {
                    "vaultResourceId" => reference with { VaultResourceId = secretValue },
                    "name" => reference with { Name = secretValue },
                    "version" => reference with { Version = secretValue },
                    _ => reference
                };
                variables[variableName] = parts with { SecretRef = reference };
            }
        }

        return variables;
    }

    private static EnvironmentVariableParts GetParts(
        Dictionary<string, EnvironmentVariableParts> variables,
        string name) =>
        variables.GetValueOrDefault(name) ?? new EnvironmentVariableParts();

    private static bool TryReadPath(
        string path,
        string marker,
        out string variableName,
        out string propertyName)
    {
        var markerIndex = path.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            variableName = string.Empty;
            propertyName = string.Empty;
            return false;
        }

        variableName = path[..markerIndex];
        propertyName = path[(markerIndex + marker.Length)..];
        return !string.IsNullOrWhiteSpace(variableName) &&
            (string.Equals(marker, ".value", StringComparison.Ordinal) ||
                !string.IsNullOrWhiteSpace(propertyName));
    }

    private sealed record EnvironmentVariableParts(
        string? Value = null,
        ResourceConfigurationEntryReference? ConfigurationEntryRef = null,
        ResourceSecretReference? SecretRef = null);
}
