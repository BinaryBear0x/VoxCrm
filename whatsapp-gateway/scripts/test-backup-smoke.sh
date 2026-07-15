#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_ROOT="$(mktemp -d /private/tmp/voxcrm-backup-smoke.XXXXXX)"
SESSION_DIR="$(mktemp -d /private/tmp/voxcrm-session-smoke.XXXXXX)"
KEY_FILE="$(mktemp /private/tmp/voxcrm-backup-key.XXXXXX)"
DECRYPTED_DIR="$(mktemp -d /private/tmp/voxcrm-backup-decrypted.XXXXXX)"
VERIFY_SUFFIX="$(date +%s)_$$"
VERIFY_VOXCRM_DB="voxcrm_restore_verify_$VERIFY_SUFFIX"
VERIFY_GATEWAY_DB="gateway_restore_verify_$VERIFY_SUFFIX"
sha256_check() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$@"
  else
    shasum -a 256 -c "$@"
  fi
}
cleanup() {
  docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" dropdb -U "${DB_USER:-voxcrm}" --if-exists "$VERIFY_VOXCRM_DB" >/dev/null 2>&1 || true
  docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" dropdb -U "${DB_USER:-voxcrm}" --if-exists "$VERIFY_GATEWAY_DB" >/dev/null 2>&1 || true
  rm -rf "$BACKUP_ROOT" "$SESSION_DIR" "$KEY_FILE" "$DECRYPTED_DIR"
}
trap cleanup EXIT
openssl rand -base64 48 > "$KEY_FILE"
chmod 600 "$KEY_FILE"

mkdir -p "$BACKUP_ROOT/snapshots" "$BACKUP_ROOT/daily" "$BACKUP_ROOT/monthly"
for index in $(seq 1 31); do mkdir -p "$BACKUP_ROOT/snapshots/200001$(printf '%02d' "$index")T000000Z"; done
for index in $(seq 1 33); do mkdir -p "$BACKUP_ROOT/daily/200002$(printf '%02d' "$index")"; done
for index in $(seq 1 15); do mkdir -p "$BACKUP_ROOT/monthly/2000$(printf '%02d' "$index")"; done
printf 'session-smoke' > "$SESSION_DIR/session.txt"
mkdir -p "$SESSION_DIR/baileys/test-clinic"
printf '{"provider":"baileys"}' > "$SESSION_DIR/baileys/test-clinic/auth.json"

backup_dir="$(
  BACKUP_ROOT="$BACKUP_ROOT" \
  SESSION_DIR="$SESSION_DIR" \
  POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-voxcrm-postgres}" \
  VOXCRM_DB="${VOXCRM_DB:-voxcrm_dev}" \
  GATEWAY_DB="${GATEWAY_DB:-voxcrm_gateway_dev}" \
  DB_USER="${DB_USER:-voxcrm}" \
  BACKUP_ENCRYPTION_KEY_FILE="$KEY_FILE" \
  "$ROOT_DIR/scripts/backup.sh"
)"

test -s "$backup_dir/backup.tar.gz.enc"
test -s "$backup_dir/backup.tar.gz.enc.sha256"
openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 \
  -pass "file:$KEY_FILE" -in "$backup_dir/backup.tar.gz.enc" | tar -xzf - -C "$DECRYPTED_DIR"
(
  cd "$DECRYPTED_DIR"
  sha256_check SHA256SUMS
)
test -s "$DECRYPTED_DIR/voxcrm-db.dump"
test -s "$DECRYPTED_DIR/gateway-db.dump"
test -s "$DECRYPTED_DIR/whatsapp-sessions.tar.gz"
tar -tzf "$DECRYPTED_DIR/whatsapp-sessions.tar.gz" | grep -q 'baileys/test-clinic/auth.json'

docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" createdb -U "${DB_USER:-voxcrm}" "$VERIFY_VOXCRM_DB"
docker exec -i "${POSTGRES_CONTAINER:-voxcrm-postgres}" pg_restore -U "${DB_USER:-voxcrm}" -d "$VERIFY_VOXCRM_DB" < "$DECRYPTED_DIR/voxcrm-db.dump"
docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" psql -U "${DB_USER:-voxcrm}" -d "$VERIFY_VOXCRM_DB" -Atc 'SELECT count(*) FROM "Clinics";' >/dev/null

docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" createdb -U "${DB_USER:-voxcrm}" "$VERIFY_GATEWAY_DB"
docker exec -i "${POSTGRES_CONTAINER:-voxcrm-postgres}" pg_restore -U "${DB_USER:-voxcrm}" -d "$VERIFY_GATEWAY_DB" < "$DECRYPTED_DIR/gateway-db.dump"
docker exec "${POSTGRES_CONTAINER:-voxcrm-postgres}" psql -U "${DB_USER:-voxcrm}" -d "$VERIFY_GATEWAY_DB" -Atc 'SELECT count(*) FROM clinic_sessions;' >/dev/null

clinic_json="$(find "$DECRYPTED_DIR" -maxdepth 1 -name 'clinic-*.json.gz' | head -n 1)"
test -n "$clinic_json"

permission="$(stat -f '%Lp' "$backup_dir/backup.tar.gz.enc")"
test "$permission" = "600"

python3 - "$clinic_json" <<'PY'
import gzip
import json
import sys

required = {
    "clinic",
    "petOwners",
    "patients",
    "patientOwners",
    "appointments",
    "muayeneler",
    "debts",
    "payments",
    "vaccinationRecords",
    "vaccineTypes",
    "serviceItems",
    "whatsappNotifications",
    "whatsappInboundMessages",
    "whatsappTemplates",
}

with gzip.open(sys.argv[1], "rt", encoding="utf-8") as handle:
    payload = json.load(handle)

missing = sorted(required - set(payload))
if missing:
    raise SystemExit(f"Missing backup keys: {missing}")

for forbidden in ("PasswordHash", "SecurityStamp", "ConcurrencyStamp"):
    if forbidden in json.dumps(payload):
        raise SystemExit(f"Forbidden auth field found in clinic export: {forbidden}")
PY

snapshot_count="$(find "$BACKUP_ROOT/snapshots" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"
daily_count="$(find "$BACKUP_ROOT/daily" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"
monthly_count="$(find "$BACKUP_ROOT/monthly" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"

test "$snapshot_count" -le 28
test "$daily_count" -le 30
test "$monthly_count" -le 12

if "$ROOT_DIR/scripts/restore.sh" >/tmp/voxcrm-restore-smoke.out 2>/tmp/voxcrm-restore-smoke.err; then
  echo "restore.sh should fail without arguments" >&2
  exit 1
fi

echo "Backup smoke ok: $backup_dir"
