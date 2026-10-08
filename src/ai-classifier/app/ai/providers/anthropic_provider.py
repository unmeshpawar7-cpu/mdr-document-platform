"""Anthropic Messages API provider over plain httpx."""

from __future__ import annotations

import httpx

from app.ai.providers.base import LlmCompletion, LlmProviderError, estimate_tokens, post_json_with_retry

ANTHROPIC_VERSION = "2023-06-01"


class AnthropicProvider:
    name = "anthropic"

    def __init__(self, api_key: str, model: str, base_url: str, client: httpx.AsyncClient) -> None:
        self.model = model
        self._api_key = api_key
        self._base_url = base_url.rstrip("/")
        self._client = client

    async def complete(self, system: str, user: str) -> LlmCompletion:
        payload = await post_json_with_retry(
            self._client,
            f"{self._base_url}/v1/messages",
            {"x-api-key": self._api_key, "anthropic-version": ANTHROPIC_VERSION},
            {
                "model": self.model,
                "max_tokens": 2048,
                "temperature": 0,
                "system": system,
                "messages": [{"role": "user", "content": user}],
            },
        )
        try:
            text = "".join(block["text"] for block in payload["content"] if block.get("type") == "text")
        except (KeyError, TypeError) as error:
            raise LlmProviderError("unexpected Anthropic response shape") from error
        usage = payload.get("usage") or {}
        return LlmCompletion(
            text=text,
            input_tokens=int(usage.get("input_tokens") or estimate_tokens(system + user)),
            output_tokens=int(usage.get("output_tokens") or estimate_tokens(text)),
        )
