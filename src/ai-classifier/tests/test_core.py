from __future__ import annotations

import json
import logging

import pytest
from pydantic import ValidationError

from app.core.config import Settings, get_settings
from app.core.logging import JsonFormatter, _safe_correlation_id, correlation_id_var


def test_settings_require_a_strong_api_key(monkeypatch: pytest.MonkeyPatch) -> None:
    with pytest.raises(ValidationError):
        Settings(classifier_api_key="short")
    monkeypatch.setenv("CLASSIFIER_API_KEY", "env-key-0123456789abc")
    monkeypatch.setenv("LLM_PROVIDER", "offline")
    get_settings.cache_clear()
    try:
        assert get_settings().classifier_api_key.get_secret_value() == "env-key-0123456789abc"
    finally:
        get_settings.cache_clear()


def test_json_formatter_includes_correlation_id_and_extras() -> None:
    token = correlation_id_var.set("abc-123")
    try:
        record = logging.LogRecord("app", logging.INFO, __file__, 1, "hello", (), None)
        record.items = 3
        line = json.loads(JsonFormatter().format(record))
    finally:
        correlation_id_var.reset(token)
    assert line["correlationId"] == "abc-123"
    assert line["items"] == 3
    assert line["message"] == "hello"


def test_json_formatter_includes_exceptions() -> None:
    try:
        raise RuntimeError("boom")
    except RuntimeError:
        import sys

        record = logging.LogRecord("app", logging.ERROR, __file__, 1, "failed", (), sys.exc_info())
    assert "RuntimeError" in json.loads(JsonFormatter().format(record))["exception"]


@pytest.mark.parametrize(
    ("value", "kept"), [("abc-1_2", True), ("bad value!", False), (None, False), ("x" * 65, False)]
)
def test_correlation_ids_are_sanitised(value: str | None, kept: bool) -> None:
    result = _safe_correlation_id(value)
    assert (result == value) is kept
    assert len(result) <= 64
