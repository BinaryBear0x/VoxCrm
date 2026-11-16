#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEPLOY_ROOT="$(cd "$ROOT/.." && pwd)"
DOMAIN="${DOMAIN:-petcrm.fenrirsoftware.com}"
ENV_FILE="${VOXCRM_SECRETS_ENV:-/etc/voxcrm/secrets/production.env}"
LOGIN_FAILURE_THRESHOLD="${LOGIN_FAILURE_THRESHOLD:-10}"
QUEUE_LAG_THRESHOLD_SECONDS="${QUEUE_LAG_THRESHOLD_SECONDS:-300}"
HEALTH_CHECK_ATTEMPTS="${HEALTH_CHECK_ATTEMPTS:-3}"
HEALTH_CHECK_RETRY_DELAY_SECONDS="${HEALTH_CHECK_RETRY_DELAY_SECONDS:-2}"
HEALTH_CHECK_CONNECT_TIMEOUT_SECONDS="${HEALTH_CHECK_CONNECT_TIMEOUT_SECONDS:-3}"
HEALTH_CHECK_MAX_TIME_SECONDS="${HEALTH_CHECK_MAX_TIME_SECONDS:-8}"
LOCAL_HEALTH_URL="${LOCAL_HEALTH_URL:-http://127.0.0.1:5180/healthz}"

check_health_with_retry() {
  local label="$1"
  local url="$2"
  local attempt=1
  local curl_exit=0

  while [ "$attempt" -le "$HEALTH_CHECK_ATTEMPTS" ]; do
    if curl --fail --silent --show-error \
      --connect-timeout "$HEALTH_CHECK_CONNECT_TIMEOUT_SECONDS" \
      --max-time "$HEALTH_CHECK_MAX_TIME_SECONDS" \
      "$url" >/dev/null; then
      return 0
    else
      curl_exit=$?
    fi

    echo "$label health-check denemesi basarisiz (deneme $attempt/$HEALTH_CHECK_ATTEMPTS, curl exit $curl_exit)" >&2
    if [ "$attempt" -lt "$HEALTH_CHECK_ATTEMPTS" ]; then
      sleep "$HEALTH_CHECK_RETRY_DELAY_SECONDS"
    fi
    attempt=$((attempt + 1))
  done

  return "$curl_exit"
}

usage="$(df -P / | awk 'NR==2 {gsub(/%/, "", $5); print $5}')"
if [ "$usage" -ge 85 ]; then "$ROOT/notify.sh" "Disk kullanimi kritik: %$usage" critical; elif [ "$usage" -ge 75 ]; then "$ROOT/notify.sh" "Disk kullanimi yuksek: %$usage" warning; fi

if ! check_health_with_retry "VoxCrm local" "$LOCAL_HEALTH_URL"; then
  "$ROOT/notify.sh" "VoxCrm local health-check basarisiz" critical
  exit 1
fi

if ! check_health_with_retry "VoxCrm dis HTTPS" "https://$DOMAIN/healthz"; then
  "$ROOT/notify.sh" "VoxCrm local saglikli fakat $DOMAIN dis HTTPS health-check basarisiz (DNS/TLS/proxy/ag kontrol edilmeli)" warning
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

# Device status is checked per clinic; offline queues must not generate retry/lag spam.
bash "$ROOT/monitor-whatsapp.sh" "$postgres_container"
