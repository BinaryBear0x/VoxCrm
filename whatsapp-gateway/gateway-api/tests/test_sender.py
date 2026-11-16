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
async def test_startup_marks_stale_sent_locally_as_needs_review(test_database, monkeypatch):
    from app import sender

    reports = []
    notification_id = uuid.uuid4()
    async with test_database() as session:
        session.add(
            OutboundMessage(
                voxcrm_notification_id=notification_id,
                clinic_id=uuid.uuid4(),
                phone_number="+905551111111",
                message_content="sent but not acknowledged",
                state="SentLocally",
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
    assert reports[0][0][1] == "NeedsReview"


@pytest.mark.asyncio
async def test_per_clinic_rate_limit_waits_only_for_the_same_clinic(monkeypatch):
    from app import sender

    clinic_a = uuid.uuid4()
    clinic_b = uuid.uuid4()
    sender._last_send_by_clinic[clinic_a] = datetime.now(timezone.utc)
    monkeypatch.setattr(sender.settings, "per_clinic_send_interval_seconds", 10)
    monkeypatch.setattr(sender.settings, "per_clinic_jitter_seconds", 0)

    async def no_persisted_send(_clinic_id):
        return None

    sleeps = []

    async def fake_sleep(seconds):
        sleeps.append(seconds)

    monkeypatch.setattr(sender, "get_last_send_at", no_persisted_send)
    monkeypatch.setattr(sender.asyncio, "sleep", fake_sleep)

    await sender.wait_for_clinic_rate_limit(clinic_b)
    assert sleeps == []

    await sender.wait_for_clinic_rate_limit(clinic_a)
    assert len(sleeps) == 1
    assert sleeps[0] > 0


@pytest.mark.asyncio
async def test_per_clinic_rate_limit_survives_gateway_restart(test_database, monkeypatch):
    from app import sender

    clinic_id = uuid.uuid4()
    async with test_database() as session:
        session.add(
            OutboundMessage(
                voxcrm_notification_id=uuid.uuid4(),
                clinic_id=clinic_id,
                phone_number="+905551111111",
                message_content="already sent",
                state="Sent",
            )
        )
        await session.commit()

    sender._last_send_by_clinic.clear()
    monkeypatch.setattr(sender.settings, "per_clinic_send_interval_seconds", 60)
    monkeypatch.setattr(sender.settings, "per_clinic_jitter_seconds", 0)
    sleeps = []

    async def fake_sleep(seconds):
        sleeps.append(seconds)

    monkeypatch.setattr(sender.asyncio, "sleep", fake_sleep)

    await sender.wait_for_clinic_rate_limit(clinic_id)

    assert len(sleeps) == 1
    assert 0 < sleeps[0] <= 61


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


@pytest.mark.asyncio
async def test_changed_recipient_never_calls_worker(test_database, monkeypatch):
    from app import sender
    clinic_id, notification_id = uuid.uuid4(), uuid.uuid4()
    async def denied(*args):
        return {"allowed": False, "error": "Alıcı telefonu değişti."}
    async def forbidden_send(*args):
        pytest.fail("Recipient changed: worker must not be called")
    monkeypatch.setattr(sender.voxcrm_client, "validate_delivery", denied)
    monkeypatch.setattr(sender.worker_client, "send", forbidden_send)
    await sender.process_notification(notification(notification_id, clinic_id))
    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)
        assert outbound.state == "NeedsReview"
        assert outbound.last_error == "Alıcı telefonu değişti."


@pytest.mark.asyncio
async def test_poll_refreshes_disconnected_device_without_claiming_or_sending(test_database, monkeypatch):
    from app import sender
    from app.models import ClinicSession
    clinic_id = uuid.uuid4()
    events = []
    async with test_database() as session:
        session.add(ClinicSession(clinic_id=clinic_id, status="ready"))
        await session.commit()
    async def device_status(_clinic):
        return {"status": "disconnected", "lastError": "Connection closed"}
    async def forbidden(*args):
        pytest.fail("Offline devices must not claim or send messages")
    async def audit(**kwargs):
        events.append(kwargs["action"])
    monkeypatch.setattr(sender.worker_client, "status", device_status)
    monkeypatch.setattr(sender.worker_client, "send", forbidden)
    monkeypatch.setattr(sender.voxcrm_client, "claim_notifications", forbidden)
    monkeypatch.setattr(sender, "write_gateway_audit", audit)
    await sender.poll_once()
    await sender.poll_once()
    assert events == ["Gateway.DeviceDisconnected"]
    async with test_database() as session:
        row = await session.scalar(select(ClinicSession).where(ClinicSession.clinic_id == clinic_id))
        assert row.status == "disconnected"


@pytest.mark.asyncio
async def test_status_failure_is_not_kept_ready_and_reconnect_is_detected(test_database, monkeypatch):
    from app import sender
    from app.models import ClinicSession
    clinic_id = uuid.uuid4()
    async with test_database() as session:
        session.add(ClinicSession(clinic_id=clinic_id, status="ready"))
        await session.commit()
    async def failed(_clinic):
        raise TimeoutError("mock worker timeout")
    async def audit(**kwargs):
        return None
    monkeypatch.setattr(sender.worker_client, "status", failed)
    monkeypatch.setattr(sender, "write_gateway_audit", audit)
    assert await sender.refresh_session_statuses() == []
    async with test_database() as session:
        row = await session.scalar(select(ClinicSession).where(ClinicSession.clinic_id == clinic_id))
        assert row.status == "unavailable"
    async def ready(_clinic):
        return {"status": "ready"}
    monkeypatch.setattr(sender.worker_client, "status", ready)
    assert await sender.refresh_session_statuses() == [clinic_id]


@pytest.mark.asyncio
async def test_monitor_sql_detects_stale_status_and_excludes_future_retries(test_database):
    import re
    from pathlib import Path
    from sqlalchemy import text
    from app.models import ClinicSession
    clinic_id, stale_id = uuid.uuid4(), uuid.uuid4()
    async with test_database() as session:
        session.add(ClinicSession(clinic_id=clinic_id, status="ready"))
        session.add(ClinicSession(clinic_id=stale_id, status="ready", updated_at=datetime.now(timezone.utc) - timedelta(minutes=5)))
        session.add(OutboundMessage(clinic_id=clinic_id, voxcrm_notification_id=uuid.uuid4(), phone_number="905551111111", message_content="Fake test", state="RetryScheduled", next_attempt_at=datetime.now(timezone.utc) + timedelta(minutes=10)))
        await session.commit()
        source = (Path(__file__).resolve().parents[3] / "deploy/scripts/monitor-whatsapp.sh").read_text(encoding="utf-8")
        sql = re.search(r"WITH clinic_ids AS \([\s\S]+?GROUP BY[^;]+;", source).group(0)
        rows = {str(row[0]): row for row in (await session.execute(text(sql))).all()}
        assert rows[str(clinic_id)][1] == "ready"
        assert rows[str(clinic_id)][2] == 0
        assert rows[str(clinic_id)][4] == 0
        assert rows[str(stale_id)][1] == "status_stale"


def test_claim_batch_and_lock_fit_the_rate_limited_send_time(monkeypatch):
    from app import sender

    settings = sender.settings
    monkeypatch.setattr(settings, "default_batch_size", 10)
    monkeypatch.setattr(settings, "per_clinic_send_interval_seconds", 60)
    monkeypatch.setattr(settings, "per_clinic_jitter_seconds", 15)

    assert settings.claim_batch_size == 6
    assert settings.claim_lock_seconds <= settings.max_claim_lock_seconds
    # Every claimed message may wait one full interval before it is sent.
    assert settings.claim_batch_size * settings.per_message_budget_seconds <= settings.claim_lock_seconds

    monkeypatch.setattr(settings, "per_clinic_send_interval_seconds", 600)
    assert settings.claim_batch_size == 1
    assert settings.claim_lock_seconds <= settings.max_claim_lock_seconds


@pytest.mark.asyncio
async def test_expired_claim_lock_is_not_turned_into_needs_review(test_database, monkeypatch):
    from app import sender

    clinic_id, notification_id = uuid.uuid4(), uuid.uuid4()

    async def lock_expired(*args):
        return {"allowed": False, "retryable": True, "error": "Gönderim kilidi geçersiz."}

    async def forbidden_send(*args):
        pytest.fail("A message with an expired lock must not be sent")

    monkeypatch.setattr(sender.voxcrm_client, "validate_delivery", lock_expired)
    monkeypatch.setattr(sender.worker_client, "send", forbidden_send)

    await sender.process_notification(notification(notification_id, clinic_id))

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)
    assert outbound.state == "Claimed"


