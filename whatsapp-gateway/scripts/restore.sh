#!/usr/bin/env bash
set -euo pipefail

if [ "$#" -lt 1 ]; then
  echo "Usage: $0 /path/to/backup-directory" >&2
  exit 2
fi

BACKUP_DIR="$1"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-voxcrm-postgres}"
VOXCRM_DB="${VOXCRM_DB:-voxcrm_dev}"
GATEWAY_DB="${GATEWAY_DB:-voxcrm_gateway_dev}"
DB_USER="${DB_USER:-voxcrm}"
SESSION_DIR="${SESSION_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/sessions}"

test -f "$BACKUP_DIR/voxcrm-db.dump"
test -f "$BACKUP_DIR/gateway-db.dump"

echo "This will replace local VoxCrm and Gateway databases from: $BACKUP_DIR"
read -r -p "Type RESTORE to continue: " confirmation
if [ "$confirmation" != "RESTORE" ]; then
  echo "Restore cancelled."
  exit 1
fi

docker exec "$POSTGRES_CONTAINER" dropdb -U "$DB_USER" --if-exists "$VOXCRM_DB"
docker exec "$POSTGRES_CONTAINER" createdb -U "$DB_USER" "$VOXCRM_DB"
docker exec -i "$POSTGRES_CONTAINER" pg_restore -U "$DB_USER" -d "$VOXCRM_DB" < "$BACKUP_DIR/voxcrm-db.dump"

docker exec "$POSTGRES_CONTAINER" dropdb -U "$DB_USER" --if-exists "$GATEWAY_DB"
docker exec "$POSTGRES_CONTAINER" createdb -U "$DB_USER" "$GATEWAY_DB"
docker exec -i "$POSTGRES_CONTAINER" pg_restore -U "$DB_USER" -d "$GATEWAY_DB" < "$BACKUP_DIR/gateway-db.dump"

if [ -f "$BACKUP_DIR/whatsapp-sessions.tar.gz" ]; then
  mkdir -p "$SESSION_DIR"
  tar -xzf "$BACKUP_DIR/whatsapp-sessions.tar.gz" -C "$SESSION_DIR"
  chmod -R go-rwx "$SESSION_DIR"
fi

echo "Restore completed."
