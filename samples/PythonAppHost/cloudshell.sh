#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

python_launcher_path="${CLOUDSHELL_PYTHON_LAUNCHER_PATH:-$repo_root/Launchers/Python/cloudshell}"
app_host_script="${CLOUDSHELL_APP_HOST_SCRIPT:-$script_dir/AppHost/app_host.py}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5107}"
app_resource_id="${CLOUDSHELL_APP_RESOURCE_ID:-application.python-app:python-api}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template       Print the Python launcher-authored ResourceTemplate JSON.
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
  start-app      Start the Python app resource.
  stop-app       Stop the Python app resource.
  restart-app    Restart the Python app resource.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL  Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR          Launcher state directory. Default: $state_dir
  CLOUDSHELL_APP_HOST_SCRIPT    Launcher script path. Default: $app_host_script
  CLOUDSHELL_PYTHON_LAUNCHER_PATH
                                Python launcher package path. Default: $python_launcher_path
  CLOUDSHELL_CLI_PROJECT        CLI project path. Default: $cli_project
  CLOUDSHELL_APP_RESOURCE_ID    App resource id. Default: $app_resource_id
USAGE
}

run_launcher() {
  PYTHONPATH="$python_launcher_path${PYTHONPATH:+:$PYTHONPATH}" \
    python3 "$app_host_script" "$@"
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
    run_launcher template "$@"
    ;;
  apply)
    run_launcher apply "$@"
    ;;
  run)
    run_launcher run "$@"
    ;;
  run-no-auth)
    Authentication__Enabled=false run_launcher run "$@"
    ;;
  start)
    run_launcher start "$@"
    ;;
  start-no-auth)
    Authentication__Enabled=false run_launcher start "$@"
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