@pytest.mark.asyncio
async def test_reclaimed_sent_message_is_reported_again_instead_of_sent_twice(test_database, monkeypatch):
    from app import sender

    reports = []
    clinic_id, notification_id = uuid.uuid4(), uuid.uuid4()
    async with test_database() as session:
        session.add(
            OutboundMessage(
                voxcrm_notification_id=notification_id,
                clinic_id=clinic_id,
                phone_number="+905551111111",
                message_content="Asi hatirlatma",
                state="SentLocally",
                gateway_message_id="wa-lost-report",
            )
        )
        await session.commit()

    async def report_status(*args, **kwargs):
        reports.append((args, kwargs))
        return True

    async def forbidden_send(*args):
        pytest.fail("The message was already delivered; it must not be sent again")

    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)
    monkeypatch.setattr(sender.worker_client, "send", forbidden_send)

    await sender.process_notification(notification(notification_id, clinic_id))

    async with test_database() as session:
        outbound = await find_outbound(session, notification_id)
    assert outbound.state == "Sent"
    assert reports[0][0][1] == "Sent"
    assert reports[0][1]["gateway_message_id"] == "wa-lost-report"


@pytest.mark.asyncio
async def test_one_failing_notification_does_not_stop_the_rest_of_the_batch(monkeypatch):
    from app import sender

    clinic_a, clinic_b = uuid.uuid4(), uuid.uuid4()
    processed = []

    async def ready_clinics():
        return [clinic_a, clinic_b]

    async def claim(_ids):
        return [notification(uuid.uuid4(), clinic_a), notification(uuid.uuid4(), clinic_b)]

    async def process(item):
        processed.append(item["clinicId"])
        if item["clinicId"] == str(clinic_a):
            raise RuntimeError("boom")

    monkeypatch.setattr(sender, "refresh_session_statuses", ready_clinics)
    monkeypatch.setattr(sender.voxcrm_client, "claim_notifications", claim)
    monkeypatch.setattr(sender, "process_notification", process)

    await sender.poll_once()

    assert sorted(processed) == sorted([str(clinic_a), str(clinic_b)])


