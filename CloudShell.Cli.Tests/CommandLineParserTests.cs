using CloudShell.Cli;
using CloudShell.ResourceModel;

namespace CloudShell.Cli.Tests;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Parse_ControlPlaneStart_UsesDefaults()
    {
        var command = Assert.IsType<ControlPlaneStartCommand>(
            CommandLineParser.Parse(["control-plane", "start"]));

        Assert.Equal(".cloudshell", command.StateDirectory);
        Assert.Equal(new Uri("http://127.0.0.1:5097"), command.Url);
        Assert.Null(command.HostProject);
        Assert.Null(command.BearerToken);
        Assert.False(command.NoBuild);
        Assert.Equal(60, command.TimeoutSeconds);
    }

    [Fact]
    public void Parse_TemplateApply_UsesRecordedDaemonByDefault()
    {
        var command = Assert.IsType<TemplateApplyCommand>(
            CommandLineParser.Parse(["template", "apply", "template.json"]));

        Assert.Equal("template.json", command.TemplatePath);
        Assert.Equal(".cloudshell", command.StateDirectory);
        Assert.Null(command.ControlPlaneUrl);
        Assert.Null(command.BearerToken);
        Assert.False(command.StartControlPlane);
        Assert.Equal(ResourceDefinitionApplyMode.CreateOrUpdate, command.Mode);
    }

    [Fact]
    public void Parse_TemplateApply_SupportsRemoteControlPlaneAndMode()
    {
        var command = Assert.IsType<TemplateApplyCommand>(
            CommandLineParser.Parse(
            [
                "template",
                "apply",
                "template.json",
                "--control-plane",
                "https://control-plane.example.com",
                "--bearer-token",
                "token-value",
                "--mode",
                "update-existing"
            ]));

        Assert.Equal(new Uri("https://control-plane.example.com"), command.ControlPlaneUrl);
        Assert.Equal("token-value", command.BearerToken);
        Assert.Equal(ResourceDefinitionApplyMode.UpdateExisting, command.Mode);
    }

    [Fact]
    public void Parse_UnknownOption_ThrowsUsageException()
    {
        Assert.Throws<CliUsageException>(() =>
            CommandLineParser.Parse(["control-plane", "status", "--wat"]));
    }

    [Fact]
    public void Parse_ResourceActionExecute_ReadsActionOptions()
    {
        var command = Assert.IsType<ResourceActionExecuteCommand>(
            CommandLineParser.Parse(
            [
                "resource",
                "action",
                "execute",
                "application:api",
                "start",
                "--start-dependencies"
            ]));

        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal("start", command.ActionId);
        Assert.True(command.StartDependencies);
    }

    [Fact]
    public void Parse_HostNamesAdd_ReadsHostsFileAndDryRun()
    {
        var command = Assert.IsType<HostNameAddCommand>(
            CommandLineParser.Parse(
            [
                "host",
                "names",
                "add",
                "api.local.test",
                "127.0.0.1",
                "--hosts-file",
                "hosts",
                "--dry-run"
            ]));

        Assert.Equal("api.local.test", command.HostName);
        Assert.Equal("127.0.0.1", command.Address);
        Assert.Equal("hosts", command.HostsFile);
        Assert.True(command.DryRun);
    }
}
