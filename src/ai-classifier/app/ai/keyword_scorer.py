"""Deterministic, explainable classifier: weighted keyword scoring against the taxonomy.

This is the `offline` provider. It is a heuristic, not a language model: every suggestion can be
traced back to the keywords listed in its rationale.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

from app.ai.taxonomy import CodeDefinition, Taxonomy
from app.ai.text import contains_phrase, normalize, token_count

TITLE_WEIGHT = 2.0
DESCRIPTION_WEIGHT = 1.0
EVIDENCE_SCALE = 3.0
MIN_DESCRIPTION_TOKENS = 3


@dataclass(frozen=True)
class AxisScore:
    code: str | None
    confidence: float
    matched: list[str]


@dataclass(frozen=True)
class HeuristicResult:
    discipline: AxisScore
    document_type: AxisScore
    category: str | None
    priority: str
    confidence: float
    missing_metadata: list[str] = field(default_factory=list)

    @property
    def rationale(self) -> str:
        parts = []
        if self.discipline.code:
            parts.append(f"discipline {self.discipline.code} via {', '.join(self.discipline.matched[:4])}")
        else:
            parts.append("no discipline keywords")
        if self.document_type.code:
            parts.append(f"type {self.document_type.code} via {', '.join(self.document_type.matched[:3])}")
        else:
            parts.append("no document type keywords")
        return "; ".join(parts)


class KeywordScorer:
    def __init__(self, taxonomy: Taxonomy) -> None:
        self._taxonomy = taxonomy

    @staticmethod
    def _score_axis(definitions: dict[str, CodeDefinition], title: str, description: str) -> AxisScore:
        scores: dict[str, float] = {}
        matches: dict[str, list[str]] = {}
        for code, definition in definitions.items():
            total = 0.0
            hits: list[str] = []
            for keyword, weight in definition.keywords.items():
                in_title = contains_phrase(title, keyword)
                in_description = contains_phrase(description, keyword)
                if in_title or in_description:
                    total += weight * (TITLE_WEIGHT if in_title else 0.0) + weight * (
                        DESCRIPTION_WEIGHT if in_description else 0.0
                    )
                    hits.append(keyword)
            if total > 0:
                scores[code] = total
                matches[code] = sorted(hits, key=lambda k: -definition.keywords[k])

        if not scores:
            return AxisScore(None, 0.0, [])

        ranked = sorted(scores.items(), key=lambda kv: (-kv[1], kv[0]))
        best_code, best = ranked[0]
        runner_up = ranked[1][1] if len(ranked) > 1 else 0.0
        margin = (best - runner_up) / best
        evidence = 1.0 - math.exp(-best / EVIDENCE_SCALE)
        confidence = evidence * (0.5 + 0.5 * margin)
        return AxisScore(best_code, round(confidence, 4), matches[best_code])

    def _category(self, text: str, discipline: str | None) -> str | None:
        best_name, best_hits = None, 0
        for name, keywords in self._taxonomy.categories.items():
            hits = sum(1 for k in keywords if contains_phrase(text, k))
            if hits > best_hits:
                best_name, best_hits = name, hits
        if best_name:
            return best_name
        if discipline:
            return self._taxonomy.disciplines[discipline].default_category
        return None

    def _priority(self, text: str, document_type: str | None) -> str:
        rules = self._taxonomy.priority
        if any(contains_phrase(text, k) for k in rules.critical):
            return "Critical"
        if any(contains_phrase(text, k) for k in rules.high):
            return "High"
        if document_type and document_type in rules.type_defaults:
            return rules.type_defaults[document_type]
        return rules.default

    def score(self, title: str, description: str | None, document_number: str | None) -> HeuristicResult:
        norm_title = normalize(title)
        norm_description = normalize(description)
        combined = f"{norm_title} {norm_description}".strip()

        discipline = self._score_axis(self._taxonomy.disciplines, norm_title, norm_description)
        document_type = self._score_axis(self._taxonomy.document_types, norm_title, norm_description)

        missing: list[str] = []
        if token_count(description) < MIN_DESCRIPTION_TOKENS:
            missing.append("description")
        if not document_number:
            missing.append("documentNumber")
        if token_count(title) < 2:
            missing.append("title")

        overall = round((discipline.confidence + document_type.confidence) / 2, 3)
        return HeuristicResult(
            discipline=discipline,
            document_type=document_type,
            category=self._category(combined, discipline.code),
            priority=self._priority(combined, document_type.code),
            confidence=overall,
            missing_metadata=missing,
        )
