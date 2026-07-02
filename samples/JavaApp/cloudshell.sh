#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$script_dir/Host/CloudShell.JavaAppHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5098}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.java-app:java-api}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  build-app      Compile the Java sample jar.
  run            Build the Java app and run the sample host in the foreground.
  run-no-auth    Build the Java app and run the foreground host with authentication disabled.
  start          Build the Java app and start or reuse the sample Control Plane daemon.
  start-no-auth  Build the Java app and start or reuse the daemon with authentication disabled
                 when a new host process is launched.
  status         Show recorded daemon status.
  stop           Stop the recorded daemon process.
  reset          Stop the daemon and remove generated sample state.
  open           Open the configured host URL in the default browser.
  resources      List resources from the configured Control Plane.
  start-app      Build and start the Java app resource.
  stop-app       Stop the Java app resource.
  restart-app    Build and restart the Java app resource.

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

build_app() {
  "$script_dir/App/build.sh" >/dev/null
}

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  build-app)
    "$script_dir/App/build.sh"
    ;;
  run)
    build_app
    dotnet run --project "$host_project" -- --urls "$control_plane_url" "$@"
    ;;
  run-no-auth)
    build_app
    Authentication__Enabled=false dotnet run --project "$host_project" -- --urls "$control_plane_url" "$@"
    ;;
  start)
    build_app
    run_cli control-plane start \
      --host-project "$host_project" \
      --url "$control_plane_url" \
      --state-dir "$state_dir" \
      "$@"
    ;;
  start-no-auth)
    build_app
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
    rm -rf "$state_dir" "$script_dir/Host/Data" "$script_dir/App/target"
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
    build_app
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
    build_app
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
