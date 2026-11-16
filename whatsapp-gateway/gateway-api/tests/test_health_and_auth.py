import uuid
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import select

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
async def test_internal_health_requires_scope_and_returns_operational_details(
    client, test_database, monkeypatch
):
    from app.main import worker_client

    async def ok_health():
        return {"status": "ok", "service": "wa-worker", "sessions": 1}

    monkeypatch.setattr(worker_client, "health", ok_health)

    unauthenticated = await client.get("/api/internal/health")
    wrong_scope = await client.get(
        "/api/internal/health",
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.session.read')}"},
    )
    authorized = await client.get(
        "/api/internal/health",
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.health.read')}"},
    )

    assert unauthenticated.status_code == 401
    assert wrong_scope.status_code == 403
    assert authorized.status_code == 200
    body = authorized.json()
    assert body["status"] == "ok"
    assert body["worker"]["service"] == "wa-worker"
    assert "database" in body
    assert "readyClinicCount" in body


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


@pytest.mark.asyncio
async def test_direct_send_requires_clinic_scope_and_bypasses_outbound_queue(client, test_database, monkeypatch):
    from app import main

    clinic_id = uuid.uuid4()
    request_id = uuid.uuid4()
    calls = []

    async def send(sent_clinic_id, phone, message_text, sent_request_id):
        calls.append((sent_clinic_id, phone, message_text, sent_request_id))
        return {"ok": True, "messageId": "direct-message-1"}

    async def audit(**_kwargs):
        return None

    monkeypatch.setattr(main.worker_client, "send", send)
    monkeypatch.setattr(main, "write_gateway_audit", audit)
    payload = {
        "phone": "905551111111",
        "message": "Aşı hatırlatması",
        "requestId": str(request_id),
    }

    wrong_scope = await client.post(
        f"/api/clinics/{clinic_id}/whatsapp/send",
        json=payload,
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.session.write', clinic_id=clinic_id)}"},
    )
    sent = await client.post(
        f"/api/clinics/{clinic_id}/whatsapp/send",
        json=payload,
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.message.write', clinic_id=clinic_id)}"},
    )

    assert wrong_scope.status_code == 403
    assert sent.status_code == 200
    assert sent.json() == {"ok": True, "messageId": "direct-message-1"}
    assert calls == [(clinic_id, "905551111111", "Aşı hatırlatması", request_id)]
    async with test_database() as session:
        assert not (await session.execute(select(OutboundMessage))).scalars().all()


@pytest.mark.asyncio
async def test_direct_send_returns_failure_without_creating_a_queue_row(client, test_database, monkeypatch):
    from app import main

    clinic_id = uuid.uuid4()

    async def send(*_args):
        return {"ok": False, "errorCode": "NOT_REGISTERED", "error": "Phone number is not registered."}

    async def audit(**_kwargs):
        return None

    monkeypatch.setattr(main.worker_client, "send", send)
    monkeypatch.setattr(main, "write_gateway_audit", audit)
    response = await client.post(
        f"/api/clinics/{clinic_id}/whatsapp/send",
        json={"phone": "905551111111", "message": "Hatırlatma", "requestId": str(uuid.uuid4())},
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.message.write', clinic_id=clinic_id)}"},
    )

    assert response.status_code == 422
    assert response.json()["errorCode"] == "NOT_REGISTERED"
    async with test_database() as session:
        assert not (await session.execute(select(OutboundMessage))).scalars().all()


def message(clinic_id: uuid.UUID, state: str) -> OutboundMessage:
    return OutboundMessage(
        voxcrm_notification_id=uuid.uuid4(),
        clinic_id=clinic_id,
        phone_number="+905551111111",
        message_content="test",
        state=state,
    )


@pytest.mark.asyncio
async def test_direct_send_stops_when_recipient_validation_fails(client, monkeypatch):
    from app import main
    clinic_id = uuid.uuid4()
    async def denied(*args):
        return {"allowed": False, "error": "WhatsApp izni kaldırıldı."}
    async def forbidden_send(*args):
        pytest.fail("Recipient validation failed: worker must not be called")
    monkeypatch.setattr(main.voxcrm_client, "validate_delivery", denied)
    monkeypatch.setattr(main.worker_client, "send", forbidden_send)
    response = await client.post(
        f"/api/clinics/{clinic_id}/whatsapp/send",
        json={"phone": "905551111111", "message": "Test", "requestId": str(uuid.uuid4())},
        headers={"Authorization": f"Bearer {gateway_token('whatsapp.message.write', clinic_id=clinic_id)}"},
    )
    assert response.status_code == 422
    assert response.json()["errorCode"] == "RECIPIENT_CHANGED"


@pytest.mark.asyncio
@pytest.mark.parametrize("upstream, expected", [(400, 422), (422, 422), (500, 502), (404, 502)])
async def test_inbound_maps_voxcrm_refusals_so_the_worker_can_tell_bad_messages_from_outages(
    client, monkeypatch, upstream, expected
):
    import httpx
    from app import main
    from app.config import settings

    async def refuse(*args, **kwargs):
        request = httpx.Request("POST", "http://voxcrm/api/whatsapp/inbound")
        raise httpx.HTTPStatusError("refused", request=request, response=httpx.Response(upstream, request=request))

    monkeypatch.setattr(main.voxcrm_client, "write_inbound", refuse)

    response = await client.post(
        "/internal/worker/inbound",
        headers={"x-internal-token": settings.worker_internal_token},
        json={
            "clinicId": "73ccade0-16f3-4d2d-b55d-fe152ae4d05b",
            "fromPhone": "905551112233@s.whatsapp.net",
            "message": "Merhaba",
            "gatewaySessionId": "73ccade0-16f3-4d2d-b55d-fe152ae4d05b",
            "providerMessageId": "m-1",
        },
    )

    assert response.status_code == expected
