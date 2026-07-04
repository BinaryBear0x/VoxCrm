import uuid
from datetime import datetime, timedelta, timezone

import pytest
from sqlalchemy import select

from app.models import OutboundMessage


@pytest.mark.asyncio
async def test_successful_send_moves_outbound_to_sent(test_database, monkeypatch):
    from app import sender

    reports = []
    clinic_id = uuid.uuid4()
    notification_id = uuid.uuid4()

    async def send(_clinic_id, _phone_number, _message, _notification_id):
        return {"ok": True, "messageId": "wa-message-1"}

    async def report_status(*args, **kwargs):
        reports.append((args, kwargs))

    monkeypatch.setattr(sender.worker_client, "send", send)
    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)

    await sender.process_notification(notification(notification_id, clinic_id))

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)

    assert outbound is not None
    assert outbound.state == "Sent"
    assert outbound.gateway_message_id == "wa-message-1"
    assert reports[0][0][1] == "Sent"


@pytest.mark.asyncio
async def test_transient_worker_failure_schedules_retry(test_database, monkeypatch):
    from app import sender

    reports = []
    clinic_id = uuid.uuid4()
    notification_id = uuid.uuid4()

    async def send(_clinic_id, _phone_number, _message, _notification_id):
        return {"ok": False, "error": "session disconnected", "errorCode": "SESSION_NOT_READY"}

    async def report_status(*args, **kwargs):
        reports.append((args, kwargs))

    monkeypatch.setattr(sender.worker_client, "send", send)
    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)

    before = datetime.now(timezone.utc)
    await sender.process_notification(notification(notification_id, clinic_id))

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)

    assert outbound is not None
    assert outbound.state == "RetryScheduled"
    assert outbound.retry_count == 1
    assert outbound.next_attempt_at is not None
    assert timedelta(minutes=4, seconds=50) <= outbound.next_attempt_at - before <= timedelta(minutes=5, seconds=30)
    assert reports[0][0][1] == "RetryScheduled"
    assert reports[0][1]["retry_count"] == 1


@pytest.mark.asyncio
async def test_permanent_worker_failure_does_not_retry(test_database, monkeypatch):
    from app import sender

    reports = []
    clinic_id = uuid.uuid4()
    notification_id = uuid.uuid4()

    async def send(_clinic_id, _phone_number, _message, _notification_id):
        return {"ok": False, "error": "invalid phone", "errorCode": "INVALID_PHONE"}

    async def report_status(*args, **kwargs):
        reports.append((args, kwargs))

    monkeypatch.setattr(sender.worker_client, "send", send)
    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)

    await sender.process_notification(notification(notification_id, clinic_id))

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)

    assert outbound is not None
    assert outbound.state == "Failed"
    assert outbound.retry_count == 1
    assert reports[0][0][1] == "Failed"


@pytest.mark.asyncio
async def test_startup_marks_stale_sending_as_needs_review(test_database, monkeypatch):
    from app import sender

    reports = []
    notification_id = uuid.uuid4()
    async with test_database() as session:
        session.add(
            OutboundMessage(
                voxcrm_notification_id=notification_id,
                clinic_id=uuid.uuid4(),
                phone_number="+905551111111",
                message_content="stale",
                state="Sending",
            )
        )
        await session.commit()

    async def report_status(*args, **kwargs):
        reports.append((args, kwargs))

    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)

    await sender.mark_stale_sending_as_needs_review()

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)

    assert outbound is not None
    assert outbound.state == "NeedsReview"
    assert "restarted" in outbound.last_error
    assert reports[0][0][1] == "NeedsReview"


@pytest.mark.asyncio
async def test_per_clinic_rate_limit_waits_only_for_the_same_clinic(monkeypatch):
    from app import sender

    clinic_a = uuid.uuid4()
    clinic_b = uuid.uuid4()
    sender._last_send_by_clinic[clinic_a] = datetime.now(timezone.utc)
    monkeypatch.setattr(sender.settings, "per_clinic_send_interval_seconds", 10)
    monkeypatch.setattr(sender.settings, "per_clinic_jitter_seconds", 0)

    sleeps = []

    async def fake_sleep(seconds):
        sleeps.append(seconds)

    monkeypatch.setattr(sender.asyncio, "sleep", fake_sleep)

    await sender.wait_for_clinic_rate_limit(clinic_b)
    assert sleeps == []

    await sender.wait_for_clinic_rate_limit(clinic_a)
    assert len(sleeps) == 1
    assert sleeps[0] > 0


def notification(notification_id: uuid.UUID, clinic_id: uuid.UUID) -> dict:
    return {
        "notificationId": str(notification_id),
        "clinicId": str(clinic_id),
        "phoneNumber": "+905551111111",
        "messageContent": "Asi hatirlatma",
        "retryCount": 0,
    }


async def find_outbound(session, notification_id: uuid.UUID) -> OutboundMessage | None:
    result = await session.execute(
        select(OutboundMessage).where(OutboundMessage.voxcrm_notification_id == notification_id)
    )
    return result.scalar_one_or_none()
