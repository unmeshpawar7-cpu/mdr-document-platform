# Sample MDR files

All data is fictional. Files target project **`NWP-001` "Northwind Water Plant"** and assume the WBS
elements `1, 1.1, 2, 2.1, 2.2, 3, 3.1, 3.2, 3.3, 4, 4.1, 4.2, 5, 5.1, 5.2` exist
(see the quick start in the root README).

| File | Purpose |
|---|---|
| `mdr-valid.csv` | 40 realistic, fully valid rows across all 8 disciplines, 8 document types, 3 offices and 4 gate sets. Expected result: `Completed`, 40 inserted. |
| `mdr-with-errors.csv` | 27 rows; each validation rule is exercised by a dedicated row (table below). |
| `mdr-large-generator.py` | Generates N valid rows (optionally with a defect rate) for load testing. Standard library only. |

## `mdr-with-errors.csv` row map

Row numbers are spreadsheet row numbers (header = row 1).

| Row | Code(s) | Defect |
|---|---|---|
| 2, 3 | - | valid |
| 4 | `MDR001_REQUIRED_MISSING` | Title empty |
| 5 | `MDR002_MAX_LENGTH_EXCEEDED` | Title is 281 characters (max 250) |
| 6 | `MDR003_INVALID_INTEGER` | Quantity `two` |
| 7 | `MDR004_INVALID_DATE` | PlannedDate `15/03/2027` (ISO `yyyy-MM-dd` required) |
| 8 | `MDR005_MALFORMED_ROW` (+ cascading errors) | Unquoted comma in Description shifts every later value one column right |
| 9 | `MDR010_DOCNO_FORMAT` | `NWP001-PUN-MEC-DWG-12` |
| 10 | `MDR011_DOCNO_PROJECT_MISMATCH` | Project segment `SRF-002` |
| 11 | `MDR012_DOCNO_OFFICE_MISMATCH` | Number says `HOU`, Office column says `PUN` |
| 12 | `MDR013_DOCNO_DISCIPLINE_MISMATCH` | Number says `MEC`, Discipline column says `ELE` |
| 13 | `MDR014_DOCNO_DOCTYPE_MISMATCH` | Number says `DWG`, DocumentType column says `SPE` |
| 14 | `MDR020_DUPLICATE_IN_FILE` | Same number as row 2 |
| 15 | `MDR021_DUPLICATE_EXISTING` | `NWP-001-PUN-MEC-DWG-0001` already exists **if `mdr-valid.csv` was uploaded first** (otherwise the row is valid) |
| 16 | `MDR030_UNKNOWN_DISCIPLINE` | `XYZ` |
| 17 | `MDR031_UNKNOWN_DOCTYPE` | `MAN` |
| 18 | `MDR032_UNKNOWN_OFFICE` | `SYD` |
| 19 | `MDR033_UNKNOWN_GATESET` | `G9` |
| 20 | `MDR034_GATESET_NOT_ALLOWED` | P&ID (`PID`) only allows gate set `STD`, row uses `PROC` |
| 21 | `MDR035_UNKNOWN_WBS` | WBS `9.9` |
| 22 | `MDR040_QUANTITY_NOT_POSITIVE` | Quantity `0` |
| 23 | `MDR041_INVALID_REVISION` | Revision `A-1` |
| 24 | `MDR042_PLANNED_DATE_IN_PAST` (Warning) | Planned 2020-01-15 - warning only, row is still valid |
| 25 | `MDR043_INVALID_STATUS` | Status `Approved` |
| 26 | - | valid; Status `In Progress` is accepted (spaces/case ignored) |
| 27 | - | valid |
| 28 | - | valid; Description, PlannedDate and Status left empty (optional) |

Expected counters when uploaded **after** `mdr-valid.csv` with the default `PersistValidRows` policy:
total 27, valid 6, invalid 21, duplicates 2, inserted 6, status `CompletedWithErrors`.
Uploaded on its own: valid 7, invalid 20, duplicates 1, inserted 7.
