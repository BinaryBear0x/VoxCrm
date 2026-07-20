#!/usr/bin/env bash
set -euo pipefail

COMPOSE_FILE="${COMPOSE_FILE:-deploy/docker-compose.prod.yml}"
DOMAIN="${DOMAIN:-petcrm.fenrirsoftware.com}"
docker compose --env-file /etc/voxcrm/secrets/production.env -f "$COMPOSE_FILE" ps
curl --fail --silent --show-error --max-time 10 "https://$DOMAIN/healthz" >/dev/null
headers="$(curl --fail --silent --show-error --head --max-time 10 "https://$DOMAIN/Auth/Login")"
grep -qi '^strict-transport-security:' <<< "$headers"
grep -qi '^content-security-policy:' <<< "$headers"
if grep -qi "unsafe-inline" <<< "$headers"; then
  echo "CSP contains unsafe-inline" >&2
  exit 1
fi
echo "Production verification passed."
