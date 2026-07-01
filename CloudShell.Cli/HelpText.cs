namespace CloudShell.Cli;

internal static class HelpText
{
    public const string Text = """
CloudShell CLI

Usage:
  cloudshell control-plane start [--host-project <path>] [--url <url>] [--state-dir <path>] [--no-build] [--bearer-token <token>]
  cloudshell control-plane stop [--state-dir <path>]
  cloudshell control-plane status [--state-dir <path>] [--bearer-token <token>]
  cloudshell resource list [--control-plane <url>] [--type <type>] [--class <class>] [--registered <true|false>]
  cloudshell resource action execute <resource-id> <action-id> [--control-plane <url>]
  cloudshell template apply <template.yaml|template.json> [--control-plane <url>] [--mode <mode>] [--start] [--bearer-token <token>]
  cloudshell host names add <host-name> <ip-address> [--hosts-file <path>] [--dry-run]
  cloudshell host names remove <host-name> [--hosts-file <path>] [--dry-run]

Commands:
  control-plane start    Start a local CloudShell Control Plane process and record daemon state.
  control-plane stop     Stop the recorded local Control Plane process.
  control-plane status   Show recorded process and API readiness.
  resource list          List resources from the selected Control Plane.
  resource action        Execute a resource action through the Control Plane API.
  template apply         Apply a ResourceTemplate YAML or JSON document through the Control Plane API.
  host names             Add or remove local hosts-file mappings.

Options:
  --control-plane        Control Plane base URL. Defaults to recorded daemon state.
  --bearer-token         Bearer token for Control Plane API calls. Defaults to CLOUDSHELL_CONTROL_PLANE_TOKEN.
  --host-project         CloudShell host project to run. Defaults to CloudShell.Host in the current repo.
  --mode                 create-or-update, create-only, or update-existing. Default: create-or-update.
  --no-build             Pass --no-build to dotnet run when starting the host.
  --start                Start the local Control Plane before applying the template.
  --state-dir            Directory for local daemon state. Default: .cloudshell.
  --timeout-seconds      Startup readiness timeout. Default: 60.
  --url                  Local Control Plane URL when starting. Default: http://127.0.0.1:5097.
  --hosts-file           Hosts file path. Defaults to the system hosts file.
  --dry-run              Show the local host-name change without writing the hosts file.
""";
}
