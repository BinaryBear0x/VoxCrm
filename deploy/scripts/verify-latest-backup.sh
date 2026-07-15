#!/usr/bin/env bash
set -euo pipefail
umask 077

DEPLOY_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BACKUP_ROOT="${BACKUP_ROOT:-/var/backups/voxcrm}"
BACKUP_KEY="${BACKUP_ENCRYPTION_KEY_FILE:-/etc/voxcrm/secrets/backup.key}"
latest="$(find "$BACKUP_ROOT/snapshots" -mindepth 1 -maxdepth 1 -type d | sort -r | head -n 1)"
test -n "$latest"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

sha256_check() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum -c "$@"
  else
    shasum -a 256 -c "$@"
  fi
}

(
  cd "$latest"
  sha256_check backup.tar.gz.enc.sha256
)
openssl enc -d -aes-256-cbc -pbkdf2 -iter 600000 \
  -pass "file:$BACKUP_KEY" -in "$latest/backup.tar.gz.enc" | tar -xzf - -C "$work"
(
  cd "$work"
  sha256_check SHA256SUMS
)
test -s "$work/voxcrm-db.dump"
test -s "$work/gateway-db.dump"
"$DEPLOY_ROOT/scripts/notify.sh" "VoxCrm günlük backup bütünlük kontrolü başarılı" info
