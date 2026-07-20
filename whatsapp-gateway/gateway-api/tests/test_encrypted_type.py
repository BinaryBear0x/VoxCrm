import base64
import os

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
