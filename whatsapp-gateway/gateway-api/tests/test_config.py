import pytest

from app.config import Settings


def production_settings(**overrides) -> Settings:
    values = {
        "app_environment": "production",
        "whatsapp_jwt_secret": "x" * 32,
        "worker_internal_token": "worker-secret",
        "pii_encryption_key_file": "/run/secrets/pii.key",
        "per_clinic_send_interval_seconds": 60,
        "per_clinic_jitter_seconds": 15,
    }
    values.update(overrides)
    return Settings(**values)


def test_production_rejects_send_interval_below_thirty_seconds():
    settings = production_settings(per_clinic_send_interval_seconds=29)

    with pytest.raises(RuntimeError, match="at least 30"):
        settings.validate_runtime()


def test_production_accepts_controlled_send_interval_and_jitter():
    production_settings().validate_runtime()
