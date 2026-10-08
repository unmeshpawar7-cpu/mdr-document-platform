"""HTTP endpoints: /health, /v1/classify, /v1/taxonomy."""

from __future__ import annotations

from typing import Annotated

from fastapi import APIRouter, Depends, HTTPException, Request

from app.core.config import Settings
from app.core.security import get_app_settings, require_api_key
from app.schemas.classify import ClassifyRequest, ClassifyResponse
from app.services.classification_service import ClassificationService

router = APIRouter()


def get_service(request: Request) -> ClassificationService:
    return request.app.state.service


@router.get("/health", tags=["health"])
@router.get("/health/live", tags=["health"], include_in_schema=False)
@router.get("/health/ready", tags=["health"], include_in_schema=False)
async def health(service: Annotated[ClassificationService, Depends(get_service)]) -> dict[str, str]:
    return {"status": "ok", "provider": service.provider_name, "classifierVersion": service.offline_version}


@router.get("/v1/taxonomy", tags=["classification"], dependencies=[Depends(require_api_key)])
async def taxonomy(request: Request) -> dict[str, object]:
    tax = request.app.state.taxonomy
    return {
        "version": tax.version,
        "disciplines": {code: d.name for code, d in sorted(tax.disciplines.items())},
        "documentTypes": {code: d.name for code, d in sorted(tax.document_types.items())},
    }


@router.post(
    "/v1/classify",
    response_model=ClassifyResponse,
    tags=["classification"],
    dependencies=[Depends(require_api_key)],
    summary="Classify a batch of MDR items (advisory)",
)
async def classify(
    payload: ClassifyRequest,
    service: Annotated[ClassificationService, Depends(get_service)],
    settings: Annotated[Settings, Depends(get_app_settings)],
) -> ClassifyResponse:
    if len(payload.items) > settings.max_items_per_request:
        raise HTTPException(
            status_code=422,
            detail=f"At most {settings.max_items_per_request} items per request.",
        )
    if len(payload.existing_documents) > settings.max_existing_documents:
        raise HTTPException(
            status_code=422,
            detail=f"At most {settings.max_existing_documents} existing documents per request.",
        )
    return await service.classify(payload)
