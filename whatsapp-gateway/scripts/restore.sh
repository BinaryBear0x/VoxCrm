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
BACKUP_ENCRYPTION_KEY_FILE="${BACKUP_ENCRYPTION_KEY_FILE:-}"
ARCHIVE="$BACKUP_DIR/backup.tar.gz.enc"

sha256_check() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$@"
  else
    shasum -a 256 -c "$@"
  fi
}

if [ -z "$BACKUP_ENCRYPTION_KEY_FILE" ] || [ ! -s "$BACKUP_ENCRYPTION_KEY_FILE" ]; then
  echo "BACKUP_ENCRYPTION_KEY_FILE must point to the restore key file." >&2
  exit 2
fi
key_mode="$(stat -f '%Lp' "$BACKUP_ENCRYPTION_KEY_FILE" 2>/dev/null || stat -c '%a' "$BACKUP_ENCRYPTION_KEY_FILE")"
if [ $((8#$key_mode & 8#077)) -ne 0 ]; then
  echo "Restore key file must not be readable or writable by group/others (use chmod 600)." >&2
  exit 2
fi

test -f "$ARCHIVE"
test -f "$ARCHIVE.sha256"
(
  cd "$BACKUP_DIR"
  sha256_check "$(basename "$ARCHIVE.sha256")"
)

restore_dir="$(mktemp -d /private/tmp/voxcrm-restore.XXXXXX)"
trap 'rm -rf "$restore_dir"' EXIT
openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 \
  -pass "file:$BACKUP_ENCRYPTION_KEY_FILE" -in "$ARCHIVE" | tar -xzf - -C "$restore_dir"
(
  cd "$restore_dir"
  sha256_check SHA256SUMS
)

test -f "$restore_dir/voxcrm-db.dump"
test -f "$restore_dir/gateway-db.dump"

echo "This will replace local VoxCrm and Gateway databases from: $BACKUP_DIR"
read -r -p "Type RESTORE to continue: " confirmation
if [ "$confirmation" != "RESTORE" ]; then
  echo "Restore cancelled."
  exit 1
fi

docker exec "$POSTGRES_CONTAINER" dropdb -U "$DB_USER" --if-exists "$VOXCRM_DB"
docker exec "$POSTGRES_CONTAINER" createdb -U "$DB_USER" "$VOXCRM_DB"
docker exec -i "$POSTGRES_CONTAINER" pg_restore -U "$DB_USER" -d "$VOXCRM_DB" < "$restore_dir/voxcrm-db.dump"

docker exec "$POSTGRES_CONTAINER" dropdb -U "$DB_USER" --if-exists "$GATEWAY_DB"
docker exec "$POSTGRES_CONTAINER" createdb -U "$DB_USER" "$GATEWAY_DB"
docker exec -i "$POSTGRES_CONTAINER" pg_restore -U "$DB_USER" -d "$GATEWAY_DB" < "$restore_dir/gateway-db.dump"

if [ -f "$restore_dir/whatsapp-sessions.tar.gz" ]; then
  mkdir -p "$SESSION_DIR"
  tar -xzf "$restore_dir/whatsapp-sessions.tar.gz" -C "$SESSION_DIR"
  chmod -R go-rwx "$SESSION_DIR"
fi

echo "Restore completed."
