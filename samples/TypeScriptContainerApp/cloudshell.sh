#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$repo_root/CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
data_dir="${CLOUDSHELL_DATA_DIR:-$state_dir/data}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5114}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.container-app:typescript-container-api}"

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
  start-app      Start the TypeScript-declared container app resource.
  stop-app       Stop the TypeScript-declared container app resource.
  restart-app    Restart the TypeScript-declared container app resource.
  template       Build and print the TypeScript-authored resource template.
  apply          Build and apply the TypeScript-authored resource template to
                 the configured Control Plane URL.
  apply-start    Build and apply the template, starting the daemon if needed.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Daemon state directory. Default: $state_dir
  CLOUDSHELL_DATA_DIR           CloudShell host data directory. Default: $data_dir
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

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  run)
    (cd "$script_dir" && CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run --silent apply -- --run "$@")
    ;;
  run-no-auth)
    (cd "$script_dir" && Authentication__Enabled=false CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run --silent apply -- --run "$@")
    ;;
  start)
    run_cli control-plane start \
      --host-project "$host_project" \
      --url "$control_plane_url" \
      --state-dir "$state_dir" \
      --data-dir "$data_dir" \
      "$@"
    ;;
  start-no-auth)
    Authentication__Enabled=false run_cli control-plane start \
      --host-project "$host_project" \
      --url "$control_plane_url" \
      --state-dir "$state_dir" \
      --data-dir "$data_dir" \
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
    if [[ -f "$state_dir/control-plane.json" ]]; then
      run_cli control-plane stop \
        --state-dir "$state_dir" || true
    fi
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
      --start-dependencies \
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
      --start-dependencies \
      "$@"
    ;;
  template)
    (cd "$script_dir" && npm run --silent template -- "$@")
    ;;
  apply)
    (cd "$script_dir" && CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run --silent apply -- "$@")
    ;;
  apply-start)
    (cd "$script_dir" && CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" npm run --silent apply -- --start "$@")
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
