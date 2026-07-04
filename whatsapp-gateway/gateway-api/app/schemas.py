import uuid
from datetime import datetime

from pydantic import BaseModel, ConfigDict


def to_camel(value: str) -> str:
    parts = value.split("_")
    return parts[0] + "".join(part.capitalize() for part in parts[1:])


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


class SessionStatus(CamelModel):
    clinic_id: uuid.UUID
    status: str
    connected_phone: str | None = None
    last_seen_at: datetime | None = None
    last_error: str | None = None
    pending_count: int = 0
    retry_scheduled_count: int = 0
    failed_count: int = 0
    needs_review_count: int = 0
    last_sent_at: datetime | None = None
    queue_lag_seconds: int = 0


class QrResponse(CamelModel):
    clinic_id: uuid.UUID
    qr: str | None = None
    updated_at: datetime | None = None
    status: str


class WorkerStatus(CamelModel):
    clinic_id: uuid.UUID
    status: str
    connected_phone: str | None = None
    last_seen_at: datetime | None = None
    last_error: str | None = None


class WorkerQr(CamelModel):
    clinic_id: uuid.UUID
    qr: str | None = None
    status: str


class InboundFromWorker(CamelModel):
    clinic_id: uuid.UUID
    from_phone: str
    message: str
    received_at: datetime | None = None
    gateway_session_id: str
