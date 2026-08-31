#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"
app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.MyServiceBusRabbitMQ.AppHost.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5112}"

run_launcher() {
  CLOUDSHELL_STATE_DIR="$state_dir" \
    CLOUDSHELL_DATA_DIR="$state_dir/data" \
    dotnet run --project "$app_host_project" -- "$@"
}

run_cli() {
  dotnet run --project "$cli_project" -- "$@"
}

resource_action() {
  run_cli resource action execute "$1" "$2" \
    --control-plane "$control_plane_url" \
    "${@:3}"
}

start_resource() {
  local output
  if output="$(resource_action "$1" start --start-dependencies 2>&1)"; then
    printf '%s\n' "$output"
  elif [[ "$output" == *"cannot start while it is running"* ]]; then
    printf '%s\n' "$output"
  else
    printf '%s\n' "$output" >&2
    return 1
  fi
}

build_images() {
  docker build \
    --file "$script_dir/RavenOrderService/Dockerfile" \
    --tag cloudshell-myservicebus-raven:dev \
    "$script_dir"
  docker build \
    --file "$script_dir/JavaFulfillmentService/Dockerfile" \
    --tag cloudshell-myservicebus-java:dev \
    "$script_dir"
}

stop_resource() {
  curl --fail --silent --show-error \
    --request POST \
    "${control_plane_url}/api/control-plane/v1/resources/$1/actions/stop?ignoreDependentWarning=true&triggeredBy=sample-stop" \
    >/dev/null || true
}

case "${1:-help}" in
  template)
    run_launcher
    ;;
  run)
    build_images
    run_launcher --run
    ;;
  start)
    build_images
    run_launcher --start
    ;;
  start-apps)
    build_images
    start_resource "application.rabbitmq:myservicebus-rabbitmq"
    start_resource "application.container-app:myservicebus-postgres"
    start_resource "application.container-app:myservicebus-monitoring"
    start_resource "application.container-app:myservicebus-dashboard"
    start_resource "application.container-app:myservicebus-order-intake"
    start_resource "application.container-app:myservicebus-fulfillment"
    ;;
  build-images)
    build_images
    ;;
  stop-apps)
    stop_resource "application.container-app:myservicebus-fulfillment"
    stop_resource "application.container-app:myservicebus-order-intake"
    stop_resource "application.container-app:myservicebus-dashboard"
    stop_resource "application.container-app:myservicebus-monitoring"
    stop_resource "application.container-app:myservicebus-postgres"
    stop_resource "application.rabbitmq:myservicebus-rabbitmq"
    ;;
  resources)
    run_cli resource list --control-plane "$control_plane_url"
    ;;
  open)
    run_cli ui open --url "$control_plane_url"
    ;;
  stop)
    run_cli control-plane stop --state-dir "$state_dir"
    ;;
  reset)
    run_cli control-plane stop --state-dir "$state_dir" || true
    rm -rf "$state_dir" "$script_dir/Data"
    ;;
  help|--help|-h)
    cat <<USAGE
Usage: ./cloudshell.sh <command>

Commands:
  template     Print the launcher-authored resource template.
  run          Run CloudShell in the foreground and apply the template.
  start        Start or reuse the local CloudShell host and apply the template.
  start-apps   Start RabbitMQ, PostgreSQL, monitoring, dashboard, and both apps.
  build-images Build the Raven and Java workload container images.
  stop-apps    Stop all sample runtime resources in dependency-safe order.
  resources    List the sample resources.
  open         Open the CloudShell UI.
  stop         Stop the recorded CloudShell host.
  reset        Stop the host and remove generated sample state.
USAGE
    ;;
  *)
    echo "Unknown command: $1" >&2
    exit 2
    ;;
esac
