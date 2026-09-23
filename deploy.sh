#!/usr/bin/env bash
set -euo pipefail

STAGING_HOST="admin_virgis@172.16.16.19"
STAGING_DIR="/app-rt-tester"
IMAGE="ghcr.io/xbefreex/rt-tester:latest"

echo "==> Copying docker-compose.yml to staging"
scp docker-compose.yml "${STAGING_HOST}:${STAGING_DIR}/docker-compose.yml"

echo "==> Pulling and restarting on staging"
ssh "${STAGING_HOST}" "cd ${STAGING_DIR} && docker compose pull && docker compose up -d && docker image prune -f"

echo "==> Deployed ${IMAGE} to ${STAGING_HOST}:${STAGING_DIR}"
