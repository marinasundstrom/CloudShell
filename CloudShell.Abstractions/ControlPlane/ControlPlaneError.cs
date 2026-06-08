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

    public static ControlPlaneError ResourceNotAvailable(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceNotAvailable,
            $"Resource '{resourceId}' is not available.");

    public static ControlPlaneError ResourceNotRegistered(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceNotRegistered,
            $"Resource '{resourceId}' is not registered.");

    public static ControlPlaneError ResourceGroupNotFound(string resourceGroupId) =>
        new(
            ControlPlaneErrorCodes.ResourceGroupNotFound,
            $"Resource group '{resourceGroupId}' could not be found.");

    public static ControlPlaneError ResourceSelfDependency(string resourceId) =>
        new(
            ControlPlaneErrorCodes.ResourceSelfDependency,
            $"Resource '{resourceId}' cannot depend on itself.");
}

public static class ControlPlaneErrorCodes
{
    public const string InvalidRequest = "invalidRequest";
    public const string ResourceProviderNotFound = "resourceProviderNotFound";
    public const string ResourceProviderCannotCreate = "resourceProviderCannotCreate";
    public const string ResourceNotAvailable = "resourceNotAvailable";
    public const string ResourceNotRegistered = "resourceNotRegistered";
    public const string ResourceGroupNotFound = "resourceGroupNotFound";
    public const string ResourceSelfDependency = "resourceSelfDependency";
    public const string DependentResourcesRunning = "dependentResourcesRunning";
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
