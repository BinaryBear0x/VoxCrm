#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
postgres_container="${1:?PostgreSQL container id required}"
STATE_DIR="${MONITOR_STATE_DIR:-/var/lib/voxcrm/monitor}"
threshold="${QUEUE_LAG_THRESHOLD_SECONDS:-300}"
if ! [[ "$threshold" =~ ^[1-9][0-9]*$ ]]; then echo 'Invalid queue lag threshold' >&2; exit 1; fi
umask 077
mkdir -p -- "$STATE_DIR"
exec 9>"$STATE_DIR/whatsapp-monitor.lock"
# Never let overlapping cron invocations send duplicate transition alerts.
if ! flock -n 9; then exit 0; fi

if ! metrics="$(docker exec "$postgres_container" psql -U "${POSTGRES_USER:-voxcrm}" -d "${GATEWAY_DB:-voxcrm_gateway_prod}" -AtF '|' -c "
WITH clinic_ids AS (
  SELECT clinic_id FROM clinic_sessions UNION SELECT clinic_id FROM outbound_messages
)
SELECT i.clinic_id, CASE WHEN s.status = 'ready' AND s.updated_at < now() - interval '2 minutes' THEN 'status_stale' ELSE COALESCE(s.status, 'missing') END,
 count(m.id) FILTER (WHERE m.state = 'RetryScheduled' AND (m.next_attempt_at IS NULL OR m.next_attempt_at <= now())),
 count(m.id) FILTER (WHERE m.state = 'NeedsReview'),
 GREATEST(0, COALESCE(EXTRACT(EPOCH FROM (now() - min(COALESCE(m.next_attempt_at, m.created_at))
   FILTER (WHERE m.state IN ('Claimed', 'Sending', 'RetryScheduled')
     AND (m.next_attempt_at IS NULL OR m.next_attempt_at <= now()))))::int, 0))
FROM clinic_ids i LEFT JOIN clinic_sessions s ON s.clinic_id = i.clinic_id
LEFT JOIN outbound_messages m ON m.clinic_id = i.clinic_id
GROUP BY i.clinic_id, s.status, s.updated_at ORDER BY i.clinic_id;")"; then
  "$ROOT/notify.sh" "WhatsApp cihaz ve kuyruk metrikleri okunamadı; bağlantı durumu doğrulanamadı." critical
  exit 1
fi

now="$(date +%s)"
while IFS='|' read -r clinic status retry review lag; do
  [ -n "$clinic" ] || continue
  if ! [[ "$clinic" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]] \
     || ! [[ "$retry" =~ ^[0-9]+$ && "$review" =~ ^[0-9]+$ && "$lag" =~ ^[0-9]+$ ]]; then
    echo 'Invalid WhatsApp monitor metrics; previous state retained.' >&2; exit 1
  fi
  file="$STATE_DIR/whatsapp-$clinic.state"
  previous='unknown'; last_retry=0; last_review=0; last_lag=0; ready_since=0
  if [ -f "$file" ]; then
    IFS='|' read -r previous last_retry last_review last_lag ready_since < "$file" || true
  fi
  if ! [[ "$last_retry" =~ ^[0-9]+$ && "$last_review" =~ ^[0-9]+$ && "$last_lag" =~ ^[01]$ && "$ready_since" =~ ^[0-9]+$ ]]; then
    previous='unknown'; last_retry=0; last_review=0; last_lag=0; ready_since=0
  fi

  if [ "$status" != 'ready' ]; then
    if [ "$previous" != 'down' ]; then
      if [[ "$status" = 'unavailable' || "$status" = 'status_stale' || "$status" = 'missing' ]]; then
        "$ROOT/notify.sh" "WhatsApp cihaz bağlantısı doğrulanamıyor (klinik: $clinic, durum: $status). Gateway/worker servisini kontrol edin; kuyruk/retry uyarıları doğrulama sağlanana kadar durduruldu." warning
      else
        "$ROOT/notify.sh" "WhatsApp cihaz bağlantısı kesildi veya hazır değil (klinik: $clinic, durum: $status). QR ile bağlantıyı kontrol edin. Bağlantı kurulana kadar kuyruk/retry uyarıları durduruldu." warning
      fi
    fi
    # Keep the outage state, even when disconnected changes to qr/connecting/auth_failed.
    printf 'down|0|0|0|0\n' > "$file.tmp.$$"
  else
    if [ "$previous" != 'ready' ] || [ "$ready_since" -eq 0 ]; then
      ready_since="$now"
      if [ "$previous" = 'down' ]; then
        "$ROOT/notify.sh" "WhatsApp cihaz bağlantısı yeniden kuruldu (klinik: $clinic)." info
      fi
    fi
    if [ "$retry" -gt 0 ] && [ "$retry" -ne "$last_retry" ]; then
      "$ROOT/notify.sh" "WhatsApp bağlantısı açık; zamanı gelmiş retry bekleyen mesaj: $retry (klinik: $clinic)." warning
    fi
    if [ "$review" -gt 0 ] && [ "$review" -ne "$last_review" ]; then
      "$ROOT/notify.sh" "WhatsApp teslimi belirsiz / inceleme gereken mesaj: $review (klinik: $clinic). Tekrar göndermeden kontrol edin." critical
    fi
    # Offline time must not immediately trigger a lag warning after reconnecting.
    ready_age=$((now - ready_since)); [ "$ready_age" -ge 0 ] || ready_age=0
    effective_lag="$lag"; if [ "$effective_lag" -gt "$ready_age" ]; then effective_lag="$ready_age"; fi
    lag_alarm=0
    if [ "$effective_lag" -ge "$threshold" ]; then
      lag_alarm=1
      if [ "$last_lag" -eq 0 ]; then
        "$ROOT/notify.sh" "WhatsApp bağlantısı açık ancak kuyruk ilerlemiyor: ${effective_lag}s (klinik: $clinic). Gönderim servisini kontrol edin." warning
      fi
    fi
    printf 'ready|%s|%s|%s|%s\n' "$retry" "$review" "$lag_alarm" "$ready_since" > "$file.tmp.$$"
  fi
  mv -f -- "$file.tmp.$$" "$file"
done <<< "$metrics"
