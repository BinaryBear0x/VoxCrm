import os
import subprocess
import sys
import uuid
from pathlib import Path

import asyncpg
import pytest


@pytest.mark.asyncio
async def test_alembic_upgrade_head_creates_gateway_schema():
    database_name = f"voxcrm_gateway_alembic_{uuid.uuid4().hex}"
    admin_dsn = os.environ.get(
        "GATEWAY_TEST_ADMIN_DSN",
        "postgresql://voxcrm:voxcrm_dev_password@127.0.0.1:5432/postgres",
    )
    database_url = f"postgresql+asyncpg://voxcrm:voxcrm_dev_password@127.0.0.1:5432/{database_name}"

    admin = await asyncpg.connect(admin_dsn)
    await admin.execute(f'CREATE DATABASE "{database_name}"')
    await admin.close()

    try:
        gateway_root = Path(__file__).resolve().parents[1]
        env = {**os.environ, "GATEWAY_DATABASE_URL": database_url, "AUTO_CREATE_DB": "false"}
        result = subprocess.run(
            [sys.executable, "-m", "alembic", "upgrade", "head"],
            cwd=gateway_root,
            env=env,
            text=True,
            capture_output=True,
            check=False,
        )
        assert result.returncode == 0, result.stderr

        connection = await asyncpg.connect(database_url.replace("+asyncpg", ""))
        rows = await connection.fetch(
            """
            SELECT tablename
            FROM pg_tables
            WHERE schemaname = 'public'
            """
        )
        tables = {row["tablename"] for row in rows}
        await connection.close()

        assert {"clinic_sessions", "outbound_messages", "alembic_version"}.issubset(tables)
    finally:
        admin = await asyncpg.connect(admin_dsn)
        await admin.execute(f'DROP DATABASE IF EXISTS "{database_name}" WITH (FORCE)')
        await admin.close()
