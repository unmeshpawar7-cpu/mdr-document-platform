#!/usr/bin/env python3
"""Generate a large, valid MDR CSV for load testing the upload pipeline.

Usage:
    python samples/mdr-large-generator.py --rows 20000 --output samples/generated/mdr-20000.csv
    python samples/mdr-large-generator.py --rows 5000 --error-rate 0.05 --seed 7

Rows target project NWP-001 and only use WBS codes created by the README quick start
(1, 1.1, 2, 2.1, 2.2, 3, 3.1, 3.2, 3.3, 4, 4.1, 4.2, 5, 5.1, 5.2). Each document number is unique
because the sequence is derived from the row index; sequences wrap per (office, discipline, type)
so values stay within 0001-9999. With --error-rate > 0 a fraction of rows get a deliberate defect
(unknown discipline, zero quantity, or a malformed date) to exercise the error paths.
Uses only the Python standard library.
"""
from __future__ import annotations

import argparse
import csv
import random
from datetime import date, timedelta
from pathlib import Path

HEADER = [
    "DocumentNumber", "Title", "Description", "Discipline", "DocumentType", "WBS",
    "GateSet", "Office", "Quantity", "Revision", "PlannedDate", "Status",
]
OFFICES = ["PUN", "HOU", "LDN"]
DISCIPLINES = {
    "MEC": ["pump", "blower", "piping", "compressor", "valve"],
    "ELE": ["switchgear", "cable", "transformer", "motor control centre", "earthing"],
    "INS": ["instrument", "SCADA", "flow meter", "analyser", "control valve"],
    "CIV": ["drainage", "road", "earthworks", "foundation", "culvert"],
    "STR": ["steel frame", "concrete slab", "pipe rack", "platform", "tank wall"],
    "PRO": ["dosing", "filtration", "clarifier", "chlorination", "hydraulic profile"],
    "HSE": ["HAZOP", "fire protection", "safety", "environmental", "emergency response"],
    "ARC": ["floor plan", "elevation", "finishes", "gatehouse", "control building"],
}
TYPES = {
    "DWG": ("layout drawing", ["STD", "INFO"]),
    "SPE": ("specification", ["STD", "PROC"]),
    "CAL": ("calculation", ["STD", "INFO"]),
    "DS": ("datasheet", ["STD", "PROC"]),
    "REP": ("report", ["STD", "INFO", "REG"]),
    "LST": ("list", ["STD", "PROC", "INFO"]),
    "PRC": ("procedure", ["STD", "REG"]),
}
WBS = ["1.1", "2.1", "2.2", "3.1", "3.2", "3.3", "4.1", "4.2", "5.1", "5.2"]
STATUSES = ["Planned", "Planned", "Planned", "InProgress", "Issued"]


def build_rows(count: int, error_rate: float, rng: random.Random) -> list[list[str]]:
    counters: dict[tuple[str, str, str], int] = {}
    start = date(2027, 1, 4)
    rows: list[list[str]] = []
    for index in range(count):
        office = rng.choice(OFFICES)
        discipline = rng.choice(list(DISCIPLINES))
        doc_type = rng.choice(list(TYPES))
        type_label, gate_sets = TYPES[doc_type]
        key = (office, discipline, doc_type)
        counters[key] = counters.get(key, 0) + 1
        sequence = counters[key]
        if sequence > 9999:
            raise SystemExit(f"Too many rows for {key}; reduce --rows")
        subject = rng.choice(DISCIPLINES[discipline])
        planned = start + timedelta(days=index % 540)
        row = [
            f"NWP-001-{office}-{discipline}-{doc_type}-{sequence:04d}",
            f"{subject.capitalize()} {type_label} {index + 1}",
            f"Generated {type_label} for {subject} (load test row {index + 1})",
            discipline,
            doc_type,
            rng.choice(WBS),
            rng.choice(gate_sets),
            office,
            str(rng.randint(1, 5)),
            rng.choice(["A", "B", "0"]),
            planned.isoformat(),
            rng.choice(STATUSES),
        ]
        if error_rate > 0 and rng.random() < error_rate:
            defect = rng.randrange(3)
            if defect == 0:
                row[3] = "XXX"            # MDR030 + MDR013
            elif defect == 1:
                row[8] = "0"              # MDR040
            else:
                row[10] = "31/12/2027"    # MDR004
        rows.append(row)
    return rows


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--rows", type=int, default=10_000, help="number of data rows (default 10000)")
    parser.add_argument("--output", type=Path, default=None, help="output path (default samples/generated/mdr-<rows>.csv)")
    parser.add_argument("--error-rate", type=float, default=0.0, help="fraction of rows with a deliberate defect")
    parser.add_argument("--seed", type=int, default=42, help="random seed for reproducible files")
    args = parser.parse_args()

    output = args.output or Path(__file__).parent / "generated" / f"mdr-{args.rows}.csv"
    output.parent.mkdir(parents=True, exist_ok=True)
    rows = build_rows(args.rows, args.error_rate, random.Random(args.seed))
    with output.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(HEADER)
        writer.writerows(rows)
    print(f"Wrote {len(rows)} rows to {output} ({output.stat().st_size / 1024:.0f} KiB)")


if __name__ == "__main__":
    main()
