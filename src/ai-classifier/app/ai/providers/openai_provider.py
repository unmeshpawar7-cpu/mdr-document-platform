"""OpenAI Chat Completions provider (JSON mode) over plain httpx."""

from __future__ import annotations

import httpx

from app.ai.providers.base import LlmCompletion, LlmProviderError, estimate_tokens, post_json_with_retry


class OpenAiProvider:
    name = "openai"

    def __init__(self, api_key: str, model: str, base_url: str, client: httpx.AsyncClient) -> None:
        self.model = model
        self._api_key = api_key
        self._base_url = base_url.rstrip("/")
        self._client = client

    async def complete(self, system: str, user: str) -> LlmCompletion:
        payload = await post_json_with_retry(
            self._client,
            f"{self._base_url}/chat/completions",
            {"Authorization": f"Bearer {self._api_key}"},
            {
                "model": self.model,
                "temperature": 0,
                "response_format": {"type": "json_object"},
                "messages": [{"role": "system", "content": system}, {"role": "user", "content": user}],
            },
        )
        try:
            text = payload["choices"][0]["message"]["content"]
        except (KeyError, IndexError, TypeError) as error:
            raise LlmProviderError("unexpected OpenAI response shape") from error
        usage = payload.get("usage") or {}
        return LlmCompletion(
            text=text,
            input_tokens=int(usage.get("prompt_tokens") or estimate_tokens(system + user)),
            output_tokens=int(usage.get("completion_tokens") or estimate_tokens(text)),
        )
