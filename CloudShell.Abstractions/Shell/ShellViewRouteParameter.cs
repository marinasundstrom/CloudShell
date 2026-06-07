namespace CloudShell.Abstractions.Shell;

public sealed record ShellViewRouteParameter(
    string Name,
    string Token,
    bool IsOptional,
    bool IsCatchAll);
