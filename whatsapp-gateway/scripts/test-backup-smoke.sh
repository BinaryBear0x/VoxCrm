#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_ROOT="$(mktemp -d /private/tmp/voxcrm-backup-smoke.XXXXXX)"
SESSION_DIR="$(mktemp -d /private/tmp/voxcrm-session-smoke.XXXXXX)"
trap 'rm -rf "$BACKUP_ROOT" "$SESSION_DIR"' EXIT

mkdir -p "$BACKUP_ROOT/daily" "$BACKUP_ROOT/weekly" "$BACKUP_ROOT/monthly"
for index in $(seq 1 10); do mkdir -p "$BACKUP_ROOT/daily/200001$(printf '%02d' "$index")T000000Z"; done
for index in $(seq 1 6); do mkdir -p "$BACKUP_ROOT/weekly/200002$(printf '%02d' "$index")T000000Z"; done
for index in $(seq 1 5); do mkdir -p "$BACKUP_ROOT/monthly/200003$(printf '%02d' "$index")T000000Z"; done
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
  "$ROOT_DIR/scripts/backup.sh"
)"

test -s "$backup_dir/voxcrm-db.dump"
test -s "$backup_dir/gateway-db.dump"
test -s "$backup_dir/whatsapp-sessions.tar.gz"
tar -tzf "$backup_dir/whatsapp-sessions.tar.gz" | grep -q 'baileys/test-clinic/auth.json'

clinic_json="$(find "$backup_dir" -maxdepth 1 -name 'clinic-*.json.gz' | head -n 1)"
test -n "$clinic_json"

permission="$(stat -f '%Lp' "$backup_dir/voxcrm-db.dump")"
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

daily_count="$(find "$BACKUP_ROOT/daily" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"
weekly_count="$(find "$BACKUP_ROOT/weekly" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"
monthly_count="$(find "$BACKUP_ROOT/monthly" -mindepth 1 -maxdepth 1 -type d | wc -l | tr -d ' ')"

test "$daily_count" -le 7
test "$weekly_count" -le 4
test "$monthly_count" -le 3

if "$ROOT_DIR/scripts/restore.sh" >/tmp/voxcrm-restore-smoke.out 2>/tmp/voxcrm-restore-smoke.err; then
  echo "restore.sh should fail without arguments" >&2
  exit 1
fi

echo "Backup smoke ok: $backup_dir"
