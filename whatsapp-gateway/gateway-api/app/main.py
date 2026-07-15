import asyncio
import hmac
import uuid
from contextlib import asynccontextmanager
from datetime import datetime, timezone

from fastapi import Depends, FastAPI, Header, HTTPException, status
import httpx
from sqlalchemy import func, select
from sqlalchemy.ext.asyncio import AsyncSession

from app.auth import GatewayPrincipal, require_scope
from app.config import settings
from app.database import get_session, init_db
from app.models import ClinicSession
from app.models import OutboundMessage
from app.schemas import InboundFromWorker, QrResponse, SessionStatus
from app.sender import poll_loop, write_gateway_audit
from app.voxcrm_client import voxcrm_client
from app.worker_client import worker_client


@asynccontextmanager
async def lifespan(app: FastAPI):
    settings.validate_runtime()
    if settings.auto_create_db:
        await init_db()
    stop_event = asyncio.Event()
    poll_task = asyncio.create_task(poll_loop(stop_event))
    yield
    stop_event.set()
    await poll_task


app = FastAPI(title="VoxCrm WhatsApp Gateway", lifespan=lifespan)


@app.get("/api/health")
async def health(session: AsyncSession = Depends(get_session)) -> dict:
    worker = None
    worker_ok = True
    try:
        worker = await worker_client.health()
    except Exception as exc:  # noqa: BLE001
        worker_ok = False
        worker = {"status": "error", "error": str(exc)}

    metrics = await global_metrics(session)

    # This endpoint is intentionally safe for liveness probes. Tenant and
    # operational details belong to the authenticated clinic endpoints.
    return {
        "status": "ok" if worker_ok and metrics["database"] == "ok" else "degraded",
        "service": "gateway-api",
    }


@app.post(
    "/api/clinics/{clinic_id}/whatsapp/connect",
)
async def connect(
    clinic_id: uuid.UUID,
    principal: GatewayPrincipal = Depends(require_scope("whatsapp.session.write", clinic_bound=True)),
    session: AsyncSession = Depends(get_session),
) -> dict:
    ensure_clinic_scope(principal, clinic_id)
    try:
        response = await worker_client.connect(clinic_id)
    except httpx.HTTPError as exc:
        await mark_session_worker_error(session, clinic_id, exc)
        await write_gateway_audit(
            level="Error", category="WhatsAppSession", action="Gateway.SessionConnectFailed",
            message="WhatsApp worker baglanti istegine hata dondu.", outcome="Failure",
            clinic_id=clinic_id, error_code="WORKER_UNAVAILABLE",
            metadata={"exceptionType": type(exc).__name__},
        )
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="WhatsApp worker'a ulasilamadi. Lutfen birkac saniye sonra tekrar deneyin.",
        ) from exc

    await upsert_session_from_worker(session, clinic_id, response)
    await write_gateway_audit(
        level="Info", category="WhatsAppSession", action="Gateway.SessionConnectRequested",
        message="WhatsApp baglanti istegi worker'a iletildi.", outcome="Success",
        clinic_id=clinic_id, metadata={"workerStatus": response.get("status")},
    )
    return {"ok": True}


@app.post(
    "/api/clinics/{clinic_id}/whatsapp/disconnect",
)
async def disconnect(
    clinic_id: uuid.UUID,
    principal: GatewayPrincipal = Depends(require_scope("whatsapp.session.write", clinic_bound=True)),
    session: AsyncSession = Depends(get_session),
) -> dict:
    ensure_clinic_scope(principal, clinic_id)
    try:
        response = await worker_client.disconnect(clinic_id)
    except httpx.HTTPError as exc:
        await mark_session_worker_error(session, clinic_id, exc)
        await write_gateway_audit(
            level="Error", category="WhatsAppSession", action="Gateway.SessionDisconnectFailed",
            message="WhatsApp worker disconnect istegine hata dondu.", outcome="Failure",
            clinic_id=clinic_id, error_code="WORKER_UNAVAILABLE",
            metadata={"exceptionType": type(exc).__name__},
        )
        raise HTTPException(
            status_code=status.HTTP_502_BAD_GATEWAY,
            detail="WhatsApp worker'a ulasilamadi. Lutfen birkac saniye sonra tekrar deneyin.",
        ) from exc

    await upsert_session_from_worker(session, clinic_id, response)
    await write_gateway_audit(
        level="Info", category="WhatsAppSession", action="Gateway.SessionDisconnected",
        message="WhatsApp session disconnect istegi basariyla isledi.", outcome="Success",
        clinic_id=clinic_id, metadata={"workerStatus": response.get("status")},
    )
    return {"ok": True}


