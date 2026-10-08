"""Loads and validates the YAML taxonomy (disciplines, document types, categories, priority rules)."""

from __future__ import annotations

from pathlib import Path

import yaml
from pydantic import BaseModel, Field, field_validator

from app.ai.text import normalize


class CodeDefinition(BaseModel):
    name: str
    keywords: dict[str, float] = Field(min_length=1)
    default_category: str | None = None

    @field_validator("keywords")
    @classmethod
    def _normalise_keywords(cls, value: dict[str, float]) -> dict[str, float]:
        cleaned = {normalize(k): float(w) for k, w in value.items() if normalize(k)}
        if any(w <= 0 for w in cleaned.values()):
            raise ValueError("keyword weights must be positive")
        return cleaned


class PriorityRules(BaseModel):
    critical: list[str] = Field(default_factory=list)
    high: list[str] = Field(default_factory=list)
    type_defaults: dict[str, str] = Field(default_factory=dict)
    default: str = "Medium"


class Taxonomy(BaseModel):
    version: str
    disciplines: dict[str, CodeDefinition]
    document_types: dict[str, CodeDefinition]
    categories: dict[str, list[str]] = Field(default_factory=dict)
    priority: PriorityRules = Field(default_factory=PriorityRules)

    @property
    def discipline_codes(self) -> set[str]:
        return set(self.disciplines)

    @property
    def document_type_codes(self) -> set[str]:
        return set(self.document_types)


def load_taxonomy(path: Path) -> Taxonomy:
    with path.open(encoding="utf-8") as handle:
        raw = yaml.safe_load(handle)
    taxonomy = Taxonomy.model_validate(raw)
    taxonomy.categories = {name: [normalize(k) for k in words] for name, words in taxonomy.categories.items()}
    taxonomy.priority.critical = [normalize(k) for k in taxonomy.priority.critical]
    taxonomy.priority.high = [normalize(k) for k in taxonomy.priority.high]
    return taxonomy
