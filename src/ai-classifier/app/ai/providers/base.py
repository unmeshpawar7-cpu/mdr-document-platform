"""Provider protocol and shared helpers."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

import httpx
from tenacity import AsyncRetrying, retry_if_exception, stop_after_attempt, wait_exponential


@dataclass(frozen=True)
class LlmCompletion:
    text: str
    input_tokens: int
    output_tokens: int


class LlmProvider(Protocol):
    name: str
    model: str

    async def complete(self, system: str, user: str) -> LlmCompletion: ...


class LlmProviderError(RuntimeError):
    """The provider call failed (transport, HTTP status, or unexpected payload)."""


def estimate_tokens(text: str) -> int:
    """Rough token estimate (about 4 characters per token) used when a provider omits usage."""
    return max(1, len(text) // 4)


def _is_transient(error: BaseException) -> bool:
    if isinstance(error, httpx.TransportError):
        return True
    if isinstance(error, httpx.HTTPStatusError):
        return error.response.status_code == 429 or error.response.status_code >= 500
    return False


async def post_json_with_retry(client: httpx.AsyncClient, url: str, headers: dict[str, str], body: dict) -> dict:
    """POST with bounded exponential-backoff retries on transient failures (429/5xx/transport)."""
    try:
        async for attempt in AsyncRetrying(
            stop=stop_after_attempt(3),
            wait=wait_exponential(multiplier=0.2, max=2),
            retry=retry_if_exception(_is_transient),
            reraise=True,
        ):
            with attempt:
                response = await client.post(url, headers=headers, json=body)
                response.raise_for_status()
                return response.json()
    except (httpx.HTTPError, ValueError) as error:
        raise LlmProviderError(f"{type(error).__name__}: provider call failed") from error
    raise LlmProviderError("provider call failed")  # pragma: no cover - AsyncRetrying always returns or raises
