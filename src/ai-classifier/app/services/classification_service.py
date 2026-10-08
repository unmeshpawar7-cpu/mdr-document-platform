"""Use case: classify a batch of MDR items.

Pipeline per request:
1. deterministic keyword/taxonomy scoring for every item (always runs - it is also the fallback),
2. TF-IDF near-duplicate detection against earlier items and existing documents (always deterministic),
3. optional LLM classification (LLM_PROVIDER=openai|anthropic) whose output is schema-validated and only
   accepted when it uses known codes; any LLM failure falls back to step 1 for the whole batch.
"""

from __future__ import annotations

import logging
import time

from app.ai.duplicates import DuplicateDetector, TitledDocument
from app.ai.keyword_scorer import KeywordScorer
from app.ai.llm_classifier import LlmClassificationError, LlmClassifier
from app.ai.taxonomy import Taxonomy
from app.schemas.classify import ClassificationResult, ClassifyRequest, ClassifyResponse, Priority, Usage

logger = logging.getLogger(__name__)

DUPLICATE_ALGORITHM = "tfidf-1"


class ClassificationService:
    def __init__(
        self,
        taxonomy: Taxonomy,
        scorer: KeywordScorer,
        duplicates: DuplicateDetector,
        llm: LlmClassifier | None = None,
    ) -> None:
        self._taxonomy = taxonomy
        self._scorer = scorer
        self._duplicates = duplicates
        self._llm = llm

    @property
    def offline_version(self) -> str:
        return f"taxonomy-{self._taxonomy.version}+{DUPLICATE_ALGORITHM}"

    @property
    def provider_name(self) -> str:
        return self._llm.provider.name if self._llm else "offline"

    async def classify(self, request: ClassifyRequest) -> ClassifyResponse:
        started = time.perf_counter()
        heuristics = {i.id: self._scorer.score(i.title, i.description, i.document_number) for i in request.items}
        duplicates = self._duplicates.find(
            [TitledDocument(i.id, i.title) for i in request.items],
            [TitledDocument(e.id, e.title) for e in request.existing_documents],
        )

        llm_items = None
        usage = Usage()
        version = self.offline_version
        provider = "offline"
        prompt_version = None
        fallback_note = ""
        if self._llm is not None:
            try:
                outcome = await self._llm.classify(request.items)
                llm_items = outcome.items
                usage = Usage(
                    estimated_input_tokens=outcome.input_tokens,
                    estimated_output_tokens=outcome.output_tokens,
                    llm_attempts=outcome.attempts,
                )
                provider = self._llm.provider.name
                prompt_version = self._llm.prompt.id
                version = f"llm-{provider}-{self._llm.provider.model}+prompt-{prompt_version}+{DUPLICATE_ALGORITHM}"
            except LlmClassificationError as error:
                logger.warning("llm unavailable, using heuristic fallback", extra={"reason": str(error)[:200]})
                fallback_note = "LLM unavailable - heuristic fallback; "

        results: list[ClassificationResult] = []
        for item in request.items:
            heuristic = heuristics[item.id]
            duplicate = duplicates.get(item.id)
            llm_item = llm_items.get(item.id) if llm_items else None
            if llm_item is not None:
                result = ClassificationResult(
                    id=item.id,
                    discipline=llm_item.discipline,
                    document_type=llm_item.documentType,
                    category=llm_item.category,
                    priority=llm_item.priority,
                    confidence=round(llm_item.confidence, 3),
                    missing_metadata=heuristic.missing_metadata,
                    potential_duplicate_of=duplicate[0] if duplicate else None,
                    rationale=llm_item.rationale or "llm",
                    classifier_version=version,
                )
            else:
                result = ClassificationResult(
                    id=item.id,
                    discipline=heuristic.discipline.code,
                    document_type=heuristic.document_type.code,
                    category=heuristic.category,
                    priority=Priority(heuristic.priority),
                    confidence=heuristic.confidence,
                    missing_metadata=heuristic.missing_metadata,
                    potential_duplicate_of=duplicate[0] if duplicate else None,
                    rationale=fallback_note
                    + heuristic.rationale
                    + (f"; similar to {duplicate[0]} (cosine {duplicate[1]:.2f})" if duplicate else ""),
                    classifier_version=version,
                )
            results.append(result)

        logger.info(
            "batch classified",
            extra={
                "provider": provider,
                "items": len(request.items),
                "existingDocuments": len(request.existing_documents),
                "duplicates": sum(1 for r in results if r.potential_duplicate_of),
                "latencyMs": round((time.perf_counter() - started) * 1000, 1),
            },
        )
        return ClassifyResponse(
            classifier_version=version,
            provider=provider,
            prompt_version=prompt_version,
            results=results,
            usage=usage,
        )
