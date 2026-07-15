import asyncio
import os
import sys
import uuid
from collections.abc import AsyncIterator
from datetime import datetime, timedelta, timezone
from pathlib import Path

import asyncpg
import jwt
import pytest
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import AsyncSession, async_sessionmaker, create_async_engine

os.environ.setdefault("APP_ENVIRONMENT", "development")
os.environ.setdefault("AUTO_CREATE_DB", "false")

GATEWAY_API_ROOT = Path(__file__).resolve().parents[1]
if str(GATEWAY_API_ROOT) not in sys.path:
    sys.path.insert(0, str(GATEWAY_API_ROOT))


@pytest.fixture()
async def test_database(monkeypatch: pytest.MonkeyPatch) -> AsyncIterator[async_sessionmaker[AsyncSession]]:
    database_name = f"voxcrm_gateway_test_{uuid.uuid4().hex}"
    admin_dsn = os.environ.get(
        "GATEWAY_TEST_ADMIN_DSN",
        "postgresql://voxcrm:voxcrm_dev_password@127.0.0.1:5432/postgres",
    )
    database_url = f"postgresql+asyncpg://voxcrm:voxcrm_dev_password@127.0.0.1:5432/{database_name}"

    admin = await asyncpg.connect(admin_dsn)
    await admin.execute(f'CREATE DATABASE "{database_name}"')
    await admin.close()

    engine = create_async_engine(database_url, pool_pre_ping=True)
    session_local = async_sessionmaker(engine, expire_on_commit=False, class_=AsyncSession)

    from app import database as database_module
    from app.models import Base
    from app import sender as sender_module

    monkeypatch.setattr(database_module, "engine", engine)
    monkeypatch.setattr(database_module, "SessionLocal", session_local)
    monkeypatch.setattr(sender_module, "SessionLocal", session_local)

    async with engine.begin() as connection:
        await connection.run_sync(Base.metadata.create_all)

    try:
        yield session_local
    finally:
        await engine.dispose()
        admin = await asyncpg.connect(admin_dsn)
        await admin.execute(f'DROP DATABASE IF EXISTS "{database_name}" WITH (FORCE)')
        await admin.close()


@pytest.fixture()
async def client(test_database: async_sessionmaker[AsyncSession]) -> AsyncIterator[AsyncClient]:
    from app.auth import _seen_jti
    from app.main import app

    _seen_jti.clear()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://testserver") as async_client:
        yield async_client


@pytest.fixture(autouse=True)
def clear_rate_limit_state() -> None:
    from app.sender import _last_send_by_clinic

    _last_send_by_clinic.clear()


def gateway_token(
    scope: str,
    jti: str | None = None,
    expires_delta: timedelta | None = None,
    clinic_id: uuid.UUID | None = None,
) -> str:
    now = datetime.now(timezone.utc)
    expires_delta = expires_delta or timedelta(minutes=5)
    payload = {
        "iss": "voxcrm",
        "aud": "voxcrm-whatsapp-gateway",
        "sub": "voxcrm-web-test",
        "scope": scope,
        "iat": int(now.timestamp()),
        "exp": int((now + expires_delta).timestamp()),
        "jti": jti or uuid.uuid4().hex,
    }
    if clinic_id:
        payload["clinic_id"] = str(clinic_id)
    return jwt.encode(payload, "dev-only-change-this-very-long-whatsapp-gateway-secret", algorithm="HS256")


async def sleep_noop(_: float) -> None:
    await asyncio.sleep(0)
