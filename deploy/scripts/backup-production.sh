#!/usr/bin/env bash
set -euo pipefail
umask 077

DEPLOY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_ROOT="$(cd "$DEPLOY_ROOT/.." && pwd)"
ENV_FILE="${VOXCRM_SECRETS_ENV:-/etc/voxcrm/secrets/production.env}"
BACKUP_KEY="${BACKUP_ENCRYPTION_KEY_FILE:-/etc/voxcrm/secrets/backup.key}"
BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/voxcrm}"
SESSION_DIR="${VOXCRM_SESSION_DIR:-/var/lib/voxcrm/whatsapp-sessions}"

postgres_container="$(docker compose --env-file "$ENV_FILE" -f "$DEPLOY_ROOT/docker-compose.prod.yml" ps -q postgres)"
if [ -z "$postgres_container" ]; then
  "$DEPLOY_ROOT/scripts/notify.sh" "VoxCrm backup: PostgreSQL container bulunamadı" critical
  exit 1
fi

if ! output="$(
  POSTGRES_CONTAINER="$postgres_container" \
  VOXCRM_DB="${POSTGRES_DB:-voxcrm_prod}" \
  GATEWAY_DB="${GATEWAY_DB:-voxcrm_gateway_prod}" \
  DB_USER="${POSTGRES_USER:-voxcrm}" \
  BACKUP_ROOT="$BACKUP_ROOT" \
  SESSION_DIR="$SESSION_DIR" \
  BACKUP_ENCRYPTION_KEY_FILE="$BACKUP_KEY" \
  "$PROJECT_ROOT/whatsapp-gateway/scripts/backup.sh"
)"; then
  "$DEPLOY_ROOT/scripts/notify.sh" "VoxCrm şifreli backup başarısız" critical
  exit 1
fi

"$DEPLOY_ROOT/scripts/notify.sh" "VoxCrm backup başarılı: $output" info
echo "$output"
