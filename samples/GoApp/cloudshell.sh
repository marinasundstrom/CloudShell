#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$script_dir/Host/CloudShell.GoAppHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5099}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.go-app:go-api}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  run            Run the sample host in the foreground.
  run-no-auth    Run the foreground host with authentication disabled.
  start          Start or reuse the sample Control Plane daemon.
  start-no-auth  Start or reuse the daemon with authentication disabled when a new host process is launched.
  status         Show recorded daemon status.
  stop           Stop the recorded daemon process.
  reset          Stop the daemon and remove generated sample state.
  open           Open the configured host URL in the default browser.
  resources      List resources from the configured Control Plane.
  start-app      Start the Go app resource.
  stop-app       Stop the Go app resource.
  restart-app    Restart the Go app resource.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Daemon state directory. Default: $state_dir
  CLOUDSHELL_HOST_PROJECT       Host project path. Default: $host_project
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
  CLOUDSHELL_APP_RESOURCE_ID    App resource id. Default: $app_resource_id
USAGE
}

run_cli() {
  dotnet run --project "$cli_project" -- "$@"
}

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  run)
    dotnet run --project "$host_project" -- --urls "$control_plane_url" "$@"
    ;;
  run-no-auth)
    Authentication__Enabled=false dotnet run --project "$host_project" -- --urls "$control_plane_url" "$@"
    ;;
  start)
    run_cli control-plane start \
      --host-project "$host_project" \
      --url "$control_plane_url" \
      --state-dir "$state_dir" \
      "$@"
    ;;
  start-no-auth)
    Authentication__Enabled=false run_cli control-plane start \
      --host-project "$host_project" \
      --url "$control_plane_url" \
      --state-dir "$state_dir" \
      "$@"
    ;;
  status)
    run_cli control-plane status \
      --state-dir "$state_dir" \
      "$@"
    ;;
  stop)
    run_cli control-plane stop \
      --state-dir "$state_dir" \
      "$@"
    ;;
  reset)
    run_cli control-plane stop \
      --state-dir "$state_dir" || true
    rm -rf "$state_dir" "$script_dir/Host/Data"
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
