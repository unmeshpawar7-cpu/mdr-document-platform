from __future__ import annotations

import csv
from pathlib import Path

import pytest
import yaml

from app.ai.keyword_scorer import KeywordScorer
from app.ai.taxonomy import Taxonomy, load_taxonomy
from app.ai.text import contains_phrase, normalize

SAMPLES = Path(__file__).resolve().parents[3] / "samples"
DOTNET_DISCIPLINES = {"ARC", "CIV", "ELE", "HSE", "INS", "MEC", "PRO", "STR"}
DOTNET_TYPES = {"CAL", "DS", "DWG", "LST", "PID", "PRC", "REP", "SPE"}


def test_normalize_and_phrase_matching() -> None:
    assert normalize("P&ID - Raw Water") == "p id raw water"
    assert normalize(None) == ""
    assert contains_phrase("p id raw water", "p id")
    assert not contains_phrase("pumping station", "pump")


def test_taxonomy_codes_match_dotnet_reference_data(taxonomy: Taxonomy) -> None:
    assert taxonomy.discipline_codes == DOTNET_DISCIPLINES
    assert taxonomy.document_type_codes == DOTNET_TYPES
    assert taxonomy.version


def test_taxonomy_rejects_non_positive_weights(tmp_path: Path) -> None:
    bad = {"version": "x", "disciplines": {"MEC": {"name": "M", "keywords": {"pump": 0}}}, "document_types": {}}
    path = tmp_path / "bad.yaml"
    path.write_text(yaml.safe_dump(bad))
    with pytest.raises(ValueError):
        load_taxonomy(path)


@pytest.mark.parametrize(
    ("title", "description", "discipline", "doc_type"),
    [
        ("Raw water intake pump datasheet", "Datasheet for vertical turbine pumps", "MEC", "DS"),
        ("Medium voltage switchgear specification", "11 kV switchgear and relays", "ELE", "SPE"),
        ("P&ID chlorination system", "Piping and instrumentation diagram for dosing", "PRO", "PID"),
        ("HAZOP study report", "Hazard and operability study", "HSE", "REP"),
        ("Instrument index", "Register of field instruments and loops", "INS", "LST"),
        ("Site grading and drainage plan", "Earthworks and stormwater layout", "CIV", "DWG"),
    ],
)
def test_scorer_classifies_typical_titles(
    scorer: KeywordScorer, title: str, description: str, discipline: str, doc_type: str
) -> None:
    result = scorer.score(title, description, "NWP-001-PUN-MEC-DWG-0001")
    assert result.discipline.code == discipline
    assert result.document_type.code == doc_type
    assert 0.5 < result.confidence <= 1.0
    assert discipline in result.rationale


def test_no_signal_gives_zero_confidence_and_missing_metadata(scorer: KeywordScorer) -> None:
    result = scorer.score("Miscellaneous", None, None)
    assert result.discipline.code is None
    assert result.document_type.code is None
    assert result.confidence == 0.0
    assert set(result.missing_metadata) == {"description", "documentNumber", "title"}
    assert "no discipline keywords" in result.rationale


def test_ambiguous_titles_get_lower_confidence(scorer: KeywordScorer) -> None:
    clear = scorer.score("Intake pump general arrangement drawing", "Pump layout", "X")
    ambiguous = scorer.score("Pump cable drawing", "Pump and cable", "X")
    assert ambiguous.discipline.confidence < clear.discipline.confidence


def test_priority_rules(scorer: KeywordScorer) -> None:
    assert scorer.score("HAZOP study report", "Hazard review", "X").priority == "Critical"
    assert scorer.score("P&ID filter backwash", "Piping and instrumentation diagram", "X").priority == "High"
    assert scorer.score("Valve list", "List of manual valves", "X").priority == "Low"
    assert scorer.score("Something", "Nothing relevant here", "X").priority == "Medium"


def test_category_falls_back_to_discipline_default(scorer: KeywordScorer) -> None:
    assert scorer.score("Intake pump datasheet", "Pump duty", "X").category == "Rotating equipment"
    assert scorer.score("Architectural elevations drawing", "Gatehouse views", "X").category == "Buildings"


def test_offline_scorer_on_bundled_sample(scorer: KeywordScorer) -> None:
    """Sanity check on the 40-row sample. The taxonomy was written alongside this sample, so this is a
    regression guard, not a generalisation benchmark."""
    with (SAMPLES / "mdr-valid.csv").open(encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    hits = sum(
        1
        for r in rows
        if (res := scorer.score(r["Title"], r["Description"], r["DocumentNumber"])).discipline.code == r["Discipline"]
        and res.document_type.code == r["DocumentType"]
    )
    assert hits / len(rows) >= 0.9
