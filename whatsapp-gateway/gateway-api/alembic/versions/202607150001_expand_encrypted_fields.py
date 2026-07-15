"""expand encrypted PII fields

Revision ID: 202607150001
Revises: 202607020001
"""

from collections.abc import Sequence
import sqlalchemy as sa
from alembic import op

revision: str = "202607150001"
down_revision: str | None = "202607020001"
branch_labels: str | Sequence[str] | None = None
depends_on: str | Sequence[str] | None = None


def upgrade() -> None:
    op.alter_column("clinic_sessions", "connected_phone", existing_type=sa.String(length=64), type_=sa.Text(), existing_nullable=True)
    op.alter_column("outbound_messages", "phone_number", existing_type=sa.String(length=64), type_=sa.Text(), existing_nullable=False)


def downgrade() -> None:
    op.alter_column("outbound_messages", "phone_number", existing_type=sa.Text(), type_=sa.String(length=64), existing_nullable=False)
    op.alter_column("clinic_sessions", "connected_phone", existing_type=sa.Text(), type_=sa.String(length=64), existing_nullable=True)
