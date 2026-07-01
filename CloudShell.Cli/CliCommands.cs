using CloudShell.Abstractions.ResourceManager;
using ResourceDefinitionApplyMode = CloudShell.ResourceModel.ResourceDefinitionApplyMode;

namespace CloudShell.Cli;

internal abstract record CliCommand;

internal sealed record HelpCommand : CliCommand;

internal sealed record ControlPlaneStartCommand(
    string StateDirectory,
    string? HostProject,
    Uri Url,
    string? BearerToken,
    bool NoBuild,
    int TimeoutSeconds) : CliCommand;

internal sealed record ControlPlaneStopCommand(
    string StateDirectory) : CliCommand;

internal sealed record ControlPlaneStatusCommand(
    string StateDirectory,
    string? BearerToken) : CliCommand;

internal sealed record TemplateApplyCommand(
    string TemplatePath,
    string StateDirectory,
    Uri? ControlPlaneUrl,
    string? BearerToken,
    bool StartControlPlane,
    string? HostProject,
    Uri StartUrl,
    bool NoBuild,
    int TimeoutSeconds,
    ResourceDefinitionApplyMode Mode) : CliCommand;

internal sealed record ResourceListCommand(
    string StateDirectory,
    Uri? ControlPlaneUrl,
    string? BearerToken,
    string? ResourceType,
    ResourceClass? ResourceClass,
    bool? IsRegistered) : CliCommand;

internal sealed record ResourceActionExecuteCommand(
    string ResourceId,
    string ActionId,
    string StateDirectory,
    Uri? ControlPlaneUrl,
    string? BearerToken,
    bool StartDependencies,
    bool IgnoreDependentWarning) : CliCommand;

internal sealed record HostNameAddCommand(
    string HostName,
    string Address,
    string? HostsFile,
    bool DryRun) : CliCommand;

internal sealed record HostNameRemoveCommand(
    string HostName,
    string? HostsFile,
    bool DryRun) : CliCommand;
