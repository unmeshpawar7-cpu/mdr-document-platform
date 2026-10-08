from __future__ import annotations

import json

import httpx
import pytest

from app.ai.providers.base import LlmCompletion, LlmProviderError
from app.core.config import Settings
from app.main import create_app
from tests.conftest import API_KEY

HEADERS = {"X-Api-Key": API_KEY}


def body(*titles: str, existing: list[dict[str, str]] | None = None) -> dict[str, object]:
    return {
        "items": [
            {
                "id": f"id-{i}",
                "title": t,
                "description": f"{t} for the intake works",
                "documentNumber": f"NWP-001-PUN-MEC-DWG-{i:04d}",
            }
            for i, t in enumerate(titles, 1)
        ],
        "existingDocuments": existing or [],
    }


async def test_health_is_public(client: httpx.AsyncClient) -> None:
    response = await client.get("/health")
    assert response.status_code == 200
    assert response.json()["provider"] == "offline"
    assert (await client.get("/health/ready")).status_code == 200


@pytest.mark.parametrize("headers", [{}, {"X-Api-Key": "wrong-key-0123456789"}])
async def test_classify_requires_api_key(client: httpx.AsyncClient, headers: dict[str, str]) -> None:
    response = await client.post("/v1/classify", json=body("Pump datasheet"), headers=headers)
    assert response.status_code == 401
    payload = response.json()
    assert payload["success"] is False
    assert payload["traceId"]


async def test_classify_returns_camel_case_results(client: httpx.AsyncClient) -> None:
    response = await client.post(
        "/v1/classify",
        json=body("Raw water intake pump datasheet", "Medium voltage switchgear specification"),
        headers={**HEADERS, "X-Correlation-Id": "corr-42"},
    )
    assert response.status_code == 200
    assert response.headers["X-Correlation-Id"] == "corr-42"
    payload = response.json()
    assert payload["provider"] == "offline"
    assert payload["classifierVersion"].startswith("taxonomy-")
    first, second = payload["results"]
    assert (first["discipline"], first["documentType"]) == ("MEC", "DS")
    assert (second["discipline"], second["documentType"]) == ("ELE", "SPE")
    assert set(first) >= {
        "category",
        "priority",
        "confidence",
        "missingMetadata",
        "potentialDuplicateOf",
        "rationale",
        "classifierVersion",
    }


async def test_near_duplicates_reference_existing_or_earlier_ids(client: httpx.AsyncClient) -> None:
    payload = body(
        "Single line diagram main substation",
        "Single line diagram main substation",
        existing=[{"id": "old-7", "title": "Raw water intake pump datasheet"}],
    )
    payload["items"].append({"id": "id-3", "title": "Raw water intake pump datasheet"})  # type: ignore[union-attr]
    results = (await client.post("/v1/classify", json=payload, headers=HEADERS)).json()["results"]
    assert results[0]["potentialDuplicateOf"] is None
    assert results[1]["potentialDuplicateOf"] == "id-1"
    assert results[2]["potentialDuplicateOf"] == "old-7"
    assert "description" in results[2]["missingMetadata"]


@pytest.mark.parametrize(
    "payload",
    [
        {"items": []},
        {"items": [{"id": "a", "title": ""}]},
        {"items": [{"id": "a", "title": "x" * 251}]},
        {"items": [{"id": "a", "title": "Pump"}, {"id": "a", "title": "Pump 2"}]},
        {"items": [{"id": f"i{i}", "title": "Pump"} for i in range(6)]},
    ],
)
async def test_input_limits_are_enforced(client: httpx.AsyncClient, payload: dict[str, object]) -> None:
    response = await client.post("/v1/classify", json=payload, headers=HEADERS)
    assert response.status_code == 422
    assert response.json()["success"] is False


async def test_existing_document_limit(settings: Settings) -> None:
    limited = settings.model_copy(update={"max_existing_documents": 1})
    app = create_app(limited)
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as http:
        payload = body("Pump", existing=[{"id": "a", "title": "x"}, {"id": "b", "title": "y"}])
        assert (await http.post("/v1/classify", json=payload, headers=HEADERS)).status_code == 422


async def test_taxonomy_endpoint(client: httpx.AsyncClient) -> None:
    payload = (await client.get("/v1/taxonomy", headers=HEADERS)).json()
    assert set(payload["disciplines"]) == {"ARC", "CIV", "ELE", "HSE", "INS", "MEC", "PRO", "STR"}


class _Provider:
    name = "fake-llm"
    model = "fake-1"

    def __init__(self, response: str | Exception) -> None:
        self._response = response

    async def complete(self, system: str, user: str) -> LlmCompletion:
        if isinstance(self._response, Exception):
            raise self._response
        return LlmCompletion(self._response, 50, 10)


async def _classify_with(settings: Settings, provider: _Provider) -> dict[str, object]:
    app = create_app(settings, provider=provider)  # type: ignore[arg-type]
    async with httpx.AsyncClient(transport=httpx.ASGITransport(app=app), base_url="http://test") as http:
        response = await http.post("/v1/classify", json=body("Intake pump datasheet"), headers=HEADERS)
        assert response.status_code == 200
        return response.json()


async def test_llm_result_is_used_when_valid(settings: Settings) -> None:
    llm_json = json.dumps(
        {
            "results": [
                {
                    "id": "id-1",
                    "discipline": "PRO",
                    "documentType": "DS",
                    "category": "Process",
                    "priority": "High",
                    "confidence": 0.77,
                    "rationale": "llm says process",
                }
            ]
        }
    )
    payload = await _classify_with(settings, _Provider(llm_json))
    result = payload["results"][0]  # type: ignore[index]
    assert payload["provider"] == "fake-llm"
    assert payload["promptVersion"] == "classify-v1"
    assert payload["usage"]["estimatedInputTokens"] == 50  # type: ignore[index]
    assert (result["discipline"], result["priority"], result["confidence"]) == ("PRO", "High", 0.77)
    assert result["classifierVersion"].startswith("llm-fake-llm-fake-1+prompt-classify-v1")


async def test_llm_failure_falls_back_to_heuristics(settings: Settings) -> None:
    payload = await _classify_with(settings, _Provider(LlmProviderError("down")))
    result = payload["results"][0]  # type: ignore[index]
    assert payload["provider"] == "offline"
    assert result["discipline"] == "MEC"
    assert result["rationale"].startswith("LLM unavailable - heuristic fallback")
