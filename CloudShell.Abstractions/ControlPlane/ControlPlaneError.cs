namespace CloudShell.Abstractions.ControlPlane;

public sealed record ControlPlaneError(
    string Code,
    string Message)
{
    public static ControlPlaneError InvalidRequest(string message) =>
        new(ControlPlaneErrorCodes.InvalidRequest, message);

    public static ControlPlaneError ResourceProviderNotFound(string providerId) =>
        new(
            ControlPlaneErrorCodes.ResourceProviderNotFound,
            $"Resource provider '{providerId}' is not registered.");

    public static ControlPlaneError ResourceProviderCannotCreate(string providerId, string resourceType) =>
        new(
            ControlPlaneErrorCodes.ResourceProviderCannotCreate,
            $"Resource provider '{providerId}' cannot create resource type '{resourceType}'.");

    public static ControlPlaneError ResourceClassMismatch(string message) =>
        new(ControlPlaneErrorCodes.ResourceClassMismatch, message);

    public static ControlPlaneError ResourceNotAvailable(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceNotAvailable,
            $"Resource '{resourceId}' is not available.");

    public static ControlPlaneError ResourceNotRegistered(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceNotRegistered,
            $"Resource '{resourceId}' is not registered.");

    public static ControlPlaneError ResourceActionNotFound(string resourceId, string actionId) =>
        new(
            ControlPlaneErrorCodes.ResourceActionNotFound,
            $"Resource '{resourceId}' does not expose action '{actionId}'.");

    public static ControlPlaneError ResourceActionUnavailable(string message) =>
        new(ControlPlaneErrorCodes.ResourceActionUnavailable, message);

    public static ControlPlaneError ResourceActionUnsupported(string resourceName) =>
        new(
            ControlPlaneErrorCodes.ResourceActionUnsupported,
            $"Resource '{resourceName}' does not support actions.");

    public static ControlPlaneError ResourceActionUnsupported(
        string resourceName,
        string providerName,
        string actionName) =>
        new(
            ControlPlaneErrorCodes.ResourceActionUnsupported,
            $"Provider '{providerName}' does not support action '{actionName}' for resource '{resourceName}'.");

    public static ControlPlaneError ResourceDeleteUnsupported(string resourceName) =>
        new(
            ControlPlaneErrorCodes.ResourceDeleteUnsupported,
            $"Resource '{resourceName}' does not support delete.");

    public static ControlPlaneError ResourceImageUpdateUnsupported(string resourceName) =>
        new(
            ControlPlaneErrorCodes.ResourceImageUpdateUnsupported,
            $"Resource '{resourceName}' does not support image updates.");

    public static ControlPlaneError ResourceImageUpdateUnavailable(string message) =>
        new(ControlPlaneErrorCodes.ResourceImageUpdateUnavailable, message);

    public static ControlPlaneError ResourceReplicasUpdateUnsupported(string resourceName) =>
        new(
            ControlPlaneErrorCodes.ResourceReplicasUpdateUnsupported,
            $"Resource '{resourceName}' does not support replica updates.");

    public static ControlPlaneError ResourceReplicasUpdateUnavailable(string message) =>
        new(ControlPlaneErrorCodes.ResourceReplicasUpdateUnavailable, message);

    public static ControlPlaneError DependentResourcesRunning(string message) =>
        new(ControlPlaneErrorCodes.DependentResourcesRunning, message);

    public static ControlPlaneError DependencyAutoStartFailed(string message) =>
        new(ControlPlaneErrorCodes.DependencyAutoStartFailed, message);

    public static ControlPlaneError ResourceGroupNotFound(string resourceGroupId) =>
        new(
            ControlPlaneErrorCodes.ResourceGroupNotFound,
            $"Resource group '{resourceGroupId}' could not be found.");

    public static ControlPlaneError ResourceSelfDependency(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceSelfDependency,
            $"Resource '{resourceId}' cannot depend on itself.");

    public static ControlPlaneError FeatureDisabled(string message) =>
        new(ControlPlaneErrorCodes.FeatureDisabled, message);
}

public static class ControlPlaneErrorCodes
{
    public const string InvalidRequest = "invalidRequest";
    public const string ResourceProviderNotFound = "resourceProviderNotFound";
    public const string ResourceProviderCannotCreate = "resourceProviderCannotCreate";
    public const string ResourceClassMismatch = "resourceClassMismatch";
    public const string ResourceNotAvailable = "resourceNotAvailable";
    public const string ResourceNotRegistered = "resourceNotRegistered";
    public const string ResourceActionNotFound = "resourceActionNotFound";
    public const string ResourceActionUnavailable = "resourceActionUnavailable";
    public const string ResourceActionUnsupported = "resourceActionUnsupported";
    public const string ResourceDeleteUnsupported = "resourceDeleteUnsupported";
    public const string ResourceImageUpdateUnsupported = "resourceImageUpdateUnsupported";
    public const string ResourceImageUpdateUnavailable = "resourceImageUpdateUnavailable";
    public const string ResourceReplicasUpdateUnsupported = "resourceReplicasUpdateUnsupported";
    public const string ResourceReplicasUpdateUnavailable = "resourceReplicasUpdateUnavailable";
    public const string ResourceGroupNotFound = "resourceGroupNotFound";
    public const string ResourceSelfDependency = "resourceSelfDependency";
    public const string DependentResourcesRunning = "dependentResourcesRunning";
    public const string DependencyAutoStartFailed = "dependencyAutoStartFailed";
    public const string FeatureDisabled = "featureDisabled";
    public const string InsufficientPermission = "insufficientPermission";
    public const string OperationFailed = "operationFailed";
}

public sealed class ControlPlaneException : InvalidOperationException
{
    public ControlPlaneException(ControlPlaneError error)
        : base(error.Message)
    {
        Error = error;
    }

    public ControlPlaneException(ControlPlaneError error, Exception innerException)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    public ControlPlaneError Error { get; }
}

public sealed class ControlPlaneAccessDeniedException : UnauthorizedAccessException
{
    public ControlPlaneAccessDeniedException(ControlPlaneError error)
        : base(error.Message)
    {
        Error = error;
    }

    public ControlPlaneError Error { get; }

    public static ControlPlaneAccessDeniedException ForResource(
        string resourceId,
        string permission) =>
        new(new ControlPlaneError(
            ControlPlaneErrorCodes.InsufficientPermission,
            $"The '{permission}' permission is required for resource '{resourceId}'."));
}
