import uuid
from datetime import datetime, timedelta, timezone

import pytest

from app.models import ClinicSession, OutboundMessage
from tests.conftest import gateway_token


@pytest.mark.asyncio
async def test_health_does_not_expose_operational_or_tenant_metrics(client, test_database, monkeypatch):
    clinic_id = uuid.uuid4()
    old_created_at = datetime.now(timezone.utc) - timedelta(minutes=3)
    async with test_database() as session:
        session.add(ClinicSession(clinic_id=clinic_id, status="ready"))
        session.add_all(
            [
                OutboundMessage(
                    voxcrm_notification_id=uuid.uuid4(),
                    clinic_id=clinic_id,
                    phone_number="+905551111111",
                    message_content="pending",
                    state="Claimed",
                    created_at=old_created_at,
                ),
                OutboundMessage(
                    voxcrm_notification_id=uuid.uuid4(),
                    clinic_id=clinic_id,
                    phone_number="+905552222222",
                    message_content="failed",
                    state="Failed",
                    last_error="worker failed",
                ),
                OutboundMessage(
                    voxcrm_notification_id=uuid.uuid4(),
                    clinic_id=clinic_id,
                    phone_number="+905553333333",
                    message_content="review",
                    state="NeedsReview",
                ),
            ]
        )
        await session.commit()

    from app.main import worker_client

    async def ok_health():
        return {"status": "ok"}

    monkeypatch.setattr(worker_client, "health", ok_health)

    response = await client.get("/api/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body == {"status": "ok", "service": "gateway-api"}


@pytest.mark.asyncio
async def test_health_is_degraded_when_worker_fails(client, monkeypatch):
    from app.main import worker_client

    async def failing_health():
        raise RuntimeError("worker down")

    monkeypatch.setattr(worker_client, "health", failing_health)

    response = await client.get("/api/health")

    assert response.status_code == 200
    assert response.json()["status"] == "degraded"


@pytest.mark.asyncio
async def test_clinic_status_metrics_are_scoped_by_clinic(client, test_database, monkeypatch):
    clinic_a = uuid.uuid4()
    clinic_b = uuid.uuid4()
    async with test_database() as session:
        session.add(ClinicSession(clinic_id=clinic_a, status="ready"))
        session.add_all(
            [
                message(clinic_a, "Claimed"),
                message(clinic_a, "Sending"),
                message(clinic_a, "RetryScheduled"),
                message(clinic_a, "Failed"),
                message(clinic_a, "NeedsReview"),
                message(clinic_a, "Sent"),
                message(clinic_b, "Failed"),
                message(clinic_b, "NeedsReview"),
            ]
        )
        await session.commit()

    from app.main import worker_client

    async def status(_clinic_id):
        return {"status": "ready", "connectedPhone": "+905550000000"}

    monkeypatch.setattr(worker_client, "status", status)

    response = await client.get(
        f"/api/clinics/{clinic_a}/whatsapp/status",
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.session.read', clinic_id=clinic_a)}"},
    )

    assert response.status_code == 200
    body = response.json()
    assert body["clinicId"] == str(clinic_a)
    assert body["pendingCount"] == 2
    assert body["retryScheduledCount"] == 1
    assert body["failedCount"] == 1
    assert body["needsReviewCount"] == 1
    assert body["lastSentAt"] is not None


@pytest.mark.asyncio
async def test_gateway_rejects_replayed_and_wrong_scope_tokens(client, monkeypatch):
    from app.main import worker_client

    async def status(_clinic_id):
        return {"status": "ready"}

    monkeypatch.setattr(worker_client, "status", status)

    clinic_id = uuid.uuid4()
    token = gateway_token("whatsapp.session.read", jti="replayed-token", clinic_id=clinic_id)
    first = await client.get(
        f"/api/clinics/{clinic_id}/whatsapp/status",
        headers={"Authorization": f"Bearer {token}"},
    )
    replay = await client.get(
        f"/api/clinics/{clinic_id}/whatsapp/status",
        headers={"Authorization": f"Bearer {token}"},
    )
    wrong_scope = await client.get(
        f"/api/clinics/{clinic_id}/whatsapp/status",
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.session.write', clinic_id=clinic_id)}"},
    )

    assert first.status_code == 200
    assert replay.status_code == 401
    assert wrong_scope.status_code == 403


@pytest.mark.asyncio
async def test_gateway_rejects_a_token_for_another_clinic(client, monkeypatch):
    from app.main import worker_client

    async def status(_clinic_id):
        return {"status": "ready"}

    monkeypatch.setattr(worker_client, "status", status)
    token_clinic = uuid.uuid4()
    requested_clinic = uuid.uuid4()

    response = await client.get(
        f"/api/clinics/{requested_clinic}/whatsapp/status",
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.session.read', clinic_id=token_clinic)}"},
    )

    assert response.status_code == 403


def message(clinic_id: uuid.UUID, state: str) -> OutboundMessage:
    return OutboundMessage(
        voxcrm_notification_id=uuid.uuid4(),
        clinic_id=clinic_id,
        phone_number="+905551111111",
        message_content="test",
        state=state,
    )
