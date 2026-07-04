import uuid

import httpx

from app.config import settings


class WorkerClient:
    def __init__(self) -> None:
        self._client = httpx.AsyncClient(
            base_url=settings.worker_base_url,
            timeout=45,
            headers={"x-internal-token": settings.worker_internal_token},
        )

    async def health(self) -> dict:
        response = await self._client.get("/health")
        response.raise_for_status()
        return response.json()

    async def connect(self, clinic_id: uuid.UUID) -> dict:
        response = await self._client.post(f"/clinics/{clinic_id}/connect")
        response.raise_for_status()
        return response.json()

    async def disconnect(self, clinic_id: uuid.UUID) -> dict:
        response = await self._client.post(f"/clinics/{clinic_id}/disconnect")
        response.raise_for_status()
        return response.json()

    async def status(self, clinic_id: uuid.UUID) -> dict:
        response = await self._client.get(f"/clinics/{clinic_id}/status")
        response.raise_for_status()
        return response.json()

    async def qr(self, clinic_id: uuid.UUID) -> dict:
        response = await self._client.get(f"/clinics/{clinic_id}/qr")
        response.raise_for_status()
        return response.json()

    async def send(self, clinic_id: uuid.UUID, phone_number: str, message_content: str, notification_id: uuid.UUID) -> dict:
        response = await self._client.post(
            f"/clinics/{clinic_id}/send",
            json={
                "phoneNumber": phone_number,
                "message": message_content,
                "notificationId": str(notification_id),
            },
        )
        response.raise_for_status()
        return response.json()


worker_client = WorkerClient()
