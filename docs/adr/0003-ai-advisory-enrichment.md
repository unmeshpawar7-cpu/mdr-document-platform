# ADR 0003: AI classification is advisory enrichment, never a gate

* Status: Accepted
* Date: 2026-10-08

## Context
Classifiers (heuristic or LLM) make mistakes and external services fail. The MDR is a contractual record: a wrong
discipline silently written by a model is worse than no suggestion.

## Decision
* AI runs **after** validated rows are committed and only adds suggestion fields plus a `NeedsReview` flag.
  It never changes the provided discipline/type and never rejects a row.
* All AI findings are `Info` severity (`MDR050-056`). Outages, timeouts, open circuits and even database errors while
  saving suggestions degrade to `MDR050` and the upload still completes.
* Outputs are distrusted: codes must exist in reference data, duplicate ids must be ids we sent, confidence is clamped,
  priorities must parse. The sidecar applies the same rules to LLM output (schema validation, known codes, corrective
  retries, heuristic fallback).
* Every document stores `AiClassifierVersion` (taxonomy version or provider/model/prompt version).

## Consequences
* Reviewers get a filterable queue (`GET /api/mdr/{id}/documents?needsReview=true`) and summary agreement rates.
* Enrichment adds latency after persistence; the upload stays `Processing` until enrichment finishes.
