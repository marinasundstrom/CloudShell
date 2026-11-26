#!/usr/bin/env bash
# Local HTTP sample only. Configure through loopback inside the container because
# Docker Desktop's published-port traffic may be classified as external.
set -euo pipefail
/opt/keycloak/bin/kc.sh start-dev --import-realm &
keycloak_pid=$!
trap 'kill -TERM "$keycloak_pid" 2>/dev/null || true; wait "$keycloak_pid" || true' TERM INT EXIT

ready=false
for attempt in {1..60}; do
    if /opt/keycloak/bin/kcadm.sh config credentials \
        --server http://localhost:8080 --realm master \
        --user "$KC_BOOTSTRAP_ADMIN_USERNAME" \
        --password "$KC_BOOTSTRAP_ADMIN_PASSWORD" >/dev/null 2>&1; then
        ready=true
        break
    fi
    kill -0 "$keycloak_pid" || exit 1
    sleep 2
done
if [[ "$ready" != true ]]; then
    echo "Keycloak development setup timed out." >&2
    exit 1
fi
/opt/keycloak/bin/kcadm.sh update realms/master -s sslRequired=none
/opt/keycloak/bin/kcadm.sh update realms/cloudshell -s sslRequired=none
wait "$keycloak_pid"
