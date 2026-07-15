#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_ROOT="$(cd "$ROOT/.." && pwd)"
DOMAIN="${DOMAIN:-petcrm.fenrirsoftware.com}"
ENV_FILE="${VOXCRM_SECRETS_ENV:-/etc/voxcrm/secrets/production.env}"
LOGIN_FAILURE_THRESHOLD="${LOGIN_FAILURE_THRESHOLD:-10}"
QUEUE_LAG_THRESHOLD_SECONDS="${QUEUE_LAG_THRESHOLD_SECONDS:-300}"
usage="$(df -P / | awk 'NR==2 {gsub(/%/, "", $5); print $5}')"
if [ "$usage" -ge 85 ]; then "$ROOT/notify.sh" "Disk kullanimi kritik: %$usage" critical; elif [ "$usage" -ge 75 ]; then "$ROOT/notify.sh" "Disk kullanimi yuksek: %$usage" warning; fi
if ! curl --fail --silent --max-time 10 "https://$DOMAIN/healthz" >/dev/null; then
  "$ROOT/notify.sh" "$DOMAIN health-check basarisiz" critical
  exit 1
fi

postgres_container="$(docker compose --env-file "$ENV_FILE" -f "$DEPLOY_ROOT/docker-compose.prod.yml" ps -q postgres)"
if [ -z "$postgres_container" ]; then
  "$ROOT/notify.sh" "PostgreSQL monitor container bulamadi" critical
  exit 1
fi

if ! auth_metrics="$(docker exec "$postgres_container" psql -U "${POSTGRES_USER:-voxcrm}" -d "${POSTGRES_DB:-voxcrm_prod}" -AtF ' ' -c \
  'SELECT count(*) FILTER (WHERE "Action" = '\''Auth.LoginFailed'\''), count(*) FILTER (WHERE "Action" = '\''Auth.AccountLocked'\''), count(*) FILTER (WHERE "Level" IN ('\''Error'\'', '\''Critical'\'') AND ("Source" = '\''Hangfire'\'' OR "Action" ILIKE '\''%Migration%'\'' OR "Action" ILIKE '\''%Retention%'\'')) FROM "SystemAuditLogs" WHERE "CreatedAt" >= now() - interval '\''5 minutes'\'';')"; then
  "$ROOT/notify.sh" "CRM audit metrikleri okunamadi" critical
  exit 1
fi
read -r login_failures account_locks job_errors <<< "$auth_metrics"
if [ "$login_failures" -ge "$LOGIN_FAILURE_THRESHOLD" ]; then
  "$ROOT/notify.sh" "Son 5 dakikada yuksek login hatasi: $login_failures" warning
fi
if [ "$account_locks" -gt 0 ]; then
  "$ROOT/notify.sh" "Son 5 dakikada kilitlenen hesap: $account_locks" critical
fi
if [ "$job_errors" -gt 0 ]; then
  "$ROOT/notify.sh" "Migration/retention job hatasi algilandi: $job_errors" critical
fi

if ! whatsapp_metrics="$(docker exec "$postgres_container" psql -U "${POSTGRES_USER:-voxcrm}" -d "${GATEWAY_DB:-voxcrm_gateway_prod}" -AtF ' ' -c \
  "SELECT count(*) FILTER (WHERE state = 'RetryScheduled'), count(*) FILTER (WHERE state = 'NeedsReview'), COALESCE(EXTRACT(EPOCH FROM (now() - min(created_at) FILTER (WHERE state IN ('Claimed', 'Sending', 'RetryScheduled'))))::int, 0) FROM outbound_messages;")"; then
  "$ROOT/notify.sh" "WhatsApp queue metrikleri okunamadi" critical
  exit 1
fi
read -r retry_count needs_review_count queue_lag_seconds <<< "$whatsapp_metrics"
if [ "$retry_count" -gt 0 ]; then
  "$ROOT/notify.sh" "WhatsApp retry bekleyen mesaj: $retry_count" warning
fi
if [ "$needs_review_count" -gt 0 ]; then
  "$ROOT/notify.sh" "WhatsApp NeedsReview mesaj: $needs_review_count" critical
fi
if [ "$queue_lag_seconds" -ge "$QUEUE_LAG_THRESHOLD_SECONDS" ]; then
  "$ROOT/notify.sh" "WhatsApp queue gecikmesi: ${queue_lag_seconds}s" warning
fi
