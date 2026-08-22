#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
image_tag=${1:-local}
registry_port=${CLOUDSHELL_LOCAL_REGISTRY_PORT:-5000}
registry_name=cloudshell-local-registry
image_repository="localhost:${registry_port}/cloudshell"

if ! docker container inspect "$registry_name" >/dev/null 2>&1; then
  docker run \
    --detach \
    --publish "127.0.0.1:${registry_port}:5000" \
    --restart unless-stopped \
    --name "$registry_name" \
    registry:3
elif [[ "$(docker inspect --format '{{.State.Running}}' "$registry_name")" != "true" ]]; then
  docker start "$registry_name" >/dev/null
fi

published_port=$(docker inspect \
  --format '{{(index (index .NetworkSettings.Ports "5000/tcp") 0).HostPort}}' \
  "$registry_name")
if [[ "$published_port" != "$registry_port" ]]; then
  echo "Container '${registry_name}' publishes port ${published_port}, not ${registry_port}." >&2
  echo "Remove or rename that registry container, or reuse its configured port." >&2
  exit 1
fi

"${repository_root}/eng/containers/build-built-in-services.sh" \
  "$image_tag" \
  "$image_repository"

for service in configuration-store secrets-vault device-registry; do
  docker push "${image_repository}:${service}-${image_tag}"
done

cat <<EOF
Published CloudShell built-in service images to the local registry.

Use these host overrides with a container-backed development host:
CloudShell__BuiltInServices__ConfigurationStore__Image=${image_repository}:configuration-store-${image_tag}
CloudShell__BuiltInServices__SecretsVault__Image=${image_repository}:secrets-vault-${image_tag}
CloudShell__BuiltInServices__DeviceRegistry__Image=${image_repository}:device-registry-${image_tag}
EOF
