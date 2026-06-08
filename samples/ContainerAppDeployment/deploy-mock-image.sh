#!/usr/bin/env sh
set -eu

BASE_URL="${CLOUDSHELL_URL:-http://localhost:5007}"
APP_ID="${1:-application:sample-api}"
TAG="${2:-$(date -u +%Y%m%d%H%M%S)}"
IMAGE="cloudshell/mock-api:${TAG}"
REGISTRY="${SAMPLE_REGISTRY:-localhost:5023}"

printf 'Mock image: %s/%s\n' "$REGISTRY" "$IMAGE"
printf 'Updating %s through %s\n' "$APP_ID" "$BASE_URL"

curl -fsS \
  -X POST \
  -H 'Content-Type: application/json' \
  -d "{\"image\":\"${IMAGE}\",\"restartIfRunning\":false,\"triggeredBy\":\"mock-build-script\"}" \
  "${BASE_URL}/api/container-apps/v1/${APP_ID}/revisions"

printf '\n'
