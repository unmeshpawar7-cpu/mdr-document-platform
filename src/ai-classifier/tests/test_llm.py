from __future__ import annotations

import json

import httpx
import pytest
from pydantic import SecretStr

from app.ai.llm_classifier import LlmClassificationError, LlmClassifier
from app.ai.prompts import load_prompt
from app.ai.providers.anthropic_provider import AnthropicProvider
from app.ai.providers.base import LlmCompletion, LlmProviderError, estimate_tokens
from app.ai.providers.factory import ProviderConfigurationError, build_provider
from app.ai.providers.openai_provider import OpenAiProvider
from app.ai.taxonomy import Taxonomy
from app.core.config import Settings
from app.schemas.classify import ClassifyItem

ITEMS = [
    ClassifyItem(
        id="1", title="Intake pump datasheet", description="Pump duty", document_number="NWP-001-PUN-MEC-DS-0001"
    ),
    ClassifyItem(id="2", title="HAZOP study report", description="Hazard study"),
]


def good_json(**overrides: object) -> str:
    results = [
        {
            "id": "1",
            "discipline": "MEC",
            "documentType": "DS",
            "category": "Pumps",
            "priority": "Medium",
            "confidence": 0.9,
            "rationale": "pump datasheet",
        },
        {
            "id": "2",
            "discipline": "HSE",
            "documentType": "REP",
            "category": "Safety",
            "priority": "Critical",
            "confidence": 0.95,
            "rationale": "HAZOP",
        },
    ]
    results[0].update(overrides)
    return json.dumps({"results": results})


class ScriptedProvider:
    name = "fake"
    model = "fake-model"

    def __init__(self, *responses: str | Exception) -> None:
        self._responses = list(responses)
        self.prompts: list[str] = []

    async def complete(self, system: str, user: str) -> LlmCompletion:
        self.prompts.append(user)
        response = self._responses.pop(0)
        if isinstance(response, Exception):
            raise response
        return LlmCompletion(response, 100, 20)


def make(taxonomy: Taxonomy, provider: ScriptedProvider, retries: int = 2) -> LlmClassifier:
    return LlmClassifier(provider, taxonomy, load_prompt("classify", "v1"), retries)


async def test_valid_output_is_accepted_and_prompt_lists_codes(taxonomy: Taxonomy) -> None:
    provider = ScriptedProvider("Sure! " + good_json() + " Hope this helps.")
    outcome = await make(taxonomy, provider).classify(ITEMS)
    assert outcome.items["1"].discipline == "MEC"
    assert outcome.attempts == 1
    assert outcome.input_tokens == 100
    assert "Allowed discipline codes: ARC, CIV, ELE, HSE, INS, MEC, PRO, STR" in provider.prompts[0]
    assert '"documentNumber": "NWP-001-PUN-MEC-DS-0001"' in provider.prompts[0]


async def test_unknown_code_triggers_corrective_retry(taxonomy: Taxonomy) -> None:
    provider = ScriptedProvider(good_json(discipline="PLUMBING"), good_json())
    outcome = await make(taxonomy, provider).classify(ITEMS)
    assert outcome.attempts == 2
    assert "PLUMBING" in provider.prompts[1] and "rejected by the validator" in provider.prompts[1]


@pytest.mark.parametrize(
    "bad",
    [
        "no json here",
        json.dumps({"results": [{"id": "1"}]}),
        good_json(id="999"),
        good_json(confidence=7),
        good_json(documentType="MAN"),
        json.dumps({"results": json.loads(good_json())["results"][:1]}),
    ],
)
async def test_invalid_outputs_exhaust_retries(taxonomy: Taxonomy, bad: str) -> None:
    provider = ScriptedProvider(bad, bad, bad)
    with pytest.raises(LlmClassificationError):
        await make(taxonomy, provider).classify(ITEMS)
    assert len(provider.prompts) == 3


async def test_provider_failure_is_a_classification_error(taxonomy: Taxonomy) -> None:
    with pytest.raises(LlmClassificationError):
        await make(taxonomy, ScriptedProvider(LlmProviderError("boom"))).classify(ITEMS)


async def test_openai_provider_posts_json_mode_request() -> None:
    seen: dict[str, object] = {}

    def handler(request: httpx.Request) -> httpx.Response:
        seen["url"] = str(request.url)
        seen["auth"] = request.headers["authorization"]
        seen["body"] = json.loads(request.content)
        return httpx.Response(
            200,
            json={"choices": [{"message": {"content": "{}"}}], "usage": {"prompt_tokens": 12, "completion_tokens": 3}},
        )

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        completion = await OpenAiProvider("sk-test", "gpt-4o-mini", "https://api.example/v1/", http).complete(
            "sys", "user"
        )

    assert seen["url"] == "https://api.example/v1/chat/completions"
    assert seen["auth"] == "Bearer sk-test"
    assert seen["body"]["response_format"] == {"type": "json_object"}  # type: ignore[index]
    assert (completion.text, completion.input_tokens, completion.output_tokens) == ("{}", 12, 3)


async def test_anthropic_provider_and_transient_retry() -> None:
    calls = {"n": 0}

    def handler(request: httpx.Request) -> httpx.Response:
        calls["n"] += 1
        if calls["n"] == 1:
            return httpx.Response(529, json={"error": "overloaded"})
        assert request.headers["x-api-key"] == "ak-test"
        assert request.headers["anthropic-version"] == "2023-06-01"
        return httpx.Response(200, json={"content": [{"type": "text", "text": '{"results": []}'}]})

    async with httpx.AsyncClient(transport=httpx.MockTransport(handler)) as http:
        completion = await AnthropicProvider("ak-test", "claude-sonnet-4-5", "https://api.example", http).complete(
            "sys", "user"
        )

    assert calls["n"] == 2
    assert completion.text == '{"results": []}'
    assert completion.input_tokens == estimate_tokens("sysuser")


async def test_provider_errors_are_wrapped() -> None:
    async with httpx.AsyncClient(transport=httpx.MockTransport(lambda _: httpx.Response(401, json={}))) as http:
        with pytest.raises(LlmProviderError):
            await OpenAiProvider("k", "m", "https://api.example/v1", http).complete("s", "u")
    async with httpx.AsyncClient(
        transport=httpx.MockTransport(lambda _: httpx.Response(200, json={"unexpected": 1}))
    ) as http:
        with pytest.raises(LlmProviderError):
            await OpenAiProvider("k", "m", "https://api.example/v1", http).complete("s", "u")
        with pytest.raises(LlmProviderError):
            await AnthropicProvider("k", "m", "https://api.example", http).complete("s", "u")


async def test_factory_selects_provider_from_settings() -> None:
    async with httpx.AsyncClient() as http:
        base = {"classifier_api_key": "x" * 16}
        assert build_provider(Settings(**base, llm_provider="offline"), http) is None
        assert isinstance(
            build_provider(Settings(**base, llm_provider="openai", openai_api_key=SecretStr("k")), http), OpenAiProvider
        )
        assert isinstance(
            build_provider(Settings(**base, llm_provider="anthropic", anthropic_api_key=SecretStr("k")), http),
            AnthropicProvider,
        )
        with pytest.raises(ProviderConfigurationError):
            build_provider(Settings(**base, llm_provider="openai"), http)
        with pytest.raises(ProviderConfigurationError):
            build_provider(Settings(**base, llm_provider="anthropic"), http)
