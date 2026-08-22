#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -lt 1 || "$#" -gt 3 ]]; then
  echo "Usage: $0 <version> [package-source] [image-repository]" >&2
  exit 2
fi

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
package_version=$1
package_source=${2:-https://www.myget.org/F/cloudshell/api/v3/index.json}
image_repository=${3:-ghcr.io/marinasundstrom/cloudshell}
resource_id=configuration.store:yaml-sample-settings
image="${image_repository}:configuration-store-${package_version}"
working_directory=$(mktemp -d "${TMPDIR:-/tmp}/cloudshell-cli-image-verify.XXXXXX")
tool_directory="${working_directory}/tool"
data_directory="${working_directory}/data"
host_log="${working_directory}/host.log"
host_pid=""

existing_containers=$(docker ps -aq \
  --filter "label=cloudshell.owner-resource-id=${resource_id}")
if [[ -n "$existing_containers" ]]; then
  echo "A container already exists for ${resource_id}; stop it before running this verification." >&2
  exit 1
fi

cleanup() {
  if [[ "$host_pid" =~ ^[0-9]+$ ]] && kill -0 "$host_pid" 2>/dev/null; then
    kill -INT "$host_pid" 2>/dev/null || true
    for _ in {1..20}; do
      kill -0 "$host_pid" 2>/dev/null || break
      sleep 1
    done
    if kill -0 "$host_pid" 2>/dev/null; then
      kill -TERM "$host_pid" 2>/dev/null || true
    fi
    wait "$host_pid" 2>/dev/null || true
  fi

  while IFS= read -r container_id; do
    [[ "$container_id" =~ ^[0-9a-f]+$ ]] || continue
    owner=$(docker inspect \
      --format '{{index .Config.Labels "cloudshell.owner-resource-id"}}' \
      "$container_id" 2>/dev/null || true)
    if [[ "$owner" == "$resource_id" ]]; then
      docker rm -f "$container_id" >/dev/null
    fi
  done < <(docker ps -aq --filter "label=cloudshell.owner-resource-id=${resource_id}")
}
trap cleanup EXIT

if docker image inspect "$image" >/dev/null 2>&1; then
  docker image rm "$image" >/dev/null
fi

dotnet tool install CloudShell.Cli \
  --tool-path "$tool_directory" \
  --version "$package_version" \
  --add-source "$package_source"
tool="${tool_directory}/cloudshell"

"$tool" run "${repository_root}/samples/YamlAppHost/cloudshell.yaml" \
  --data-dir "$data_directory" >"$host_log" 2>&1 &
host_pid=$!

for attempt in {1..60}; do
  if "$tool" resource show "$resource_id" \
      --control-plane http://127.0.0.1:5112 >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$host_pid" 2>/dev/null; then
    cat "$host_log"
    echo "Released CloudShell CLI host exited before registering ${resource_id}." >&2
    exit 1
  fi
  if [[ "$attempt" -eq 60 ]]; then
    cat "$host_log"
    echo "Released CloudShell CLI host did not register ${resource_id}." >&2
    exit 1
  fi
  sleep 2
done

"$tool" resource action execute "$resource_id" start \
  --control-plane http://127.0.0.1:5112
curl --fail --retry 20 --retry-delay 1 http://127.0.0.1:5266/healthz

container_id=$(docker ps -q \
  --filter "label=cloudshell.owner-resource-id=${resource_id}")
if [[ ! "$container_id" =~ ^[0-9a-f]+$ ]]; then
  cat "$host_log"
  echo "Expected one running container for ${resource_id}." >&2
  exit 1
fi

actual_image=$(docker inspect --format '{{.Config.Image}}' "$container_id")
if [[ "$actual_image" != "$image" ]]; then
  echo "Expected image '${image}', but the container uses '${actual_image}'." >&2
  exit 1
fi

docker image inspect "$image" >/dev/null
echo "Verified CloudShell.Cli ${package_version} pulled and ran ${image}."
