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


async def mark_stale_sending_as_needs_review() -> bool:
    """Settle messages left in flight by a previous gateway process.

    Returns True when every message was reconciled with VoxCRM. Messages whose report
    could not be delivered (for example VoxCRM is still starting) keep their local state
    and are retried by the caller, so one failure never aborts the others.
    """
    all_settled = True
    async with SessionLocal() as session:
        result = await session.execute(
            select(OutboundMessage).where(
                OutboundMessage.state.in_(("Sending", "SentLocally"))
            )
        )
        messages = result.scalars().all()
        for message in messages:
            try:
                error ="Gateway restarted before the final delivery state was acknowledged."
                await voxcrm_client.report_status(
                    message.voxcrm_notification_id,
                    "NeedsReview",
                    last_error=error,
                    retry_count=message.retry_count,
                )
                message.state = "NeedsReview"
                message.last_error = error
                await write_gateway_audit(
                    level="Warning", category="WhatsApp", action="Gateway.StaleSendingRecovered",
                    message="Gateway restart sonrasi Sending durumundaki mesaj NeedsReview durumuna alindi.",
                    outcome="NeedsReview", clinic_id=message.clinic_id,
                    entity_type="WhatsAppNotification", entity_id=str(message.voxcrm_notification_id),
                    error_code="STALE_SENDING",
                )
            except Exception as exc:  # noqa: BLE001
                all_settled = False
                print(f"stale recovery failed for {message.voxcrm_notification_id}: {type(exc).__name__}: {exc!r}")
        await session.commit()
    return all_settled


async def poll_loop(stop_event: asyncio.Event) -> None:
    recovered = False

    while not stop_event.is_set():
        try:
            # Nothing may be claimed before leftovers of the previous process are settled,
            # otherwise freshly started sends would be mistaken for stale ones.
            if not recovered:
                recovered = await mark_stale_sending_as_needs_review()
            if recovered:
                await voxcrm_client.recover_expired_processing()
                await poll_once()
        except Exception as exc:  # noqa: BLE001
            print(f"poll error: {type(exc).__name__}: {exc!r}")

        try:
            await asyncio.wait_for(stop_event.wait(), timeout=settings.poll_interval_seconds)
        except TimeoutError:
            continue


async def poll_once() -> None:
    clinic_ids = await refresh_session_statuses()
    if not clinic_ids:
        return
    notifications = await voxcrm_client.claim_notifications(clinic_ids)

    by_clinic: dict[str, list[dict]] = {}
    for notification in notifications:
        by_clinic.setdefault(notification["clinicId"], []).append(notification)

    async def run_clinic(items: list[dict]) -> None:
        # Messages of one clinic stay sequential (anti-ban pacing), but one clinic's
        # waiting must not eat the claim lock of every other clinic.
        for item in items:
            try:
                await process_notification(item)
            except Exception as exc:  # noqa: BLE001
                print(f"notification {item.get('notificationId')} failed: {type(exc).__name__}: {exc!r}")

    await asyncio.gather(*(run_clinic(items) for items in by_clinic.values()))


async def refresh_session_statuses() -> list[uuid.UUID]:
    """Read actual device state even when nobody has the WhatsApp UI open."""
    async with SessionLocal() as session:
        ids = list((await session.scalars(select(ClinicSession.clinic_id))).all())
    semaphore = asyncio.Semaphore(5)

    async def refresh(clinic_id):
        async with semaphore:
            started_at = datetime.now(timezone.utc)
            try:
                payload = await asyncio.wait_for(worker_client.status(clinic_id), timeout=8)
                device_status = payload.get("status")
                if device_status not in {"ready", "disconnected", "connecting", "qr", "auth_failed"}:
                    device_status = "unavailable"
            except Exception:  # A failed status read must never leave a stale ready state.
                payload = {"lastError": "Cihaz bağlantı durumu doğrulanamadı. WhatsApp worker servisini kontrol edin."}
                device_status = "unavailable"
            async with SessionLocal() as session:
                row = await session.scalar(select(ClinicSession).where(ClinicSession.clinic_id == clinic_id))
                if row is None:
                    return None
                # Do not overwrite a connect/disconnect operation newer than this read.
                if row.updated_at and row.updated_at > started_at:
                    return clinic_id if row.status == "ready" else None
                previous = row.status
                row.status = device_status
                row.last_error = payload.get("lastError")
                row.qr = None
                if payload.get("connectedPhone"):
                    row.connected_phone = payload["connectedPhone"]
                row.updated_at = datetime.now(timezone.utc)
                await session.commit()
            if previous == "ready" and device_status != "ready":
                await write_gateway_audit(
                    level="Warning", category="WhatsAppSession", action="Gateway.DeviceDisconnected",
                    message="WhatsApp cihaz bağlantısı kesildi." if device_status != "unavailable" else "WhatsApp cihaz bağlantısı doğrulanamadı.",
                    outcome="Disconnected", clinic_id=clinic_id, metadata={"status": device_status},
                )
            elif previous != "ready" and device_status == "ready":
                await write_gateway_audit(
                    level="Info", category="WhatsAppSession", action="Gateway.DeviceReconnected",
                    message="WhatsApp cihaz bağlantısı yeniden kuruldu.", outcome="Success", clinic_id=clinic_id,
                )
            return clinic_id if device_status == "ready" else None

    return [item for item in await asyncio.gather(*(refresh(i) for i in ids)) if item is not None]


