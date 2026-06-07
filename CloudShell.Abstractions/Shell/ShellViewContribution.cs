namespace CloudShell.Abstractions.Shell;

public sealed record ShellViewContribution(
    string Id,
    string Route,
    Type ComponentType,
    IReadOnlyList<ShellViewRouteParameter> RouteParameters);
