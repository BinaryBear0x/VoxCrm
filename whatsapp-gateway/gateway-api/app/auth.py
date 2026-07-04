from datetime import datetime, timedelta, timezone
from typing import Annotated
from uuid import uuid4

import jwt
from fastapi import Depends, Header, HTTPException, status

from app.config import settings

_seen_jti: dict[str, int] = {}


def create_voxcrm_token(scope: str, clinic_id: str | None = None) -> str:
    now = datetime.now(timezone.utc)
    payload = {
        "iss": settings.voxcrm_jwt_issuer,
        "aud": settings.voxcrm_jwt_audience,
        "sub": "voxcrm-whatsapp-gateway",
        "scope": scope,
        "iat": int(now.timestamp()),
        "exp": int((now + timedelta(minutes=5)).timestamp()),
        "jti": uuid4().hex,
    }
    if clinic_id:
        payload["clinic_id"] = clinic_id
    return jwt.encode(payload, settings.whatsapp_jwt_secret, algorithm="HS256")


def require_scope(required_scope: str):
    def dependency(authorization: Annotated[str | None, Header()] = None) -> None:
        if not authorization or not authorization.lower().startswith("bearer "):
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED)

        token = authorization.split(" ", 1)[1]
        try:
            payload = jwt.decode(
                token,
                settings.whatsapp_jwt_secret,
                algorithms=["HS256"],
                issuer=settings.gateway_jwt_issuer,
                audience=settings.gateway_jwt_audience,
            )
        except jwt.PyJWTError as exc:
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED) from exc

        scopes = str(payload.get("scope", "")).split()
        if required_scope not in scopes:
            raise HTTPException(status_code=status.HTTP_403_FORBIDDEN)

        jti = str(payload.get("jti", ""))
        exp = int(payload.get("exp", 0))
        if not jti or _is_replay(jti, exp):
            raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED)

    return dependency


def _is_replay(jti: str, exp: int) -> bool:
    now = int(datetime.now(timezone.utc).timestamp())
    expired = [key for key, value in _seen_jti.items() if value <= now]
    for key in expired:
        _seen_jti.pop(key, None)

    if jti in _seen_jti:
        return True

    _seen_jti[jti] = exp
    return False
