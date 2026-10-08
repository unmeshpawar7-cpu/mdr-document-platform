"""Builds the configured LLM provider; `offline` means no LLM at all (heuristic scorer only)."""

from __future__ import annotations

import httpx

from app.ai.providers.anthropic_provider import AnthropicProvider
from app.ai.providers.base import LlmProvider
from app.ai.providers.openai_provider import OpenAiProvider
from app.core.config import Settings


class ProviderConfigurationError(ValueError):
    pass


def build_provider(settings: Settings, client: httpx.AsyncClient) -> LlmProvider | None:
    if settings.llm_provider == "offline":
        return None
    if settings.llm_provider == "openai":
        if settings.openai_api_key is None:
            raise ProviderConfigurationError("LLM_PROVIDER=openai requires OPENAI_API_KEY")
        return OpenAiProvider(
            settings.openai_api_key.get_secret_value(), settings.openai_model, settings.openai_base_url, client
        )
    if settings.anthropic_api_key is None:
        raise ProviderConfigurationError("LLM_PROVIDER=anthropic requires ANTHROPIC_API_KEY")
    return AnthropicProvider(
        settings.anthropic_api_key.get_secret_value(), settings.anthropic_model, settings.anthropic_base_url, client
    )
