#!/usr/bin/env bash
set -euo pipefail
message="${1:?notification message required}"
severity="${2:-warning}"

if [ -n "${TELEGRAM_BOT_TOKEN:-}" ] && [ -n "${TELEGRAM_CHAT_ID:-}" ]; then
  curl --fail --silent --show-error --max-time 10 \
    --data-urlencode "chat_id=$TELEGRAM_CHAT_ID" \
    --data-urlencode "text=[$severity] VoxCrm: $message" \
    "https://api.telegram.org/bot$TELEGRAM_BOT_TOKEN/sendMessage" >/dev/null
fi

if [ -n "${ALERT_EMAIL_TO:-}" ] && command -v sendmail >/dev/null; then
  printf 'To: %s\nSubject: [%s] VoxCrm alert\n\n%s\n' "$ALERT_EMAIL_TO" "$severity" "$message" | sendmail -t
fi
