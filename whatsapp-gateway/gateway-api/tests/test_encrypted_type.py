import base64
import os

from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from sqlalchemy.dialects import postgresql

from app.encrypted_type import EncryptedText, PREFIX, _key


def test_encrypted_text_never_binds_plaintext_and_round_trips(tmp_path, monkeypatch):
    key_file = tmp_path / "pii.key"
    key_file.write_text(base64.b64encode(os.urandom(32)).decode("ascii"), encoding="utf-8")
    monkeypatch.setenv("PII_ENCRYPTION_KEY_FILE", str(key_file))
    _key.cache_clear()

    field = EncryptedText()
    encrypted = field.process_bind_param("çok gizli mesaj", postgresql.dialect())

    assert encrypted.startswith(PREFIX)
    assert "çok gizli mesaj" not in encrypted
    assert field.process_result_value(encrypted, postgresql.dialect()) == "çok gizli mesaj"
    _key.cache_clear()


def test_encrypted_text_reads_dotnet_payload_and_normalizes_on_bind(tmp_path, monkeypatch):
    key_file = tmp_path / "pii.key"
    key = os.urandom(32)
    key_file.write_text(base64.b64encode(key).decode("ascii"), encoding="utf-8")
    monkeypatch.setenv("PII_ENCRYPTION_KEY_FILE", str(key_file))
    _key.cache_clear()

    nonce = os.urandom(12)
    cipher_and_tag = AESGCM(key).encrypt(nonce, "CRM gizli alanı".encode("utf-8"), None)
    dotnet_payload = nonce + cipher_and_tag[-16:] + cipher_and_tag[:-16]
    dotnet_encrypted = PREFIX + base64.b64encode(dotnet_payload).decode("ascii")

    field = EncryptedText()
    assert field.process_result_value(dotnet_encrypted, postgresql.dialect()) == "CRM gizli alanı"

    normalized = field.process_bind_param(dotnet_encrypted, postgresql.dialect())
    assert normalized != dotnet_encrypted
    assert field.process_result_value(normalized, postgresql.dialect()) == "CRM gizli alanı"
    _key.cache_clear()
