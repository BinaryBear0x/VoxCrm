"""initial gateway schema

Revision ID: 202607020001
Revises:
Create Date: 2026-07-02 00:01:00
"""

from collections.abc import Sequence

import sqlalchemy as sa
from alembic import op
from sqlalchemy.dialects import postgresql

revision: str = "202607020001"
down_revision: str | None = None
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.create_table(
        "clinic_sessions",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True, nullable=False),
        sa.Column("clinic_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("status", sa.String(length=32), nullable=False),
        sa.Column("qr", sa.Text(), nullable=True),
        sa.Column("connected_phone", sa.String(length=64), nullable=True),
        sa.Column("last_seen_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("last_error", sa.Text(), nullable=True),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("clinic_id", name="uq_clinic_sessions_clinic_id"),
    )
    op.create_index("ix_clinic_sessions_clinic_id", "clinic_sessions", ["clinic_id"])
    op.create_index("ix_clinic_sessions_status", "clinic_sessions", ["status"])

    op.create_table(
        "outbound_messages",
        sa.Column("id", postgresql.UUID(as_uuid=True), primary_key=True, nullable=False),
        sa.Column("voxcrm_notification_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("clinic_id", postgresql.UUID(as_uuid=True), nullable=False),
        sa.Column("phone_number", sa.String(length=64), nullable=False),
        sa.Column("message_content", sa.Text(), nullable=False),
        sa.Column("state", sa.String(length=32), nullable=False),
        sa.Column("retry_count", sa.Integer(), nullable=False),
        sa.Column("gateway_message_id", sa.String(length=256), nullable=True),
        sa.Column("last_error", sa.Text(), nullable=True),
        sa.Column("next_attempt_at", sa.DateTime(timezone=True), nullable=True),
        sa.Column("created_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.Column("updated_at", sa.DateTime(timezone=True), server_default=sa.func.now(), nullable=False),
        sa.UniqueConstraint("voxcrm_notification_id", name="uq_outbound_voxcrm_notification"),
    )
    op.create_index("ix_outbound_messages_clinic_id", "outbound_messages", ["clinic_id"])
    op.create_index("ix_outbound_messages_next_attempt_at", "outbound_messages", ["next_attempt_at"])
    op.create_index("ix_outbound_messages_state", "outbound_messages", ["state"])
    op.create_index("ix_outbound_messages_voxcrm_notification_id", "outbound_messages", ["voxcrm_notification_id"])


def downgrade() -> None:
    op.drop_index("ix_outbound_messages_voxcrm_notification_id", table_name="outbound_messages")
    op.drop_index("ix_outbound_messages_state", table_name="outbound_messages")
    op.drop_index("ix_outbound_messages_next_attempt_at", table_name="outbound_messages")
    op.drop_index("ix_outbound_messages_clinic_id", table_name="outbound_messages")
    op.drop_table("outbound_messages")
    op.drop_index("ix_clinic_sessions_status", table_name="clinic_sessions")
    op.drop_index("ix_clinic_sessions_clinic_id", table_name="clinic_sessions")
    op.drop_table("clinic_sessions")
