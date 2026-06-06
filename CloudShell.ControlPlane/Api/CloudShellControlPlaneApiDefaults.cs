namespace CloudShell.ControlPlane.Api;

public static class CloudShellControlPlaneApiDefaults
{
    public const string ApiVersion = "v1";
    public const string DocumentName = "control-plane-v1";
    public const string RoutePrefix = $"/api/control-plane/{ApiVersion}";
    public const string OpenApiRoutePattern = "/openapi/{documentName}.json";
}
