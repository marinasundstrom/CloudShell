#!/usr/bin/env sh
set -eu

REGISTRY_PORT="${CONTAINER_APP_DEPLOYMENT_REGISTRY_PORT:-5023}"

docker run -d \
  --name cloudshell-container-app-deployment-registry \
  -p "${REGISTRY_PORT}:5000" \
  --restart unless-stopped \
  registry:2
