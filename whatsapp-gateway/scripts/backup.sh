#!/usr/bin/env bash
set -euo pipefail
umask 077

BACKUP_ROOT="${BACKUP_ROOT:-$HOME/Documents/Projeler/voxcrm-backups}"
POSTGRES_CONTAINER="${POSTGRES_CONTAINER:-voxcrm-postgres}"
VOXCRM_DB="${VOXCRM_DB:-voxcrm_dev}"
GATEWAY_DB="${GATEWAY_DB:-voxcrm_gateway_dev}"
DB_USER="${DB_USER:-voxcrm}"
SESSION_DIR="${SESSION_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/sessions}"

timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
day_dir="$BACKUP_ROOT/daily/$timestamp"
mkdir -p "$day_dir"
chmod 700 "$BACKUP_ROOT" "$BACKUP_ROOT/daily" "$day_dir"

docker exec "$POSTGRES_CONTAINER" pg_dump -U "$DB_USER" -Fc "$VOXCRM_DB" > "$day_dir/voxcrm-db.dump"
docker exec "$POSTGRES_CONTAINER" pg_dump -U "$DB_USER" -Fc "$GATEWAY_DB" > "$day_dir/gateway-db.dump"

if [ -d "$SESSION_DIR" ]; then
  tar -czf "$day_dir/whatsapp-sessions.tar.gz" -C "$SESSION_DIR" .
fi

clinics="$(docker exec "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d "$VOXCRM_DB" -Atc 'SELECT "ID" FROM "Clinics" WHERE "IsActive" = TRUE ORDER BY "Name";')"
while IFS= read -r clinic_id; do
  [ -z "$clinic_id" ] && continue
  docker exec -i "$POSTGRES_CONTAINER" psql -U "$DB_USER" -d "$VOXCRM_DB" -At <<SQL | gzip -9 > "$day_dir/clinic-$clinic_id.json.gz"
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

if [ "$(date -u +%u)" = "1" ]; then
  mkdir -p "$BACKUP_ROOT/weekly"
  cp -R "$day_dir" "$BACKUP_ROOT/weekly/$timestamp"
fi

if [ "$(date -u +%d)" = "01" ]; then
  mkdir -p "$BACKUP_ROOT/monthly"
  cp -R "$day_dir" "$BACKUP_ROOT/monthly/$timestamp"
fi

prune_dir() {
  local dir="$1"
  local keep="$2"
  [ -d "$dir" ] || return 0
  find "$dir" -mindepth 1 -maxdepth 1 -type d | sort -r | tail -n +"$((keep + 1))" | xargs -r rm -rf
}

prune_dir "$BACKUP_ROOT/daily" 7
prune_dir "$BACKUP_ROOT/weekly" 4
prune_dir "$BACKUP_ROOT/monthly" 3

echo "$day_dir"