async def process_notification(notification: dict) -> None:
    notification_id = uuid.UUID(notification["notificationId"])
    clinic_id = uuid.UUID(notification["clinicId"])

    async with SessionLocal() as session:
        outbound = await get_or_create_outbound(session, notification, notification_id, clinic_id)
        local_state = outbound.state
        known_message_id = outbound.gateway_message_id
        known_error = outbound.last_error
        known_retry_count = outbound.retry_count

    # VoxCRM handed this message out again although the gateway already settled it
    # (typically because an earlier report was lost). Re-report instead of sending again.
    if local_state in {"SentLocally", "Sent"}:
        await voxcrm_client.report_status(
            notification_id, "Sent", gateway_message_id=known_message_id, retry_count=known_retry_count,
        )
        await set_outbound_state(notification_id, "Sent")
        return
    if local_state == "NeedsReview":
        await voxcrm_client.report_status(
            notification_id, "NeedsReview",
            last_error=known_error or "Alıcı doğrulanamadı; gönderim durduruldu.",
            retry_count=known_retry_count,
        )
        return

    await wait_for_clinic_rate_limit(clinic_id)

    await set_outbound_state(notification_id, "Sending", clear_error=True)

    try:
        validation = await voxcrm_client.validate_delivery(notification_id, clinic_id, notification["phoneNumber"], notification["messageContent"])
        if not validation.get("allowed"):
            if validation.get("retryable"):
                # The claim lock ran out before sending. Nothing was sent, VoxCRM requeues
                # the message itself; do not turn a timing problem into NeedsReview.
                await set_outbound_state(notification_id, "Claimed")
                return
            await set_outbound_state(
                notification_id, "NeedsReview",
                error=validation.get("error") or "Alıcı doğrulanamadı; gönderim durduruldu.",
            )
            return
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


async def set_outbound_state(
    notification_id: uuid.UUID, state: str, *, error: str | None = None, clear_error: bool = False,
) -> None:
    async with SessionLocal() as session:
        outbound = await find_outbound(session, notification_id)
        if outbound is None:
            return
        outbound.state = state
        if error is not None:
            outbound.last_error = error
        elif clear_error:
            outbound.last_error = None
        await session.commit()


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


async def wait_for_clinic_rate_limit(clinic_id: uuid.UUID) -> None:
    memory_last_send = _last_send_by_clinic.get(clinic_id)
    persisted_last_send = await get_last_send_at(clinic_id)
    candidates = [value for value in (memory_last_send, persisted_last_send) if value is not None]
    last_send = max(candidates) if candidates else None
    if last_send is None:
        return

    jitter = random.uniform(-settings.per_clinic_jitter_seconds, settings.per_clinic_jitter_seconds)
    interval = max(1, settings.per_clinic_send_interval_seconds + jitter)
    next_allowed = last_send + timedelta(seconds=interval)
    wait_seconds = (next_allowed - datetime.now(timezone.utc)).total_seconds()
    if wait_seconds > 0:
        await asyncio.sleep(wait_seconds)


async def get_last_send_at(clinic_id: uuid.UUID) -> datetime | None:
    async with SessionLocal() as session:
        result = await session.execute(
            select(OutboundMessage.updated_at)
            .where(
                OutboundMessage.clinic_id == clinic_id,
                OutboundMessage.state.in_(("SentLocally", "Sent")),
            )
            .order_by(OutboundMessage.updated_at.desc())
            .limit(1)
        )
        last_send = result.scalar_one_or_none()

    if last_send is None or last_send.tzinfo is not None:
        return last_send
    return last_send.replace(tzinfo=timezone.utc)
