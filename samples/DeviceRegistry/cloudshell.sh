#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.DeviceRegistryAppHost.csproj}"
device_app_project="${CLOUDSHELL_DEVICE_APP_PROJECT:-$script_dir/DeviceApp/CloudShell.DeviceRegistry.DeviceApp.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5108}"
registry_endpoint="${CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT:-http://localhost:7150}"
configuration_endpoint="${CLOUDSHELL_CONFIGURATION_STORE_ENDPOINT:-http://localhost:7152}"
device_app_url="${CLOUDSHELL_DEVICE_APP_URL:-http://localhost:7153}"
settings_resource_id="${CLOUDSHELL_CONFIGURATION_STORE_RESOURCE_ID:-configuration.store:device-settings}"
secrets_resource_id="${CLOUDSHELL_SECRETS_VAULT_RESOURCE_ID:-secrets.vault:factory}"
registry_resource_id="${CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID:-iot.device-registry:devices}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  template        Print the launcher-authored ResourceTemplate YAML.
  run             Run the local development host in the foreground and apply the template.
  start           Start or reuse the local development host daemon, then apply the template.
  stop            Stop the recorded host process.
  reset           Stop the recorded host process and remove generated sample state.
  open            Open the configured host URL in the default browser.
  resources       List resources from the configured Control Plane.
  start-services  Start the Configuration Store, Secrets Vault, and Device Registry resources.
  run-device      Run the independent device app.
  enroll          Enroll the current device through the device app.

Environment:
  CLOUDSHELL_CONTROL_PLANE_URL              Host URL. Default: $control_plane_url
  CLOUDSHELL_STATE_DIR                      Launcher state directory. Default: $state_dir
  CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT       Device Registry endpoint. Default: $registry_endpoint
  CLOUDSHELL_CONFIGURATION_STORE_ENDPOINT   Configuration Store endpoint. Default: $configuration_endpoint
  CLOUDSHELL_DEVICE_APP_URL                 Device app URL. Default: $device_app_url
USAGE
}

run_launcher() {
  dotnet run --project "$app_host_project" -- "$@"
}

run_cli() {
  dotnet run --project "$cli_project" -- "$@"
}

start_resource() {
  run_cli resource action execute "$1" start \
    --control-plane "$control_plane_url"
}

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  template)
    run_launcher "$@"
    ;;
  run)
    run_launcher --run "$@"
    ;;
  start)
    run_launcher --start "$@"
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
    start_resource "$settings_resource_id"
    start_resource "$secrets_resource_id"
    start_resource "$registry_resource_id"
    ;;
  run-device)
    dotnet run --project "$device_app_project" -- \
      --urls "$device_app_url" \
      --DeviceRegistry:Endpoint "$registry_endpoint" \
      --DeviceRegistry:ResourceId "$registry_resource_id" \
      --ConfigurationStore:Endpoint "$configuration_endpoint" \
      --ConfigurationStore:ResourceId "$settings_resource_id" \
      --ConfigurationStore:EntryName Device:Mode \
      --Device:Manufacturer cloudshell \
      "$@"
    ;;
  enroll)
    curl -X POST "$device_app_url/enroll-current-device"
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
