# MDR CSV format

* Encoding: **UTF-8**, with or without BOM ("CSV UTF-8" in Excel). UTF-16/Windows-1252 files are rejected (`MDR103`).
* Delimiter `,`, RFC 4180 quoting (quoted fields may contain commas and line breaks). Blank lines are ignored.
* First non-blank row is the header. Header matching ignores case, spaces and punctuation, and accepts aliases.
* Row numbers in reports follow the spreadsheet: header = row 1, first data row = row 2.
* Limits: `MdrProcessing:MaxFileSizeBytes` (default 10 MiB) and `MdrProcessing:MaxRows` (default 50,000).

| Column | Required | Max | Rule | Accepted header aliases |
|---|---|---|---|---|
| `DocumentNumber` | yes | 50 | Format below; unique per project | Document Number, Document No, Doc No, Doc Number, DocNo |
| `Title` | yes | 250 | | Document Title |
| `Description` | no | 2000 | Improves AI suggestions | Desc, Document Description |
| `Discipline` | yes | 10 | Active discipline code | Discipline Code |
| `DocumentType` | yes | 10 | Active document type code | Document Type, Doc Type, Type, Document Type Code |
| `WBS` | yes | 30 | Existing WBS code of the project (`1.2.3`) | WBS Code, WBS Element |
| `GateSet` | yes | 10 | Active gate set allowed for the document type | Gate Set, Gates |
| `Office` | yes | 10 | Active office code | Office Code, Originating Office |
| `Quantity` | yes | 10 | Whole number > 0 (sheets / volumes) | Qty |
| `Revision` | yes | 5 | 1-3 upper-case letters or digits (`A`, `0`, `P01`) | Rev |
| `PlannedDate` | no | 20 | ISO `yyyy-MM-dd`; past dates are a warning | Planned Date, Planned Issue Date, Target Date |
| `Status` | no | 20 | Planned (default), InProgress, Issued, OnHold, Cancelled (case/spaces ignored) | Document Status |

Values are trimmed. Codes, document numbers and revisions are upper-cased. Unknown columns produce a warning and are ignored.

## Document number

```
{ProjectCode}-{Office}-{Discipline}-{DocType}-{Seq}
NWP-001     - PUN    - MEC        - DWG     - 0012
```

| Segment | Pattern | Must equal |
|---|---|---|
| ProjectCode | `[A-Z]{2,5}-\d{3}` | the upload's project code (`MDR011`) |
| Office | `[A-Z]{3}` | the `Office` column (`MDR012`) |
| Discipline | `[A-Z]{3}` | the `Discipline` column (`MDR013`) |
| DocType | `[A-Z]{2,3}` | the `DocumentType` column (`MDR014`) |
| Seq | `\d{4}`, 0001-9999 | - |

## Reference data (seeded)

* Disciplines: `ARC` Architectural, `CIV` Civil, `ELE` Electrical, `HSE` Health, Safety & Environment,
  `INS` Instrumentation & Control, `MEC` Mechanical, `PRO` Process, `STR` Structural.
* Document types: `CAL` Calculation, `DS` Datasheet, `DWG` Drawing, `LST` List / Register,
  `PID` Piping & Instrumentation Diagram, `PRC` Procedure, `REP` Report, `SPE` Specification.
* Offices (fictional): `HOU` Houston, `LDN` London, `PUN` Pune.

### Gate sets

A gate set is the ordered sequence of issue gates a deliverable passes through. Each document type has an allow-list.

| Gate set | Stages | Used for | Allowed document types |
|---|---|---|---|
| `STD` | IFR > IFA > IFC (Issued for Review, Approval, Construction) | Standard design deliverables | all |
| `PROC` | IFR > IFQ > IFP (Review, Quotation, Purchase) | Procurement packages | DS, LST, SPE |
| `INFO` | IFI (Issued for Information) | Information-only documents | CAL, DWG, LST, REP |
| `REG` | IFR > IFA > IFU (Review, Approval, Use) | Regulatory / HSE deliverables | PRC, REP |

## Processing policy

`MdrProcessing:Policy`:

* `PersistValidRows` (default) - valid rows are stored, invalid rows are reported; status `CompletedWithErrors`
  when at least one row failed.
* `RejectFileOnAnyError` - all-or-nothing; if any row has an `Error` nothing is stored, findings are still saved for
  the report, status `Failed`.

See [ADR 0002](adr/0002-persist-valid-rows-policy.md). Duplicate rule: within a file the first occurrence of a document
number owns it; later rows get `MDR020` even if the first row is itself invalid.
