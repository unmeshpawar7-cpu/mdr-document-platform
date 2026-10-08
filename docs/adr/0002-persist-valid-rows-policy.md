# ADR 0002: Persist valid rows by default, reject-on-error as an option

* Status: Accepted
* Date: 2026-10-08

## Context
Real MDR exports are large and rarely perfect. Document controllers want progress on the valid 95% while fixing the
rest; some clients contractually require an all-or-nothing register submission.

## Decision
`MdrProcessing:Policy` with `PersistValidRows` (default) and `RejectFileOnAnyError`. Validation always runs to the end
and reports **every** problem of every row in one pass. Under `PersistValidRows` valid rows, findings and counters are
written in one transaction and the upload ends `CompletedWithErrors`. Under `RejectFileOnAnyError` only findings are
written and the upload ends `Failed` with a policy reason. Warnings and AI `Info` issues never invalidate a row.

## Consequences
* Re-uploading a corrected file adds the previously rejected rows; rows already registered are reported as `MDR021`,
  so a corrected full file needs those rows removed (or a future "update revision" flow).
* Identical bytes are rejected outright (SHA-256 per project, `409`), preventing accidental double submissions.
* Counters are consistent by construction (`valid + invalid = total`, `duplicates ⊆ invalid`) and enforced in the domain.
