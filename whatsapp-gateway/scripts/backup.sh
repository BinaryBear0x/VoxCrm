#!/usr/bin/env bash
set -euo pipefail
umask 077

BACKUP_ROOT="${BACKUP_ROOT:-$HOME/Documents/Projeler/voxcrm-backups}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-voxcrm-postgres}"
VOXCRM_DB="${VOXCRM_DB:-voxcrm_dev}"
GATEWAY_DB="${GATEWAY_DB:-voxcrm_gateway_dev}"
DB_USER="${DB_USER:-voxcrm}"
SESSION_DIR="${SESSION_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/sessions}"
BACKUP_ENCRYPTION_KEY_FILE="${BACKUP_ENCRYPTION_KEY_FILE:-}"

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$@"
  else
    shasum -a 256 "$@"
  fi
}

if [ -z "$BACKUP_ENCRYPTION_KEY_FILE" ] || [ ! -s "$BACKUP_ENCRYPTION_KEY_FILE" ]; then
  echo "BACKUP_ENCRYPTION_KEY_FILE must point to a non-empty, protected key file." >&2
  exit 2
fi
key_mode="$(stat -f '%Lp' "$BACKUP_ENCRYPTION_KEY_FILE" 2>/dev/null || stat -c '%a' "$BACKUP_ENCRYPTION_KEY_FILE")"
if [ $((8#$key_mode & 8#077)) -ne 0 ]; then
  echo "Backup key file must not be readable or writable by group/others (use chmod 600)." >&2
  exit 2
fi

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
snapshot_dir="$BACKUP_ROOT/snapshots/$timestamp"
mkdir -p "$BACKUP_ROOT/snapshots" "$BACKUP_ROOT/daily" "$BACKUP_ROOT/monthly"
staging_dir="$(mktemp -d "$BACKUP_ROOT/.staging.XXXXXX")"
trap 'rm -rf "$staging_dir"' EXIT
mkdir -p "$snapshot_dir"
chmod 700 "$BACKUP_ROOT" "$BACKUP_ROOT/snapshots" "$BACKUP_ROOT/daily" "$BACKUP_ROOT/monthly" "$snapshot_dir"

docker exec "$POSTGRES_CONTAINER" pg_dump -U "$DB_USER" -Fc "$VOXCRM_DB" > "$staging_dir/voxcrm-db.dump"
docker exec "$POSTGRES_CONTAINER" pg_dump -U "$DB_USER" -Fc "$GATEWAY_DB" > "$staging_dir/gateway-db.dump"

if [ -d "$SESSION_DIR" ]; then
  tar -czf "$staging_dir/whatsapp-sessions.tar.gz" -C "$SESSION_DIR" .
fi

clinics="$(docker exec "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d "$VOXCRM_DB" -Atc 'SELECT "ID" FROM "Clinics" WHERE "IsActive" = TRUE ORDER BY "Name";')"
while IFS= read -r clinic_id; do
  [ -z "$clinic_id" ] && continue
  docker exec -i "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d "$VOXCRM_DB" -At <<SQL | gzip -9 > "$staging_dir/clinic-$clinic_id.json.gz"
SELECT jsonb_build_object(
  'exportedAt', now(),
  'clinicId', '$clinic_id',
  'clinic', (SELECT to_jsonb(c) FROM "Clinics" c WHERE c."ID" = '$clinic_id'::uuid),
  'petOwners', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "PetOwners" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb),
  'patients', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "Patients" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb),
  'patientOwners', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "PatientOwners" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb),
  'appointments', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "Appointments" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "ScheduledAt") x), '[]'::jsonb),
  'muayeneler', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "Muayeneler" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb),
  'debts', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "Borçlar" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "DueDate") x), '[]'::jsonb),
  'payments', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "Payments" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "PaymentDate") x), '[]'::jsonb),
  'vaccinationRecords', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "VaccinationRecords" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "NextDueDate") x), '[]'::jsonb),
  'vaccineTypes', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "VaccineTypes" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "Name") x), '[]'::jsonb),
  'serviceItems', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "ServiceItems" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "Name") x), '[]'::jsonb),
  'whatsappNotifications', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "WhatsAppNotifications" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb),
  'whatsappInboundMessages', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "WhatsAppInboundMessages" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "ReceivedAt") x), '[]'::jsonb),
  'whatsappTemplates', COALESCE((SELECT jsonb_agg(to_jsonb(x)) FROM (SELECT * FROM "WhatsAppTemplates" WHERE "ClinicID" = '$clinic_id'::uuid ORDER BY "CreatedAt") x), '[]'::jsonb)
)::text;
SQL
done <<< "$clinics"

(
  cd "$staging_dir"
  while IFS= read -r -d '' file; do
    sha256_file "$file"
  done < <(find . -type f ! -name SHA256SUMS -print0 | sort -z) > SHA256SUMS
)

tar -czf - -C "$staging_dir" . | openssl enc -aes-256-cbc -salt -pbkdf2 -iter 600000 \
  -pass "file:$BACKUP_ENCRYPTION_KEY_FILE" -out "$snapshot_dir/backup.tar.gz.enc"
(
  cd "$snapshot_dir"
  sha256_file backup.tar.gz.enc > backup.tar.gz.enc.sha256
)
chmod 600 "$snapshot_dir/backup.tar.gz.enc" "$snapshot_dir/backup.tar.gz.enc.sha256"

daily_key="$(date -u +%Y%m%d)"
if [ ! -e "$BACKUP_ROOT/daily/$daily_key" ]; then
  cp -al "$snapshot_dir" "$BACKUP_ROOT/daily/$daily_key"
fi

monthly_key="$(date -u +%Y%m)"
if [ ! -e "$BACKUP_ROOT/monthly/$monthly_key" ]; then
  cp -al "$snapshot_dir" "$BACKUP_ROOT/monthly/$monthly_key"
fi

prune_dir() {
  local dir="$1"
  local keep="$2"
  [ -d "$dir" ] || return 0
  find "$dir" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -rf
}

prune_dir "$BACKUP_ROOT/snapshots" 28
prune_dir "$BACKUP_ROOT/daily" 30
prune_dir "$BACKUP_ROOT/monthly" 12

echo "$snapshot_dir"
