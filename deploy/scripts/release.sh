#!/usr/bin/env bash
set -euo pipefail
umask 077

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
VERSION="${1:-$(date -u +%Y%m%dT%H%M%SZ)}"
OUT="${RELEASE_OUT:-$ROOT/artifacts/releases}"

sha256_file() {
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$@"
  else
    shasum -a 256 "$@"
  fi
}

if ! git -C "$ROOT" diff --quiet || ! git -C "$ROOT" diff --cached --quiet \
  || [ -n "$(git -C "$ROOT" ls-files --others --exclude-standard)" ]; then
  echo "Release reddedildi: çalışma ağacı temiz ve commit edilmiş olmalıdır." >&2
  exit 2
fi

"$ROOT/whatsapp-gateway/scripts/test-all.sh"
mkdir -p "$OUT"
git -C "$ROOT" -c tar.umask=0022 archive \
  --format=tar.gz \
  --prefix=VoxCrm/ \
  --output="$OUT/voxcrm-$VERSION.tar.gz" \
  HEAD
(
  cd "$OUT"
  sha256_file "voxcrm-$VERSION.tar.gz" > "voxcrm-$VERSION.tar.gz.sha256"
)
chmod 600 "$OUT/voxcrm-$VERSION.tar.gz" "$OUT/voxcrm-$VERSION.tar.gz.sha256"
echo "$OUT/voxcrm-$VERSION.tar.gz"
