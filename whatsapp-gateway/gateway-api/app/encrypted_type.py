import base64
import os
from functools import lru_cache

from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from sqlalchemy import Text
from sqlalchemy.types import TypeDecorator

PREFIX = "enc:v1:"


@lru_cache(maxsize=1)
def _key() -> bytes | None:
    key_file = os.environ.get("PII_ENCRYPTION_KEY_FILE", "")
    if not key_file:
        return None
    with open(key_file, "r", encoding="utf-8") as handle:
        key = base64.b64decode(handle.read().strip(), validate=True)
    if len(key) != 32:
        raise RuntimeError("PII encryption key must be exactly 32 bytes encoded as base64.")
    return key


class EncryptedText(TypeDecorator):
    impl = Text
    cache_ok = True

    def process_bind_param(self, value, dialect):
        key = _key()
        if value is None or not key or str(value).startswith(PREFIX):
            return value
        nonce = os.urandom(12)
        encrypted = AESGCM(key).encrypt(nonce, str(value).encode("utf-8"), None)
        return PREFIX + base64.b64encode(nonce + encrypted).decode("ascii")

    def process_result_value(self, value, dialect):
        key = _key()
        if value is None or not key or not str(value).startswith(PREFIX):
            return value
        payload = base64.b64decode(str(value)[len(PREFIX):], validate=True)
        return AESGCM(key).decrypt(payload[:12], payload[12:], None).decode("utf-8")
