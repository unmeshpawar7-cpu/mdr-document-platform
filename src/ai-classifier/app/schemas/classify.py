"""Request / response contracts for POST /v1/classify (camelCase on the wire)."""

from __future__ import annotations

from enum import StrEnum

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class CamelModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True, str_strip_whitespace=True)


class Priority(StrEnum):
    LOW = "Low"
    MEDIUM = "Medium"
    HIGH = "High"
    CRITICAL = "Critical"


class ClassifyItem(CamelModel):
    id: str = Field(min_length=1, max_length=64)
    title: str = Field(min_length=1, max_length=250)
    description: str | None = Field(default=None, max_length=2000)
    document_number: str | None = Field(default=None, max_length=50)


class ExistingDocument(CamelModel):
    id: str = Field(min_length=1, max_length=64)
    title: str = Field(min_length=1, max_length=250)
    document_number: str | None = Field(default=None, max_length=50)


class ClassifyRequest(CamelModel):
    items: list[ClassifyItem] = Field(min_length=1, max_length=500)
    existing_documents: list[ExistingDocument] = Field(default_factory=list, max_length=10_000)

    @model_validator(mode="after")
    def _unique_ids(self) -> ClassifyRequest:
        ids = [item.id for item in self.items]
        if len(ids) != len(set(ids)):
            raise ValueError("item ids must be unique")
        return self


class ClassificationResult(CamelModel):
    id: str
    discipline: str | None
    document_type: str | None
    category: str | None
    priority: Priority
    confidence: float = Field(ge=0, le=1)
    missing_metadata: list[str]
    potential_duplicate_of: str | None
    rationale: str
    classifier_version: str


class Usage(CamelModel):
    estimated_input_tokens: int = 0
    estimated_output_tokens: int = 0
    llm_attempts: int = 0


class ClassifyResponse(CamelModel):
    classifier_version: str
    provider: str
    prompt_version: str | None
    results: list[ClassificationResult]
    usage: Usage


class ErrorItem(BaseModel):
    field: str
    message: str


class ErrorResponse(BaseModel):
    success: bool = False
    message: str
    errors: list[ErrorItem] = Field(default_factory=list)
    traceId: str
