#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.CSharpAppHost.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$repo_root/CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
data_dir="${CLOUDSHELL_DATA_DIR:-$state_dir}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5099}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.javascript-app:csharp-declared-frontend}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template       Print the launcher-authored ResourceTemplate YAML.
  apply          Apply the template to the configured Control Plane.
  start          Start or reuse the local development host, then apply the template.
  start-no-auth  Start or reuse the local development host with authentication disabled
                 when a new host process is launched, then apply the template.
  stop           Stop the recorded host process.
  reset          Stop the recorded host process and remove generated sample state.
  open           Open the configured host URL in the default browser.
  resources      List resources from the configured Control Plane.
  start-app      Start the C#-declared JavaScript app resource.
  stop-app       Stop the C#-declared JavaScript app resource.
  restart-app    Restart the C#-declared JavaScript app resource.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Launcher state directory. Default: $state_dir
  CLOUDSHELL_DATA_DIR           CloudShell host data directory. Default: $data_dir
  CLOUDSHELL_HOST_PROJECT       Host project path. Default: $host_project
  CLOUDSHELL_APP_HOST_PROJECT   Launcher project path. Default: $app_host_project
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
  CLOUDSHELL_APP_RESOURCE_ID    App resource id. Default: $app_resource_id
USAGE
}

run_launcher() {
  CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" \
  CLOUDSHELL_STATE_DIR="$state_dir" \
  CLOUDSHELL_DATA_DIR="$data_dir" \
  CLOUDSHELL_CLI_PROJECT="$cli_project" \
  CLOUDSHELL_HOST_PROJECT="$host_project" \
  dotnet run --project "$app_host_project" -- "$@"
}

run_cli() {
  dotnet run --project "$cli_project" -- "$@"
}

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  template)
    run_launcher "$@"
    ;;
  apply)
    run_launcher --apply "$@"
    ;;
  start)
    run_launcher --start "$@"
    ;;
  start-no-auth)
    Authentication__Enabled=false run_launcher --start "$@"
    ;;
  stop)
    run_cli control-plane stop \
      --state-dir "$state_dir" \
      "$@"
    ;;
  reset)
    run_cli control-plane stop \
      --state-dir "$state_dir" || true
    rm -rf "$state_dir"
    ;;
  open)
    run_cli ui open \
      --url "$control_plane_url" \
      "$@"
    ;;
  resources)
    run_cli resource list \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  start-app)
    run_cli resource action execute "$app_resource_id" start \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  stop-app)
    run_cli resource action execute "$app_resource_id" stop \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  restart-app)
    run_cli resource action execute "$app_resource_id" restart \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  help|--help|-h)
    usage
    ;;
  *)
    echo "Unknown command: $command" >&2
    echo >&2
    usage >&2
    exit 2
    ;;
esac