@app.get(
    "/api/clinics/{clinic_id}/whatsapp/status",
    response_model=SessionStatus,
)
async def get_status(
    clinic_id: uuid.UUID,
    principal: GatewayPrincipal = Depends(require_scope("whatsapp.session.read", clinic_bound=True)),
    session: AsyncSession = Depends(get_session),
) -> SessionStatus:
    ensure_clinic_scope(principal, clinic_id)
    try:
        worker_status = await worker_client.status(clinic_id)
        clinic_session = await upsert_session_from_worker(session, clinic_id, worker_status)
    except Exception:
        clinic_session = await get_or_create_session(session, clinic_id)

    metrics = await clinic_metrics(session, clinic_id)

    return SessionStatus(
        clinic_id=clinic_id,
        status=clinic_session.status,
        connected_phone=clinic_session.connected_phone,
        last_seen_at=clinic_session.last_seen_at,
        last_error=clinic_session.last_error,
        **metrics,
    )


@app.get(
    "/api/clinics/{clinic_id}/whatsapp/qr",
    response_model=QrResponse,
)
async def get_qr(
    clinic_id: uuid.UUID,
    principal: GatewayPrincipal = Depends(require_scope("whatsapp.session.read", clinic_bound=True)),
    session: AsyncSession = Depends(get_session),
) -> QrResponse:
    ensure_clinic_scope(principal, clinic_id)
    try:
        worker_qr = await worker_client.qr(clinic_id)
        clinic_session = await upsert_session_from_worker(session, clinic_id, worker_qr)
        live_qr = worker_qr.get("qr")
    except Exception:
        clinic_session = await get_or_create_session(session, clinic_id)
        live_qr = None

    return QrResponse(
        clinic_id=clinic_id,
        qr=live_qr,
        updated_at=clinic_session.updated_at,
        status=clinic_session.status,
    )


@app.post("/internal/worker/inbound")
async def worker_inbound(
    payload: InboundFromWorker,
    x_internal_token: str | None = Header(default=None),
) -> dict:
    if not x_internal_token or not hmac.compare_digest(x_internal_token, settings.worker_internal_token):
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED)

    await voxcrm_client.write_inbound(
        payload.clinic_id,
        payload.from_phone,
        payload.message,
        payload.received_at or datetime.now(timezone.utc),
        payload.gateway_session_id,
        payload.provider_message_id,
    )
    await write_gateway_audit(
        level="Info", category="WhatsAppInbound", action="Gateway.InboundReceived",
        message="Hasta sahibinden gelen WhatsApp mesaji otomatik cevap verilmeden kaydedildi.",
        outcome="Success", clinic_id=payload.clinic_id,
        metadata={"gatewaySessionId": payload.gateway_session_id,
                  "providerMessageId": payload.provider_message_id},
    )
    return {"ok": True}


async def get_or_create_session(session: AsyncSession, clinic_id: uuid.UUID) -> ClinicSession:
    result = await session.execute(
        select(ClinicSession).where(ClinicSession.clinic_id == clinic_id)
    )
    clinic_session = result.scalar_one_or_none()
    if clinic_session is None:
        clinic_session = ClinicSession(clinic_id=clinic_id, status="disconnected")
        session.add(clinic_session)
        await session.commit()
        await session.refresh(clinic_session)
    return clinic_session


async def upsert_session_from_worker(session: AsyncSession, clinic_id: uuid.UUID, payload: dict) -> ClinicSession:
    clinic_session = await get_or_create_session(session, clinic_id)
    clinic_session.status = payload.get("status", clinic_session.status)
    # QR codes are short-lived credentials. Never persist them in the database.
    clinic_session.qr = None
    clinic_session.connected_phone = payload.get("connectedPhone", clinic_session.connected_phone)
    clinic_session.last_error = payload.get("lastError")

    last_seen = payload.get("lastSeenAt")
    if last_seen:
        clinic_session.last_seen_at = datetime.fromisoformat(last_seen.replace("Z", "+00:00"))

    await session.commit()
    await session.refresh(clinic_session)
    return clinic_session


