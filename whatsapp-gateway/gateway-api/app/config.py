from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    gateway_api_host: str = "0.0.0.0"
    gateway_api_port: int = 8088
    app_environment: str = "development"
    auto_create_db: bool = True
    gateway_database_url: str = "postgresql+asyncpg://voxcrm:voxcrm_dev_password@127.0.0.1:5432/voxcrm_gateway_dev"

    voxcrm_api_base_url: str = "http://127.0.0.1:5080"
    voxcrm_jwt_issuer: str = "voxcrm-whatsapp-gateway"
    voxcrm_jwt_audience: str = "voxcrm-api"

    gateway_jwt_issuer: str = "voxcrm"
    gateway_jwt_audience: str = "voxcrm-whatsapp-gateway"
    whatsapp_jwt_secret: str = Field(
        default="dev-only-change-this-very-long-whatsapp-gateway-secret",
        min_length=32,
    )

    worker_base_url: str = "http://127.0.0.1:8090"
    worker_internal_token: str = "dev-only-worker-token-change-me"
    pii_encryption_key_file: str = ""

    poll_interval_seconds: int = 10
    default_batch_size: int = 10
    per_clinic_send_interval_seconds: int = 60
    per_clinic_jitter_seconds: int = 15
    max_retry_count: int = 3

    def validate_runtime(self) -> None:
        if self.app_environment.lower() == "production":
            if self.whatsapp_jwt_secret == "dev-only-change-this-very-long-whatsapp-gateway-secret":
                raise RuntimeError("WHATSAPP_JWT_SECRET must be changed in production.")
            if self.worker_internal_token == "dev-only-worker-token-change-me":
                raise RuntimeError("WORKER_INTERNAL_TOKEN must be changed in production.")
            if not self.pii_encryption_key_file:
                raise RuntimeError("PII_ENCRYPTION_KEY_FILE must be configured in production.")
            if self.per_clinic_send_interval_seconds < 30:
                raise RuntimeError("PER_CLINIC_SEND_INTERVAL_SECONDS must be at least 30 in production.")
            if not 0 <= self.per_clinic_jitter_seconds < self.per_clinic_send_interval_seconds:
                raise RuntimeError(
                    "PER_CLINIC_JITTER_SECONDS must be non-negative and lower than the send interval."
                )


settings = Settings()
