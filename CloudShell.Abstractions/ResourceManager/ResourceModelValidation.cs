namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceModelDiagnostic(
    string Code,
    string Message,
    string ResourceId,
    string ResourceType,
    ResourceClass ExpectedResourceClass,
    ResourceClass ActualResourceClass,
    string Source);

public sealed record ResourceModelValidationResult(
    bool Succeeded,
    ResourceClass? ResourceClass = null,
    ResourceModelDiagnostic? Diagnostic = null)
{
    public static ResourceModelValidationResult Success(ResourceClass? resourceClass = null) =>
        new(true, resourceClass);

    public static ResourceModelValidationResult Failure(ResourceModelDiagnostic diagnostic) =>
        new(false, Diagnostic: diagnostic);
}

public static class ResourceModelValidation
{
    public const string ResourceClassMismatchCode = "resourceClassMismatch";
    public const string ResourceIdentityProviderUnresolvedCode = "resourceIdentityProviderUnresolved";

    public static ResourceModelValidationResult ResolveResourceClass(
        string resourceId,
        string resourceType,
        ResourceClass expectedResourceClass,
        ResourceClass? resourceClass,
        string source)
    {
        if (resourceClass is null)
        {
            return ResourceModelValidationResult.Success(expectedResourceClass);
        }

        var result = ValidateResourceClass(
            resourceId,
            resourceType,
            expectedResourceClass,
            resourceClass.Value,
            source);

        return result.Succeeded
            ? ResourceModelValidationResult.Success(resourceClass.Value)
            : result;
    }

    public static ResourceModelValidationResult ValidateResourceClass(
        string resourceId,
        string resourceType,
        ResourceClass expectedResourceClass,
        ResourceClass actualResourceClass,
        string source)
    {
        if (expectedResourceClass == actualResourceClass)
        {
            return ResourceModelValidationResult.Success(actualResourceClass);
        }

        return ResourceModelValidationResult.Failure(
            CreateResourceClassMismatch(
                resourceId,
                resourceType,
                expectedResourceClass,
                actualResourceClass,
                source));
    }

    public static ResourceModelDiagnostic CreateResourceClassMismatch(
        string resourceId,
        string resourceType,
        ResourceClass expectedResourceClass,
        ResourceClass actualResourceClass,
        string source) =>
        new(
            ResourceClassMismatchCode,
            $"Resource '{resourceId}' uses type '{resourceType}' which requires class '{expectedResourceClass}', but {source} declares class '{actualResourceClass}'.",
            resourceId,
            resourceType,
            expectedResourceClass,
            actualResourceClass,
            source);

    public static ResourceModelDiagnostic CreateResourceIdentityProviderUnresolved(
        string resourceId,
        string resourceType,
        ResourceClass resourceClass,
        string reason,
        string source) =>
        new(
            ResourceIdentityProviderUnresolvedCode,
            $"Resource '{resourceId}' identity provider could not be resolved. {reason}",
            resourceId,
            resourceType,
            resourceClass,
            resourceClass,
            source);
}
