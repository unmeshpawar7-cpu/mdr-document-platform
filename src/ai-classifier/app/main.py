"""Application factory. Run with: uvicorn app.main:create_app --factory --host 0.0.0.0 --port 8000"""

from __future__ import annotations

from collections.abc import AsyncIterator
from contextlib import asynccontextmanager

import httpx
from fastapi import FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app import __version__
from app.ai.duplicates import DuplicateDetector
from app.ai.keyword_scorer import KeywordScorer
from app.ai.llm_classifier import LlmClassifier
from app.ai.prompts import load_prompt
from app.ai.providers.base import LlmProvider
from app.ai.providers.factory import build_provider
from app.ai.taxonomy import load_taxonomy
from app.api.routes import router
from app.core.config import Settings, get_settings
from app.core.logging import CorrelationIdMiddleware, configure_logging, correlation_id_var
from app.schemas.classify import ErrorItem, ErrorResponse
from app.services.classification_service import ClassificationService


def _error(status_code: int, message: str, errors: list[ErrorItem] | None = None) -> JSONResponse:
    body = ErrorResponse(message=message, errors=errors or [], traceId=correlation_id_var.get())
    return JSONResponse(status_code=status_code, content=body.model_dump())


def create_app(settings: Settings | None = None, provider: LlmProvider | None = None) -> FastAPI:
    """Builds the app. `provider` lets tests inject a fake LLM provider without network access."""
    settings = settings or get_settings()
    configure_logging(settings.log_level)
    http_client = httpx.AsyncClient(timeout=settings.llm_timeout_seconds)

    taxonomy = load_taxonomy(settings.taxonomy_path)
    llm_provider = provider if provider is not None else build_provider(settings, http_client)
    llm = (
        LlmClassifier(llm_provider, taxonomy, load_prompt("classify", "v1"), settings.llm_max_validation_retries)
        if llm_provider is not None
        else None
    )

    @asynccontextmanager
    async def lifespan(_: FastAPI) -> AsyncIterator[None]:
        yield
        await http_client.aclose()

    app = FastAPI(
        title="MDR AI Classifier",
        version=__version__,
        description="Advisory discipline/type/priority suggestions and near-duplicate hints for MDR rows.",
        lifespan=lifespan,
    )
    app.state.settings = settings
    app.state.taxonomy = taxonomy
    app.state.service = ClassificationService(
        taxonomy, KeywordScorer(taxonomy), DuplicateDetector(settings.duplicate_threshold), llm
    )
    app.add_middleware(CorrelationIdMiddleware)
    app.include_router(router)

    @app.exception_handler(RequestValidationError)
    async def _validation_handler(_: Request, exc: RequestValidationError) -> JSONResponse:
        errors = [
            ErrorItem(field=".".join(str(p) for p in e.get("loc", ())[1:]) or "body", message=e.get("msg", "invalid"))
            for e in exc.errors()
        ]
        return _error(422, "Validation failed.", errors)

    @app.exception_handler(HTTPException)
    async def _http_handler(_: Request, exc: HTTPException) -> JSONResponse:
        return _error(exc.status_code, str(exc.detail))

    return app
