# ADR 0004: Classification in a Python sidecar

* Status: Accepted
* Date: 2026-10-08

## Context
Text classification and similarity tooling (scikit-learn, LLM SDK ecosystems, evaluation notebooks) is strongest in
Python, while the transactional MDR core is .NET.

## Decision
A separate FastAPI service (`src/ai-classifier`) behind `POST /v1/classify`, called with a typed `HttpClient` and the
standard resilience handler, authenticated with a shared `X-Api-Key`. Default provider `offline` is a deterministic
taxonomy scorer (YAML weights) plus TF-IDF cosine duplicates; `LLM_PROVIDER=openai|anthropic` adds an LLM whose output
is validated with Pydantic. The contract is batch-oriented (up to 100 items, plus existing titles for duplicate hints).

## Consequences
* Independent deployment, scaling and release cadence; ML dependencies stay out of the API image.
* One more network hop and service to operate; mitigated by batching, timeouts, circuit breaker and graceful
  degradation (ADR 0003). The API health check reports the sidecar as `Degraded`, never `Unhealthy`.
* The contract is duplicated in C# wire records and Pydantic models; both have tests on their side.
