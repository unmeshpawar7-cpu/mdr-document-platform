"""LLM classification with schema validation, known-code checks and bounded corrective retries."""

from __future__ import annotations

import json
import logging
import re
import time
from dataclasses import dataclass

from pydantic import BaseModel, Field, ValidationError

from app.ai.prompts import PromptTemplate
from app.ai.providers.base import LlmProvider, LlmProviderError
from app.ai.taxonomy import Taxonomy
from app.schemas.classify import ClassifyItem, Priority

logger = logging.getLogger(__name__)

SYSTEM_PROMPT = "You are a precise engineering document-control assistant. Reply with JSON only."
_JSON_OBJECT = re.compile(r"\{.*\}", re.DOTALL)


class LlmItem(BaseModel):
    id: str
    discipline: str
    documentType: str
    category: str | None = Field(default=None, max_length=100)
    priority: Priority
    confidence: float = Field(ge=0, le=1)
    rationale: str = Field(default="", max_length=500)


class LlmBatch(BaseModel):
    results: list[LlmItem]


class LlmClassificationError(RuntimeError):
    """The model did not return valid, grounded output within the retry budget (or the call failed)."""


@dataclass(frozen=True)
class LlmOutcome:
    items: dict[str, LlmItem]
    attempts: int
    input_tokens: int
    output_tokens: int


def _extract_json(text: str) -> str:
    match = _JSON_OBJECT.search(text)
    if not match:
        raise ValueError("no JSON object found in the model output")
    return match.group(0)


class LlmClassifier:
    def __init__(
        self, provider: LlmProvider, taxonomy: Taxonomy, prompt: PromptTemplate, max_validation_retries: int
    ) -> None:
        self.provider = provider
        self.prompt = prompt
        self._taxonomy = taxonomy
        self._max_retries = max_validation_retries

    def _validate(self, text: str, expected_ids: set[str]) -> dict[str, LlmItem]:
        batch = LlmBatch.model_validate_json(_extract_json(text))
        problems: list[str] = []
        by_id: dict[str, LlmItem] = {}
        for item in batch.results:
            if item.id not in expected_ids:
                problems.append(f"unknown id '{item.id}'")
                continue
            if item.discipline not in self._taxonomy.discipline_codes:
                problems.append(f"id {item.id}: discipline '{item.discipline}' is not an allowed code")
            if item.documentType not in self._taxonomy.document_type_codes:
                problems.append(f"id {item.id}: documentType '{item.documentType}' is not an allowed code")
            by_id[item.id] = item
        missing = expected_ids - set(by_id)
        if missing:
            problems.append(f"missing results for ids {sorted(missing)}")
        if problems:
            raise ValueError("; ".join(problems))
        return by_id

    async def classify(self, items: list[ClassifyItem]) -> LlmOutcome:
        expected_ids = {item.id for item in items}
        items_json = json.dumps(
            [
                {"id": i.id, "title": i.title, "description": i.description, "documentNumber": i.document_number}
                for i in items
            ]
        )
        base_prompt = self.prompt.render(
            discipline_codes=", ".join(sorted(self._taxonomy.discipline_codes)),
            document_type_codes=", ".join(sorted(self._taxonomy.document_type_codes)),
            items_json=items_json,
        )
        user_prompt = base_prompt
        input_tokens = output_tokens = 0
        started = time.perf_counter()

        for attempt in range(1, self._max_retries + 2):
            try:
                completion = await self.provider.complete(SYSTEM_PROMPT, user_prompt)
            except LlmProviderError as error:
                raise LlmClassificationError(str(error)) from error
            input_tokens += completion.input_tokens
            output_tokens += completion.output_tokens
            try:
                validated = self._validate(completion.text, expected_ids)
            except (ValidationError, ValueError) as error:
                feedback = str(error)[:1500]
                logger.warning(
                    "llm output rejected",
                    extra={"provider": self.provider.name, "model": self.provider.model, "attempt": attempt},
                )
                user_prompt = (
                    f"{base_prompt}\n\nYour previous answer was rejected by the validator:\n{feedback}\n"
                    "Return the corrected JSON object only."
                )
                continue

            logger.info(
                "llm classification succeeded",
                extra={
                    "provider": self.provider.name,
                    "model": self.provider.model,
                    "promptVersion": self.prompt.id,
                    "attempts": attempt,
                    "items": len(items),
                    "inputTokens": input_tokens,
                    "outputTokens": output_tokens,
                    "latencyMs": round((time.perf_counter() - started) * 1000, 1),
                },
            )
            return LlmOutcome(validated, attempt, input_tokens, output_tokens)

        raise LlmClassificationError(f"model output failed validation after {self._max_retries + 1} attempts")
