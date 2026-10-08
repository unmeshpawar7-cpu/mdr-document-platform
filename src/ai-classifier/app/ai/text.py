"""Text normalisation shared by the scorer and the duplicate detector."""

from __future__ import annotations

import re

_NON_ALNUM = re.compile(r"[^a-z0-9]+")


def normalize(text: str | None) -> str:
    """Lower-case, replace punctuation with spaces and collapse whitespace ("P&ID" -> "p id")."""
    if not text:
        return ""
    return _NON_ALNUM.sub(" ", text.lower()).strip()


def contains_phrase(normalized_text: str, phrase: str) -> bool:
    """Whole-word / whole-phrase match on normalised text."""
    return f" {phrase} " in f" {normalized_text} "


def token_count(text: str | None) -> int:
    return len(normalize(text).split())
