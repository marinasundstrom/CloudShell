#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$repo_root/samples/JavaScriptApp/Host/CloudShell.JavaScriptAppHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
sibling_state_dir="$repo_root/samples/JavaScriptApp/.cloudshell"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5097}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.javascript-app:typescript-frontend}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  run            Run the sample host in the foreground.
  run-no-auth    Run the foreground host with Authentication:Enabled=false.
  start          Start or reuse the sample Control Plane daemon.
  start-no-auth  Start or reuse the daemon with Authentication:Enabled=false
                 when a new host process is launched.
  status         Show recorded daemon status.
  stop           Stop the recorded daemon process.
  reset          Stop the daemon and remove generated sample state.
  open           Open the configured host URL in the default browser.
  resources      List resources from the configured Control Plane.
  start-app      Start the TypeScript-declared JavaScript app resource.
  stop-app       Stop the TypeScript-declared JavaScript app resource.
  restart-app    Restart the TypeScript-declared JavaScript app resource.
  apply          Build and apply the TypeScript-authored resource template to
                 the configured Control Plane URL.
  apply-start    Build and apply the template, starting the daemon if needed.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Daemon state directory. Default: $state_dir
  CLOUDSHELL_HOST_PROJECT       Host project path. Default: $host_project
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
  CLOUDSHELL_APP_RESOURCE_ID    App resource id. Default: $app_resource_id

Any extra options are passed through to the underlying command. For example:
  ./cloudshell.sh run --environment Development
  ./cloudshell.sh apply --bearer-token <token>
USAGE
}

run_cli() {
  dotnet run --project "$cli_project" -- "$@"
}

stop_state_dir() {
  local target_state_dir="$1"
  if [[ -f "$target_state_dir/control-plane.json" ]]; then
    run_cli control-plane stop \
      --state-dir "$target_state_dir" || true
  fi
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
    stop_state_dir "$state_dir"
    stop_state_dir "$sibling_state_dir"
    rm -rf "$state_dir" "$repo_root/samples/JavaScriptApp/Host/Data"
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
  apply)
    CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run apply -- "$@"
    ;;
  apply-start)
    CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run apply -- --start "$@"
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
