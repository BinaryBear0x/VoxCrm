import asyncio
import random
import uuid
from datetime import datetime, timedelta, timezone

from sqlalchemy import select
from sqlalchemy.dialects.postgresql import insert
from sqlalchemy.ext.asyncio import AsyncSession

from app.config import settings
from app.database import SessionLocal
from app.models import ClinicSession, OutboundMessage
from app.voxcrm_client import voxcrm_client
from app.worker_client import worker_client

TRANSIENT_ERROR_CODES = {"SESSION_NOT_READY", "NETWORK_TIMEOUT", "WORKER_TRANSIENT"}
RETRY_DELAYS = (timedelta(minutes=5), timedelta(minutes=30), timedelta(hours=2))
_last_send_by_clinic: dict[uuid.UUID, datetime] = {}


async def mark_stale_sending_as_needs_review() -> None:
    async with SessionLocal() as session:
        result = await session.execute(
            select(OutboundMessage).where(OutboundMessage.state == "Sending")
        )
        messages = result.scalars().all()
        for message in messages:
            message.state = "NeedsReview"
            message.last_error = "Gateway restarted while message was in Sending state."
            await voxcrm_client.report_status(
                message.voxcrm_notification_id,
                "NeedsReview",
                last_error=message.last_error,
                retry_count=message.retry_count,
            )
            await write_gateway_audit(
                level="Warning", category="WhatsApp", action="Gateway.StaleSendingRecovered",
                message="Gateway restart sonrasi Sending durumundaki mesaj NeedsReview durumuna alindi.",
                outcome="NeedsReview", clinic_id=message.clinic_id,
                entity_type="WhatsAppNotification", entity_id=str(message.voxcrm_notification_id),
                error_code="STALE_SENDING",
            )
        await session.commit()


async def poll_loop(stop_event: asyncio.Event) -> None:
    await mark_stale_sending_as_needs_review()

    while not stop_event.is_set():
        try:
            await voxcrm_client.recover_expired_processing()
            await poll_once()
        except Exception as exc:  # noqa: BLE001
            print(f"poll error: {exc}")

        try:
            await asyncio.wait_for(stop_event.wait(), timeout=settings.poll_interval_seconds)
        except TimeoutError:
            continue


async def poll_once() -> None:
    async with SessionLocal() as session:
        result = await session.execute(
            select(ClinicSession).where(ClinicSession.status == "ready")
        )
        ready_sessions = result.scalars().all()

    if not ready_sessions:
        return

    clinic_ids = [item.clinic_id for item in ready_sessions]
    notifications = await voxcrm_client.claim_notifications(clinic_ids)

    for notification in sorted(notifications, key=next_send_sort_key):
        await process_notification(notification)


async def process_notification(notification: dict) -> None:
    notification_id = uuid.UUID(notification["notificationId"])
    clinic_id = uuid.UUID(notification["clinicId"])

    await wait_for_clinic_rate_limit(clinic_id)

    async with SessionLocal() as session:
        outbound = await get_or_create_outbound(session, notification, notification_id, clinic_id)
        if outbound.state in {"SentLocally", "Sent", "NeedsReview"}:
            return

        outbound.state = "Sending"
        outbound.last_error = None
        await session.commit()

    try:
        response = await worker_client.send(
            clinic_id,
            notification["phoneNumber"],
            notification["messageContent"],
            notification_id,
        )
    except Exception as exc:  # noqa: BLE001
        await handle_failure(notification_id, clinic_id, str(exc), "WORKER_TRANSIENT")
        return

    if response.get("ok"):
        _last_send_by_clinic[clinic_id] = datetime.now(timezone.utc)
        gateway_message_id = response.get("messageId")
        async with SessionLocal() as session:
            outbound = await find_outbound(session, notification_id)
            if outbound is None:
                return
            outbound.state = "SentLocally"
            outbound.gateway_message_id = gateway_message_id
            outbound.last_error = None
            await session.commit()

        await voxcrm_client.report_status(
            notification_id,
            "Sent",
            gateway_message_id=gateway_message_id,
            retry_count=response.get("retryCount"),
        )
        await write_gateway_audit(
            level="Info", category="WhatsApp", action="Gateway.MessageSent",
            message="WhatsApp mesaji gateway tarafindan basariyla gonderildi.", outcome="Success",
            clinic_id=clinic_id, entity_type="WhatsAppNotification", entity_id=str(notification_id),
            metadata={"gatewayMessageId": gateway_message_id},
        )
        async with SessionLocal() as session:
            outbound = await find_outbound(session, notification_id)
            if outbound:
                outbound.state = "Sent"
                await session.commit()
        return

    await handle_failure(
        notification_id,
        clinic_id,
        response.get("error") or "Unknown worker error",
        response.get("errorCode") or "WORKER_TRANSIENT",
    )


