#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VOXCRM_ROOT="${VOXCRM_ROOT:-/Users/ozhanyildirim/Documents/Projeler/VoxCrm}"
GATEWAY_API_ROOT="$ROOT_DIR/gateway-api"
WORKER_ROOT="$ROOT_DIR/wa-worker"
PYTHON_BIN="${PYTHON_BIN:-$ROOT_DIR/.venv/bin/python}"
if [ ! -x "$PYTHON_BIN" ]; then
  PYTHON_BIN="python3"
fi

echo "== VoxCrm restore/build/test =="
dotnet restore "$VOXCRM_ROOT/VoxCrm.slnx"
dotnet build "$VOXCRM_ROOT/VoxCrm.slnx" --no-restore -m:1
dotnet test "$VOXCRM_ROOT/VoxCrm.slnx" --no-build

echo "== VoxCrm dependency audit =="
audit_output="$(dotnet list "$VOXCRM_ROOT/VoxCrm.slnx" package --vulnerable --include-transitive)"
echo "$audit_output"
if grep -Eiq '(^|[[:space:]])(High|Critical)([[:space:]]|$)' <<< "$audit_output"; then
  echo "High/Critical vulnerable package found." >&2
  exit 1
fi

echo "== Gateway Python syntax =="
"$PYTHON_BIN" -m py_compile "$GATEWAY_API_ROOT"/app/*.py

echo "== Gateway pytest =="
(
  cd "$GATEWAY_API_ROOT"
  "$PYTHON_BIN" -m pytest
)

echo "== Worker tests =="
npm test --prefix "$WORKER_ROOT"

echo "== Backup script syntax =="
bash -n "$ROOT_DIR/scripts/backup.sh"
bash -n "$ROOT_DIR/scripts/restore.sh"
bash -n "$ROOT_DIR/scripts/test-backup-smoke.sh"

echo "== Backup smoke =="
"$ROOT_DIR/scripts/test-backup-smoke.sh"

echo "All tests passed."
