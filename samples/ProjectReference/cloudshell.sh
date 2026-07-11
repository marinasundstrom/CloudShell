#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.ProjectReferenceAppHost.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5104}"
api_resource_id="${CLOUDSHELL_API_RESOURCE_ID:-application.dotnet-app:project-reference-api}"
frontend_resource_id="${CLOUDSHELL_FRONTEND_RESOURCE_ID:-application.dotnet-app:project-reference-frontend}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template        Print the launcher-authored ResourceTemplate YAML.
  apply           Apply the template to the configured Control Plane.
  run             Run the local development host in the foreground, apply the
                  template, and keep the host tied to the launcher lifetime.
  run-no-auth     Run the foreground host with authentication disabled.
  start           Start or reuse the local development host daemon, then apply the template.
  start-no-auth   Start or reuse the daemon with authentication disabled
                  when a new host process is launched, then apply the template.
  stop            Stop the recorded host process.
  reset           Stop the recorded host process and remove generated sample state.
  open            Open the configured host URL in the default browser.
  resources       List resources from the configured Control Plane.
  start-api       Start the Project Reference API resource.
  start-frontend  Start the Project Reference Frontend resource.
  stop-api        Stop the Project Reference API resource.
  stop-frontend   Stop the Project Reference Frontend resource.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Launcher state directory. Default: $state_dir
  CLOUDSHELL_APP_HOST_PROJECT   Launcher project path. Default: $app_host_project
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
USAGE
}

run_launcher() {
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
  run)
    run_launcher --run "$@"
    ;;
  run-no-auth)
    Authentication__Enabled=false run_launcher --run "$@"
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
  start-api)
    run_cli resource action execute "$api_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  start-frontend)
    run_cli resource action execute "$frontend_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  stop-api)
    run_cli resource action execute "$api_resource_id" stop \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  stop-frontend)
    run_cli resource action execute "$frontend_resource_id" stop \
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
