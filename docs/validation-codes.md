# Validation code catalogue

Every finding produced by the MDR pipeline carries one of these codes. The list mirrors
`src/Mdr.Domain/Validation/ValidationCodes.cs`; `ValidationCodeCatalogueTests` fails the build if a code
is added there without being documented here.

* **Severity** - only `Error` makes a row invalid. `Warning` rows are still persisted. `Info` is advisory (AI).
* **Row** - spreadsheet row number (header = row 1, first data row = row 2). File-level codes use row `0`.
* **Where** - `upload` codes can be returned synchronously by `POST /api/mdr/upload` as a `400` envelope
  (`errors[].code`); all codes are also stored as validation issues by the background processor.

| Code | Severity | Stage | Meaning | Example trigger |
|---|---|---|---|---|
| `MDR001_REQUIRED_MISSING` | Error | Schema | A required value is empty. | DocumentNumber, Title, Discipline, DocumentType, WBS, GateSet, Office, Quantity or Revision is blank |
| `MDR002_MAX_LENGTH_EXCEEDED` | Error | Schema | A value is longer than the column allows. | Value longer than the column limit (see csv-format.md) |
| `MDR003_INVALID_INTEGER` | Error | Schema | Quantity is not a whole number. | Quantity `two`, `1.5`, `1,000` |
| `MDR004_INVALID_DATE` | Error | Schema | PlannedDate is not an ISO date (yyyy-MM-dd). | PlannedDate `15/03/2027` |
| `MDR005_MALFORMED_ROW` | Error | Schema | The row has more fields than the header. | Unquoted comma creates more fields than the header |
| `MDR010_DOCNO_FORMAT` | Error | Business | Document number does not match {Project}-{Office}-{Discipline}-{Type}-{Seq}. | `NWP001-PUN-MEC-DWG-12`, sequence `0000` |
| `MDR011_DOCNO_PROJECT_MISMATCH` | Error | Business | Document number project segment differs from the upload's project code. | `SRF-002-...` uploaded to project NWP-001 |
| `MDR012_DOCNO_OFFICE_MISMATCH` | Error | Business | Document number office segment differs from the Office column. | Number `...-HOU-...`, Office `PUN` |
| `MDR013_DOCNO_DISCIPLINE_MISMATCH` | Error | Business | Document number discipline segment differs from the Discipline column. | Number `...-MEC-...`, Discipline `ELE` |
| `MDR014_DOCNO_DOCTYPE_MISMATCH` | Error | Business | Document number type segment differs from the DocumentType column. | Number `...-DWG-...`, DocumentType `SPE` |
| `MDR020_DUPLICATE_IN_FILE` | Error | Duplicates | Document number already appears on an earlier row of the same file. | Second and later rows with the same number (first row wins) |
| `MDR021_DUPLICATE_EXISTING` | Error | Duplicates | Document number already exists in the project's register. | Number already registered by an earlier upload |
| `MDR030_UNKNOWN_DISCIPLINE` | Error | Business | Discipline code is not an active reference value. | `XYZ` |
| `MDR031_UNKNOWN_DOCTYPE` | Error | Business | Document type code is not an active reference value. | `MAN` |
| `MDR032_UNKNOWN_OFFICE` | Error | Business | Office code is not an active reference value. | `SYD` |
| `MDR033_UNKNOWN_GATESET` | Error | Business | Gate set code is not an active reference value. | `G9` |
| `MDR034_GATESET_NOT_ALLOWED` | Error | Business | Gate set is not allowed for the row's document type. | `PID` with gate set `PROC` (P&IDs allow `STD` only) |
| `MDR035_UNKNOWN_WBS` | Error | Business | WBS code does not exist for the project. | `9.9` not created for the project |
| `MDR040_QUANTITY_NOT_POSITIVE` | Error | Business | Quantity must be greater than zero. | `0`, `-3` |
| `MDR041_INVALID_REVISION` | Error | Business | Revision must be 1-3 upper-case letters or digits. | `A-1`, `REV1` |
| `MDR042_PLANNED_DATE_IN_PAST` | Warning | Business | Planned date is before the processing date. | `2020-01-15` (row stays valid) |
| `MDR043_INVALID_STATUS` | Error | Business | Status is not one of Planned, InProgress, Issued, OnHold, Cancelled. | `Approved`, `2` |
| `MDR050_AI_UNAVAILABLE` | Info | AI enrichment | The AI classifier could not be reached; documents were stored without suggestions. | Sidecar down, timeout, HTTP 5xx, open circuit breaker (row 0, once per batch) |
| `MDR051_AI_DISCIPLINE_MISMATCH` | Info | AI enrichment | AI suggests a different discipline; document flagged for review. | Row says MEC, classifier says ELE |
| `MDR052_AI_DOCTYPE_MISMATCH` | Info | AI enrichment | AI suggests a different document type; document flagged for review. | Row says DWG, classifier says SPE |
| `MDR053_AI_LOW_CONFIDENCE` | Info | AI enrichment | AI confidence is below the configured threshold; document flagged for review. | Confidence below `MdrProcessing:AiLowConfidenceThreshold` (default 0.6) |
| `MDR054_AI_POTENTIAL_DUPLICATE` | Info | AI enrichment | AI found a near-duplicate title; document flagged for review. | TF-IDF cosine similarity above the sidecar threshold with an earlier or existing title |
| `MDR055_AI_MISSING_METADATA` | Info | AI enrichment | AI reports metadata that would improve classification (e.g. description). | Description missing / too short to classify |
| `MDR056_AI_SUGGESTION_REJECTED` | Info | AI enrichment | AI returned a code outside the reference data; the suggestion was discarded. | Classifier returned a code outside the reference data |
| `MDR100_FILE_EMPTY` | Error | File (upload) | The uploaded file is empty. | 0-byte upload |
| `MDR101_FILE_EXTENSION` | Error | File (upload) | Only .csv files are accepted. | `mdr.xlsx` |
| `MDR102_FILE_TOO_LARGE` | Error | File (upload) | The file exceeds the configured maximum size. | Over `MdrProcessing:MaxFileSizeBytes` (declared length or bytes actually streamed) |
| `MDR103_FILE_ENCODING` | Error | File (upload) | The file is not valid UTF-8 (UTF-8 with or without BOM is accepted). | Windows-1252 bytes, UTF-16 BOM, NUL bytes |
| `MDR104_HEADER_MISSING` | Error | File (upload) | The file has no header row. | Whitespace-only file, or first row is data |
| `MDR105_REQUIRED_COLUMN_MISSING` | Error | File | A required column is missing from the header. | No `GateSet` column |
| `MDR106_DUPLICATE_COLUMN` | Error | File | Two header columns map to the same field. | `DocumentNumber` and `Doc No` both present |
| `MDR107_UNKNOWN_COLUMN` | Warning | File | A header column is not recognised and will be ignored. | `Remarks` (ignored) |
| `MDR108_NO_DATA_ROWS` | Error | File | The file has a header but no data rows. | Header only |
| `MDR109_TOO_MANY_ROWS` | Error | File | The file exceeds the configured maximum number of rows. | More than `MdrProcessing:MaxRows` (default 50,000) |
| `MDR110_FILE_UNREADABLE` | Error | File | The stored file could not be opened for processing. | Stored file missing when the background job runs |

## Ranges

| Range | Stage |
|---|---|
| `MDR001-009` | Schema: required values, lengths, types, row shape |
| `MDR010-019` | Document number format and segment consistency |
| `MDR020-029` | Duplicate detection (in file, against the register) |
| `MDR030-039` | Reference data and WBS lookups |
| `MDR040-049` | Field rules (quantity, revision, dates, status) |
| `MDR050-059` | AI enrichment - always `Info`, never invalidates a row |
| `MDR100-199` | File level (row 0) |

A row can carry several codes at once: the pipeline reports every problem in a single pass so a
document controller can fix a file in one iteration. Values that fail schema validation are not
re-checked by business rules (no double reporting).
