"""Settings loaded from environment variables (pydantic-settings)."""

from __future__ import annotations

from functools import lru_cache
from pathlib import Path
from typing import Literal

from pydantic import Field, SecretStr
from pydantic_settings import BaseSettings, SettingsConfigDict

DEFAULT_TAXONOMY = Path(__file__).resolve().parent.parent / "taxonomy" / "taxonomy.yaml"


class Settings(BaseSettings):
    """All configuration comes from the environment; secrets are never hard-coded."""

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

    classifier_api_key: SecretStr = Field(min_length=16, description="Shared secret expected in X-Api-Key")
    llm_provider: Literal["offline", "openai", "anthropic"] = "offline"

    openai_api_key: SecretStr | None = None
    openai_model: str = "gpt-4o-mini"
    openai_base_url: str = "https://api.openai.com/v1"
    anthropic_api_key: SecretStr | None = None
    anthropic_model: str = "claude-sonnet-4-5"
    anthropic_base_url: str = "https://api.anthropic.com"
    llm_timeout_seconds: float = Field(default=20.0, gt=0, le=120)
    llm_max_validation_retries: int = Field(default=2, ge=0, le=5)

    max_items_per_request: int = Field(default=100, ge=1, le=500)
    max_existing_documents: int = Field(default=1000, ge=0, le=10_000)
    duplicate_threshold: float = Field(default=0.85, gt=0, le=1)
    taxonomy_path: Path = DEFAULT_TAXONOMY
    log_level: str = "INFO"


@lru_cache
def get_settings() -> Settings:
    return Settings()  # type: ignore[call-arg]
