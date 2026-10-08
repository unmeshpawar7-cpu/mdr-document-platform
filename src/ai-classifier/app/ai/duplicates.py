"""Near-duplicate detection with TF-IDF (word uni/bi-grams) and cosine similarity (scikit-learn)."""

from __future__ import annotations

import logging
from dataclasses import dataclass

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.metrics.pairwise import linear_kernel

from app.ai.text import normalize

logger = logging.getLogger(__name__)


@dataclass(frozen=True)
class TitledDocument:
    id: str
    title: str


class DuplicateDetector:
    """For each item, reports the most similar *earlier* item or any existing document above the threshold.

    Comparing only against earlier items means the first occurrence is never flagged, mirroring the
    .NET pipeline's "first row wins" rule for exact duplicates.
    """

    def __init__(self, threshold: float) -> None:
        self._threshold = threshold

    def find(self, items: list[TitledDocument], existing: list[TitledDocument]) -> dict[str, tuple[str, float] | None]:
        result: dict[str, tuple[str, float] | None] = {item.id: None for item in items}
        corpus = [normalize(d.title) for d in existing] + [normalize(i.title) for i in items]
        if len(corpus) < 2:
            return result
        try:
            matrix = TfidfVectorizer(ngram_range=(1, 2), sublinear_tf=True, stop_words="english").fit_transform(corpus)
        except ValueError:
            # Empty vocabulary (e.g. only stop words); nothing to compare.
            logger.info("duplicate detection skipped: empty vocabulary")
            return result

        similarities = linear_kernel(matrix[len(existing) :], matrix)
        candidates = existing + items
        for index, item in enumerate(items):
            row = similarities[index]
            best_id, best_score = None, 0.0
            for column in range(len(existing) + index):
                score = float(row[column])
                if score > best_score and candidates[column].id != item.id:
                    best_id, best_score = candidates[column].id, score
            if best_id is not None and best_score >= self._threshold:
                result[item.id] = (best_id, round(best_score, 4))
        return result