async def mark_session_worker_error(session: AsyncSession, clinic_id: uuid.UUID, exc: Exception) -> None:
    clinic_session = await get_or_create_session(session, clinic_id)
    clinic_session.status = "disconnected"
    clinic_session.last_error = str(exc)
    await session.commit()


async def global_metrics(session: AsyncSession) -> dict:
    try:
        session_count = await session.scalar(select(func.count()).select_from(ClinicSession))
        ready_count = await session.scalar(
            select(func.count()).select_from(ClinicSession).where(ClinicSession.status == "ready")
        )
        failed_count = await session.scalar(
            select(func.count()).select_from(OutboundMessage).where(OutboundMessage.state == "Failed")
        )
        needs_review_count = await session.scalar(
            select(func.count()).select_from(OutboundMessage).where(OutboundMessage.state == "NeedsReview")
        )
        last_send_at = await session.scalar(
            select(func.max(OutboundMessage.updated_at)).where(OutboundMessage.state == "Sent")
        )
        oldest_open = await session.scalar(
            select(func.min(OutboundMessage.created_at)).where(
                OutboundMessage.state.in_(["Claimed", "Sending", "RetryScheduled"])
            )
        )
        last_error = await session.scalar(
            select(OutboundMessage.last_error)
            .where(OutboundMessage.last_error.is_not(None))
            .order_by(OutboundMessage.updated_at.desc())
            .limit(1)
        )
        return {
            "database": "ok",
            "sessionCount": session_count or 0,
            "readyClinicCount": ready_count or 0,
            "failedCount": failed_count or 0,
            "needsReviewCount": needs_review_count or 0,
            "queueLagSeconds": lag_seconds(oldest_open),
            "lastSendAt": last_send_at,
            "lastError": last_error,
        }
    except Exception as exc:  # noqa: BLE001
        return {
            "database": "error",
            "sessionCount": 0,
            "readyClinicCount": 0,
            "failedCount": 0,
            "needsReviewCount": 0,
            "queueLagSeconds": 0,
            "lastSendAt": None,
            "lastError": str(exc),
        }


async def clinic_metrics(session: AsyncSession, clinic_id: uuid.UUID) -> dict:
    pending_count = await session.scalar(
        select(func.count()).select_from(OutboundMessage).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state.in_(["Claimed", "Sending"]),
        )
    )
    retry_count = await session.scalar(
        select(func.count()).select_from(OutboundMessage).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state == "RetryScheduled",
        )
    )
    failed_count = await session.scalar(
        select(func.count()).select_from(OutboundMessage).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state == "Failed",
        )
    )
    needs_review_count = await session.scalar(
        select(func.count()).select_from(OutboundMessage).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state == "NeedsReview",
        )
    )
    last_sent_at = await session.scalar(
        select(func.max(OutboundMessage.updated_at)).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state == "Sent",
        )
    )
    oldest_open = await session.scalar(
        select(func.min(OutboundMessage.created_at)).where(
            OutboundMessage.clinic_id == clinic_id,
            OutboundMessage.state.in_(["Claimed", "Sending", "RetryScheduled"]),
        )
    )
    return {
        "pending_count": pending_count or 0,
        "retry_scheduled_count": retry_count or 0,
        "failed_count": failed_count or 0,
        "needs_review_count": needs_review_count or 0,
        "last_sent_at": last_sent_at,
        "queue_lag_seconds": lag_seconds(oldest_open),
    }


def lag_seconds(started_at: datetime | None) -> int:
    if started_at is None:
        return 0
    if started_at.tzinfo is None:
        started_at = started_at.replace(tzinfo=timezone.utc)
    return max(0, int((datetime.now(timezone.utc) - started_at).total_seconds()))


def ensure_clinic_scope(principal: GatewayPrincipal, clinic_id: uuid.UUID) -> None:
    if principal.clinic_id != str(clinic_id):
        raise HTTPException(status_code=status.HTTP_403_FORBIDDEN)
