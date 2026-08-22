#!/usr/bin/env bash
set -euo pipefail

repository_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
image_tag=${1:-local}
image_repository=${2:-cloudshell}
dotnet_version=${DOTNET_VERSION:-11.0-preview}

services=(
  "configuration-store:CloudShell.ConfigurationStoreService"
  "secrets-vault:CloudShell.SecretsVaultService"
  "device-registry:CloudShell.DeviceRegistryService"
)

for service in "${services[@]}"; do
  image_name=${service%%:*}
  project_directory=${service#*:}
  docker build \
    --build-arg "DOTNET_VERSION=${dotnet_version}" \
    --build-arg "IMAGE_VERSION=${image_tag}" \
    --file "${repository_root}/${project_directory}/Dockerfile" \
    --tag "${image_repository}:${image_name}-${image_tag}" \
    "${repository_root}"
done
