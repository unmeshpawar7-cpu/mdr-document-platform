from __future__ import annotations

from app.ai.duplicates import DuplicateDetector, TitledDocument


def test_flags_later_near_duplicate_but_not_first_occurrence() -> None:
    detector = DuplicateDetector(threshold=0.8)
    items = [
        TitledDocument("a", "Raw water intake pump datasheet"),
        TitledDocument("b", "Raw water intake pump datasheet rev"),
        TitledDocument("c", "Earthing and lightning protection layout"),
    ]
    result = detector.find(items, [])
    assert result["a"] is None
    assert result["b"] is not None and result["b"][0] == "a"
    assert result["c"] is None


def test_matches_existing_documents() -> None:
    detector = DuplicateDetector(threshold=0.8)
    existing = [TitledDocument("old-1", "Single line diagram main substation")]
    result = detector.find([TitledDocument("new", "Single line diagram - main substation")], existing)
    assert result["new"] == ("old-1", result["new"][1])
    assert result["new"][1] >= 0.8


def test_threshold_and_degenerate_inputs() -> None:
    strict = DuplicateDetector(threshold=0.99)
    pair = [TitledDocument("a", "Pump datasheet"), TitledDocument("b", "Pump datasheet for intake")]
    assert strict.find(pair, [])["b"] is None
    assert DuplicateDetector(0.5).find([TitledDocument("only", "Pump")], []) == {"only": None}
    assert DuplicateDetector(0.5).find([TitledDocument("x", "the and of"), TitledDocument("y", "a the")], []) == {
        "x": None,
        "y": None,
    }
