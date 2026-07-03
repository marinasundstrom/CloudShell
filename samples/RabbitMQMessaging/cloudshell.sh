#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../.." && pwd)"

app_host_project="${CLOUDSHELL_APP_HOST_PROJECT:-$script_dir/AppHost/CloudShell.RabbitMQMessagingAppHost.csproj}"
cli_project="${CLOUDSHELL_CLI_PROJECT:-$repo_root/CloudShell.Cli/CloudShell.Cli.csproj}"
state_dir="${CLOUDSHELL_STATE_DIR:-$script_dir/.cloudshell}"
control_plane_url="${CLOUDSHELL_CONTROL_PLANE_URL:-http://127.0.0.1:5112}"
rabbitmq_resource_id="${CLOUDSHELL_RABBITMQ_RESOURCE_ID:-application.rabbitmq:rabbitmq}"
dotnet_resource_id="${CLOUDSHELL_DOTNET_RESOURCE_ID:-application.aspnet-core-project:rabbitmq-dotnet}"
java_resource_id="${CLOUDSHELL_JAVA_RESOURCE_ID:-application.java-app:rabbitmq-java}"

usage() {
  cat <<USAGE
Usage: ./cloudshell.sh <command> [options]

Commands:
  build-java      Compile the Java RabbitMQ sample jar.
  template        Print the launcher-authored ResourceTemplate YAML.
  apply           Build the Java app and apply the template.
  run             Build the Java app, run the local development host in the
                  foreground, apply the template, and keep the host tied to the
                  launcher lifetime.
  run-no-auth     Build the Java app and run the foreground host with authentication disabled.
  start           Build the Java app and start or reuse the local development host daemon.
  start-no-auth   Build the Java app and start or reuse the daemon with authentication disabled.
  stop            Stop the recorded host process.
  reset           Stop the recorded host process and remove generated sample state.
  open            Open the configured host URL in the default browser.
  resources       List resources from the configured Control Plane.
  start-broker    Start the RabbitMQ broker resource.
  start-dotnet    Start the .NET app and its dependencies.
  start-java      Build and start the Java app and its dependencies.
  start-apps      Build and start both apps with RabbitMQ as a dependency.
  stop-broker     Stop the RabbitMQ broker resource.
  stop-dotnet     Stop the .NET app resource.
  stop-java       Stop the Java app resource.

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

build_java() {
  "$script_dir/JavaApp/build.sh" >/dev/null
}

command="${1:-help}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$command" in
  build-java)
    "$script_dir/JavaApp/build.sh"
    ;;
  template)
    run_launcher "$@"
    ;;
  apply)
    build_java
    run_launcher --apply "$@"
    ;;
  run)
    build_java
    run_launcher --run "$@"
    ;;
  run-no-auth)
    build_java
    Authentication__Enabled=false run_launcher --run "$@"
    ;;
  start)
    build_java
    run_launcher --start "$@"
    ;;
  start-no-auth)
    build_java
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
    rm -rf "$state_dir" "$script_dir/Data" "$script_dir/JavaApp/target"
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
  start-broker)
    run_cli resource action execute "$rabbitmq_resource_id" start \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  start-dotnet)
    run_cli resource action execute "$dotnet_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  start-java)
    build_java
    run_cli resource action execute "$java_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  start-apps)
    build_java
    run_cli resource action execute "$dotnet_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    run_cli resource action execute "$java_resource_id" start \
      --control-plane "$control_plane_url" \
      --start-dependencies \
      "$@"
    ;;
  stop-broker)
    run_cli resource action execute "$rabbitmq_resource_id" stop \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  stop-dotnet)
    run_cli resource action execute "$dotnet_resource_id" stop \
      --control-plane "$control_plane_url" \
      "$@"
    ;;
  stop-java)
    run_cli resource action execute "$java_resource_id" stop \
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
