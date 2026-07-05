#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_dir="${CLOUDSHELL_REACT_TYPESCRIPT_APP_HOST_DIR:-$script_dir/AppHost}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$repo_root/CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
data_dir="${CLOUDSHELL_DATA_DIR:-$state_dir}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5110}"
frontend_resource_id="${CLOUDSHELL_FRONTEND_RESOURCE_ID:-application.javascript-app:react-frontend}"
backend_resource_id="${CLOUDSHELL_BACKEND_RESOURCE_ID:-application.javascript-app:react-api}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template       Print the TypeScript-authored ResourceTemplate JSON.
  apply          Apply the template to the configured Control Plane.
  run            Run the local development host in the foreground, apply the
                 template, and keep the host tied to the launcher lifetime.
  run-no-auth    Run the foreground host with authentication disabled.
  start          Start or reuse the local development host daemon, then apply the template.
  start-no-auth  Start or reuse the daemon with authentication disabled
                 when a new host process is launched, then apply the template.
  stop           Stop the recorded host process.
  reset          Stop the recorded host process and remove generated sample state.
  open           Open the configured host URL in the default browser.
  resources      List resources from the configured Control Plane.
  start-app      Start the React frontend and its dependencies.
  stop-app       Stop the React frontend.
  start-api      Start the backend API and its dependencies.
  stop-api       Stop the backend API.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Launcher state directory. Default: $state_dir
  CLOUDSHELL_DATA_DIR           CloudShell host data directory. Default: $data_dir
  CLOUDSHELL_HOST_PROJECT       Host project path. Default: $host_project
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
USAGE
}

run_launcher() {
  CLOUDSHELL_CONTROL_PLANE_URL="$control_plane_url" \
  CLOUDSHELL_STATE_DIR="$state_dir" \
  CLOUDSHELL_DATA_DIR="$data_dir" \
  CLOUDSHELL_CLI_PROJECT="$cli_project" \
  CLOUDSHELL_HOST_PROJECT="$host_project" \
  npm run apply -- "$@"
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
    (cd "$app_host_dir" && npm run template -- "$@")
    ;;
  apply)
    (cd "$app_host_dir" && run_launcher "$@")
    ;;
  run)
    (cd "$app_host_dir" && run_launcher --run "$@")
    ;;
  run-no-auth)
    (cd "$app_host_dir" && Authentication__Enabled=false run_launcher --run "$@")
    ;;
  start)
    (cd "$app_host_dir" && run_launcher --start "$@")
    ;;
  start-no-auth)
    (cd "$app_host_dir" && Authentication__Enabled=false run_launcher --start "$@")
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
    run_cli resource action execute "$frontend_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  stop-app)
    run_cli resource action execute "$frontend_resource_id" stop \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  start-api)
    run_cli resource action execute "$backend_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  stop-api)
    run_cli resource action execute "$backend_resource_id" stop \
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
