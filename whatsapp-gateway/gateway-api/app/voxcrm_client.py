import uuid
from datetime import datetime, timezone

import httpx

from app.auth import create_voxcrm_token
from app.config import settings


class VoxCrmClient:
    async def validate_delivery(self, notification_id, clinic_id, phone, message) -> dict:
        token = create_voxcrm_token("whatsapp.notifications.status")
        response = await self._client.post(
            f"/api/whatsapp/notifications/{notification_id}/validate",
            headers={"Authorization": f"Bearer {token}"},
            json={"clinicId": str(clinic_id), "phone": phone, "message": message},
        )
        response.raise_for_status()
        return response.json()

    def __init__(self) -> None:
        self._client = httpx.AsyncClient(base_url=settings.voxcrm_api_base_url, timeout=30)

    async def claim_notifications(self, clinic_ids: list[uuid.UUID]) -> list[dict]:
        token = create_voxcrm_token("whatsapp.notifications.claim")
        response = await self._client.post(
            "/api/whatsapp/notifications/claim",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "clinicIds": [str(clinic_id) for clinic_id in clinic_ids],
                "batchSize": settings.claim_batch_size,
                "gatewayId": "voxcrm-whatsapp-gateway",
                "lockSeconds": settings.claim_lock_seconds,
            },
        )
        response.raise_for_status()
        return response.json()

    async def report_status(
        self,
        notification_id: uuid.UUID,
        status: str,
        gateway_message_id: str | None = None,
        last_error: str | None = None,
        retry_count: int | None = None,
        next_attempt_at: datetime | None = None,
    ) -> bool:
        """Report a delivery state. Returns False when VoxCRM refuses it (404).

        VoxCRM answers 404 for notifications that were cancelled, sent elsewhere, sent to
        review or whose state moved on (stale report). That is final, not a transport
        failure, so callers must not treat it as an exception.
        """
        token = create_voxcrm_token("whatsapp.notifications.status")
        payload = {
            "status": status,
            "gatewayMessageId": gateway_message_id,
            "lastError": last_error,
            "retryCount": retry_count,
            "nextAttemptAt": (
                next_attempt_at.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%fZ")
                if next_attempt_at
                else None
            ),
        }
        response = await self._client.post(
            f"/api/whatsapp/notifications/{notification_id}/status",
            headers={"Authorization": f"Bearer {token}"},
            json=payload,
        )
        if response.status_code == 404:
            return False
        response.raise_for_status()
        return True

    async def recover_expired_processing(self) -> None:
        token = create_voxcrm_token("whatsapp.notifications.recover")
        response = await self._client.post(
            "/api/whatsapp/notifications/recover-expired-processing",
            headers={"Authorization": f"Bearer {token}"},
        )
        response.raise_for_status()

    async def write_inbound(
        self,
        clinic_id: uuid.UUID,
        from_phone: str,
        message: str,
        received_at: datetime | None,
        gateway_session_id: str,
        provider_message_id: str,
    ) -> None:
        token = create_voxcrm_token("whatsapp.inbound.write")
        response = await self._client.post(
            "/api/whatsapp/inbound",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "clinicId": str(clinic_id),
                "fromPhone": from_phone,
                "message": message,
                "receivedAt": received_at.isoformat() if received_at else None,
                "gatewaySessionId": gateway_session_id,
                "providerMessageId": provider_message_id,
            },
        )
        response.raise_for_status()

    async def write_audit(
        self,
        *,
        level: str,
        category: str,
        action: str,
        message: str,
        outcome: str,
        clinic_id: uuid.UUID | None = None,
        entity_type: str | None = None,
        entity_id: str | None = None,
        error_code: str | None = None,
        metadata: dict | None = None,
    ) -> None:
        token = create_voxcrm_token("system.audit.write")
        response = await self._client.post(
            "/api/system/audit-logs",
            headers={"Authorization": f"Bearer {token}"},
            json={
                "level": level,
                "source": "Gateway",
                "category": category,
                "action": action,
                "message": message,
                "outcome": outcome,
                "clinicId": str(clinic_id) if clinic_id else None,
                "entityType": entity_type,
                "entityId": entity_id,
                "errorCode": error_code,
                "metadata": metadata or {},
            },
        )
        response.raise_for_status()


voxcrm_client = VoxCrmClient()