async def get_or_create_outbound(
    session: AsyncSession,
    notification: dict,
    notification_id: uuid.UUID,
    clinic_id: uuid.UUID,
) -> OutboundMessage:
    stmt = insert(OutboundMessage).values(
        voxcrm_notification_id=notification_id,
        clinic_id=clinic_id,
        phone_number=notification["phoneNumber"],
        message_content=notification["messageContent"],
        retry_count=notification.get("retryCount", 0),
        state="Claimed",
    ).on_conflict_do_nothing(
        index_elements=["voxcrm_notification_id"],
    )
    await session.execute(stmt)
    await session.commit()

    outbound = await find_outbound(session, notification_id)
    if outbound is None:
        raise RuntimeError("OutboundMessage could not be created.")
    return outbound


async def find_outbound(session: AsyncSession, notification_id: uuid.UUID) -> OutboundMessage | None:
    result = await session.execute(
        select(OutboundMessage).where(OutboundMessage.voxcrm_notification_id == notification_id)
    )
    return result.scalar_one_or_none()


async def handle_failure(notification_id: uuid.UUID, clinic_id: uuid.UUID, error: str, error_code: str) -> None:
    async with SessionLocal() as session:
        outbound = await find_outbound(session, notification_id)
        if outbound is None:
            return

        is_transient = error_code in TRANSIENT_ERROR_CODES
        next_retry_count = outbound.retry_count + 1

        if is_transient and next_retry_count <= settings.max_retry_count:
            delay = RETRY_DELAYS[min(next_retry_count - 1, len(RETRY_DELAYS) - 1)]
            next_attempt_at = datetime.now(timezone.utc) + delay
            outbound.state = "RetryScheduled"
            outbound.retry_count = next_retry_count
            outbound.next_attempt_at = next_attempt_at
            outbound.last_error = error
            await session.commit()

            await voxcrm_client.report_status(
                notification_id,
                "RetryScheduled",
                last_error=error,
                retry_count=next_retry_count,
                next_attempt_at=next_attempt_at,
            )
            await write_gateway_audit(
                level="Warning", category="WhatsApp", action="Gateway.MessageRetryScheduled",
                message="WhatsApp mesaji gecici hata nedeniyle tekrar denenecek.", outcome="RetryScheduled",
                clinic_id=clinic_id, entity_type="WhatsAppNotification", entity_id=str(notification_id),
                error_code=error_code,
                metadata={"retryCount": next_retry_count, "nextAttemptAt": next_attempt_at.isoformat()},
            )
            await update_session_error(clinic_id, error)
            return

        outbound.state = "Failed"
        outbound.retry_count = next_retry_count
        outbound.last_error = error
        await session.commit()

    await voxcrm_client.report_status(
        notification_id,
        "Failed",
        last_error=error,
        retry_count=next_retry_count,
    )
    await write_gateway_audit(
        level="Error", category="WhatsApp", action="Gateway.MessageFailed",
        message="WhatsApp mesaji kalici hata nedeniyle basarisiz oldu.", outcome="Failure",
        clinic_id=clinic_id, entity_type="WhatsAppNotification", entity_id=str(notification_id),
        error_code=error_code, metadata={"retryCount": next_retry_count},
    )
    await update_session_error(clinic_id, error)


async def update_session_error(clinic_id: uuid.UUID, error: str) -> None:
    async with SessionLocal() as session:
        result = await session.execute(
            select(ClinicSession).where(ClinicSession.clinic_id == clinic_id)
        )
        clinic_session = result.scalar_one_or_none()
        if clinic_session:
            clinic_session.last_error = error
            await session.commit()


async def write_gateway_audit(
    *, level: str, category: str, action: str, message: str, outcome: str,
    clinic_id: uuid.UUID | None = None, entity_type: str | None = None,
    entity_id: str | None = None, error_code: str | None = None,
    metadata: dict | None = None,
) -> None:
    try:
        await voxcrm_client.write_audit(
            level=level, category=category, action=action, message=message, outcome=outcome,
            clinic_id=clinic_id, entity_type=entity_type, entity_id=entity_id,
            error_code=error_code, metadata=metadata,
        )
    except Exception as exc:  # noqa: BLE001
        print(f"audit write failed: {exc}")


def next_send_sort_key(notification: dict) -> datetime:
    clinic_id = uuid.UUID(notification["clinicId"])
    last_send = _last_send_by_clinic.get(clinic_id)
    if last_send is None:
        return datetime.min.replace(tzinfo=timezone.utc)
    return last_send + timedelta(seconds=settings.per_clinic_send_interval_seconds)


async def wait_for_clinic_rate_limit(clinic_id: uuid.UUID) -> None:
    last_send = _last_send_by_clinic.get(clinic_id)
    if last_send is None:
        return

    jitter = random.uniform(-settings.per_clinic_jitter_seconds, settings.per_clinic_jitter_seconds)
    interval = max(1, settings.per_clinic_send_interval_seconds + jitter)
    next_allowed = last_send + timedelta(seconds=interval)
    wait_seconds = (next_allowed - datetime.now(timezone.utc)).total_seconds()
    if wait_seconds > 0:
        await asyncio.sleep(wait_seconds)
