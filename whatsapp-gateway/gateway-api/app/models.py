import uuid
from datetime import datetime

from sqlalchemy import DateTime, Integer, String, Text, UniqueConstraint, func
from sqlalchemy.dialects.postgresql import UUID
from sqlalchemy.orm import DeclarativeBase, Mapped, mapped_column


class Base(DeclarativeBase):
    pass


class ClinicSession(Base):
    __tablename__ = "clinic_sessions"

    id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    clinic_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), unique=True, index=True)
    status: Mapped[str] = mapped_column(String(32), default="disconnected", index=True)
    qr: Mapped[str | None] = mapped_column(Text)
    connected_phone: Mapped[str | None] = mapped_column(String(64))
    last_seen_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True))
    last_error: Mapped[str | None] = mapped_column(Text)
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())


class OutboundMessage(Base):
    __tablename__ = "outbound_messages"
    __table_args__ = (
        UniqueConstraint("voxcrm_notification_id", name="uq_outbound_voxcrm_notification"),
    )

    id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), primary_key=True, default=uuid.uuid4)
    voxcrm_notification_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), index=True)
    clinic_id: Mapped[uuid.UUID] = mapped_column(UUID(as_uuid=True), index=True)
    phone_number: Mapped[str] = mapped_column(String(64))
    message_content: Mapped[str] = mapped_column(Text)
    state: Mapped[str] = mapped_column(String(32), default="Claimed", index=True)
    retry_count: Mapped[int] = mapped_column(Integer, default=0)
    gateway_message_id: Mapped[str | None] = mapped_column(String(256))
    last_error: Mapped[str | None] = mapped_column(Text)
    next_attempt_at: Mapped[datetime | None] = mapped_column(DateTime(timezone=True), index=True)
    created_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now())
    updated_at: Mapped[datetime] = mapped_column(DateTime(timezone=True), server_default=func.now(), onupdate=func.now())
