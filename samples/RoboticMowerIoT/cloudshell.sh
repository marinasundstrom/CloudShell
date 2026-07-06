#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.RoboticMowerIoTAppHost.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
host_project="${CLOUDSHELL_HOST_PROJECT:-$repo_root/CloudShell.LocalDevelopmentHost/CloudShell.LocalDevelopmentHost.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:7165}"
backend_endpoint="${MOWER_BACKEND_URL:-http://localhost:7161}"
frontend_endpoint="${MOWER_FRONTEND_URL:-http://localhost:7162}"
registry_endpoint="${CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT:-http://localhost:7160}"
registry_resource_id="${CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID:-${CLOUDSHELL_REGISTRY_RESOURCE_ID:-iot.device-registry:park-devices}}"
enrollment_token="${CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN:-local-development-mower-enrollment-token}"
backend_resource_id="${CLOUDSHELL_BACKEND_RESOURCE_ID:-application.container-app:mower-backend}"
frontend_resource_id="${CLOUDSHELL_FRONTEND_RESOURCE_ID:-application.javascript-app:mower-frontend}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template        Print the launcher-authored ResourceTemplate YAML.
  run             Run the CloudShell sample host in the foreground.
  start           Start or reuse the local development host daemon, then apply the template.
  stop            Stop the recorded host process.
  reset           Stop the recorded host process and remove generated sample state.
  open            Open the configured host URL in the default browser.
  resources       List resources from the configured Control Plane.
  start-services  Start Device Registry, backend, and frontend resources.
  stop-services   Stop frontend and backend resources.
  run-mower       Run one simulated mower device.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL              Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR                      Launcher state directory. Default: $state_dir
  CLOUDSHELL_HOST_PROJECT                   Host project path. Default: $host_project
  MOWER_BACKEND_URL                         Backend ingress URL. Default: $backend_endpoint
  MOWER_FRONTEND_URL                        Frontend URL. Default: $frontend_endpoint
  CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT       Device Registry endpoint. Default: $registry_endpoint
  CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN  Device enrollment proof token.
  MOWER_ID                                  Mower simulator id. Default: mower-001
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
  template)
    dotnet run --project "$app_host_project" -- "$@"
    ;;
  run)
    dotnet run --project "$app_host_project" -- \
      --run \
      --host-project "$host_project" \
      --state-dir "$state_dir" \
      --control-plane "$control_plane_url" \
      --url "$control_plane_url" \
      "$@"
    ;;
  start)
    dotnet run --project "$app_host_project" -- \
      --start \
      --host-project "$host_project" \
      --state-dir "$state_dir" \
      --control-plane "$control_plane_url" \
      --url "$control_plane_url" \
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
  start-services)
    run_cli resource action execute "$registry_resource_id" start \
      --control-plane "$control_plane_url"
    run_cli resource action execute "$backend_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies
    run_cli resource action execute "$frontend_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies
    ;;
  stop-services)
    run_cli resource action execute "$frontend_resource_id" stop \
      --control-plane "$control_plane_url" || true
    run_cli resource action execute "$backend_resource_id" stop \
      --control-plane "$control_plane_url" || true
    ;;
  run-mower)
    (
      cd "$script_dir/DeviceApp"
      CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT="$registry_endpoint" \
      CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID="$registry_resource_id" \
      CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN="$enrollment_token" \
      MOWER_BACKEND_URL="$backend_endpoint" \
      npm run start -- "$@"
    )
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