@pytest.mark.asyncio
async def test_stale_recovery_continues_after_a_failed_report(test_database, monkeypatch):
    from app import sender

    first, second = uuid.uuid4(), uuid.uuid4()
    async with test_database() as session:
        for notification_id in (first, second):
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

    async def report_status(notification_id, *args, **kwargs):
        if notification_id == first:
            raise RuntimeError("VoxCRM is not reachable yet")

    monkeypatch.setattr(sender.voxcrm_client, "report_status", report_status)

    assert await sender.mark_stale_sending_as_needs_review() is False

    async with test_database() as session:
        # The unreported message keeps its state so the next attempt can settle it.
        assert (await find_outbound(session, first)).state == "Sending"
        assert (await find_outbound(session, second)).state == "NeedsReview"


@pytest.mark.asyncio
async def test_poll_loop_survives_voxcrm_being_down_and_waits_for_recovery(monkeypatch):
    import asyncio
    from app import sender

    monkeypatch.setattr(sender.settings, "poll_interval_seconds", 0.01)
    events = []
    attempts = {"count": 0}

    async def stale_recovery():
        attempts["count"] += 1
        if attempts["count"] < 3:
            raise RuntimeError("VoxCRM is starting")
        return True

    async def recover():
        events.append("recover")

    async def poll():
        events.append("poll")

    monkeypatch.setattr(sender, "mark_stale_sending_as_needs_review", stale_recovery)
    monkeypatch.setattr(sender.voxcrm_client, "recover_expired_processing", recover)
    monkeypatch.setattr(sender, "poll_once", poll)

    stop = asyncio.Event()
    task = asyncio.create_task(sender.poll_loop(stop))
    await asyncio.sleep(0.3)
    stop.set()
    await asyncio.wait_for(task, timeout=2)

    assert attempts["count"] >= 3
    assert "poll" in events  # the loop kept running after the early failures
