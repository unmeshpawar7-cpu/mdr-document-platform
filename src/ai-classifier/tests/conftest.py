from __future__ import annotations

from collections.abc import AsyncIterator

import httpx
import pytest

from app.ai.keyword_scorer import KeywordScorer
from app.ai.taxonomy import Taxonomy, load_taxonomy
from app.core.config import DEFAULT_TAXONOMY, Settings
from app.main import create_app

API_KEY = "test-api-key-0123456789"


@pytest.fixture(scope="session")
def taxonomy() -> Taxonomy:
    return load_taxonomy(DEFAULT_TAXONOMY)


@pytest.fixture(scope="session")
def scorer(taxonomy: Taxonomy) -> KeywordScorer:
    return KeywordScorer(taxonomy)


@pytest.fixture
def settings() -> Settings:
    return Settings(classifier_api_key=API_KEY, llm_provider="offline", max_items_per_request=5)


@pytest.fixture
async def client(settings: Settings) -> AsyncIterator[httpx.AsyncClient]:
    app = create_app(settings)
    transport = httpx.ASGITransport(app=app)
    async with httpx.AsyncClient(transport=transport, base_url="http://test") as http:
        yield http
