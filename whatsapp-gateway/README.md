# VoxCrm WhatsApp Gateway

Separate WhatsApp Gateway for VoxCrm vaccination reminders.

## Services

- `gateway-api`: FastAPI orchestration API. Owns auth, local delivery state, polling VoxCrm, status reporting and health.
- `wa-worker`: internal Node service. Owns Baileys WhatsApp linked-device sessions, QR lifecycle, send and inbound forwarding.

VoxCrm remains the source of business data. The gateway stores only technical session and delivery state.

## Local Infra

This setup expects the lightweight OrbStack containers already created:

```txt
Postgres: 127.0.0.1:5432
Gateway DB: voxcrm_gateway_dev
Redis: 127.0.0.1:6379
```

Copy `.env.example` to `.env` and adjust `VOXCRM_API_BASE_URL` to the actual VoxCrm.Api URL.

The worker uses Baileys and does not run a managed browser. Local development may keep `WORKER_SESSION_ENCRYPTION_KEY` empty, but production must configure it so Baileys auth-state files are encrypted at rest.

## Run Locally

Start the complete local stack in this order:

```bash
orb start
docker start voxcrm-postgres voxcrm-redis
docker compose -f ~/Documents/Projeler/voxcrm-whatsapp-gateway/docker-compose.yml up -d gateway-api wa-worker
dotnet run --project ~/Documents/Projeler/VoxCrm/VoxCrm.Api
dotnet run --project ~/Documents/Projeler/VoxCrm/VoxCrm.Web
```

WhatsApp delivery requires all of these to be running: Postgres/Redis, `gateway-api`, `wa-worker`, `VoxCrm.Api`, and `VoxCrm.Web`. If only Postgres/Redis are running, VoxCrm can create `Pending` notifications but nothing will claim or send them.

Manual service startup is also supported:

```bash
cd gateway-api
python3 -m venv .venv
. .venv/bin/activate
pip install -r requirements.txt
uvicorn app.main:app --host 0.0.0.0 --port 8088 --reload
```

```bash
cd wa-worker
npm install
npm run dev
```

## Migrations

Production should use Alembic instead of auto-creating tables at runtime:

```bash
cd gateway-api
alembic -c alembic.ini upgrade head
```

Keep `AUTO_CREATE_DB=true` only for local development convenience. In production, set:

```txt
APP_ENVIRONMENT=production
AUTO_CREATE_DB=false
```

## Tests

Install Gateway test dependencies once:

```bash
python3 -m venv .venv
. .venv/bin/activate
pip install -r gateway-api/requirements-dev.txt
```

Run the full local verification suite:

```bash
./scripts/test-all.sh
```

The suite runs VoxCrm build/tests, NuGet vulnerability audit, Gateway pytest, worker Vitest tests, Python syntax checks, backup script syntax checks and a non-destructive backup smoke test.

## Backup

The lightweight local backup script creates:

- compressed `pg_dump -Fc` backups for VoxCrm and Gateway databases
- per-clinic `json.gz` exports for readable clinic archives
- compressed WhatsApp session archive

```bash
./scripts/backup.sh
```

Default retention is daily 7, weekly 4 and monthly 3 under:

```txt
~/Documents/Projeler/voxcrm-backups
```

Restore is intentionally explicit because it replaces local databases:

```bash
./scripts/restore.sh ~/Documents/Projeler/voxcrm-backups/daily/<timestamp>
```

## API

- `POST /api/clinics/{clinicId}/whatsapp/connect`
- `GET /api/clinics/{clinicId}/whatsapp/status`
- `GET /api/clinics/{clinicId}/whatsapp/qr`
- `POST /api/clinics/{clinicId}/whatsapp/disconnect`
- `GET /api/health`

The browser should call VoxCrm, not this API directly. VoxCrm signs short-lived JWTs and proxies clinic/dealer actions.
