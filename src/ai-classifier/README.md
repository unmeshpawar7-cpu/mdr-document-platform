# MDR AI Classifier (sidecar)

FastAPI service called by the .NET MDR API to enrich imported documents with **advisory** suggestions:
discipline, document type, category, priority, confidence, missing metadata and near-duplicate hints.

* `offline` (default): deterministic weighted-keyword scorer over `app/taxonomy/taxonomy.yaml` - a heuristic,
  not a language model; every suggestion lists the keywords that produced it.
* `openai` / `anthropic`: the LLM classifies the batch using the versioned prompt `app/ai/prompts/classify/v1.md`.
  Output must parse as JSON, validate against a Pydantic schema, return every requested id and use only known codes;
  otherwise the validator errors are fed back (max `LLM_MAX_VALIDATION_RETRIES`, default 2) and then the whole
  batch falls back to the offline scorer.
* Near duplicates always use TF-IDF (word uni/bi-grams, scikit-learn) cosine similarity against earlier items and
  the existing documents supplied by the caller (`DUPLICATE_THRESHOLD`, default 0.85).

```bash
python -m venv .venv && . .venv/bin/activate
pip install -e ".[dev]"
export CLASSIFIER_API_KEY=local-dev-key-0123456789
uvicorn app.main:create_app --factory --port 8000
ruff check . && pytest --cov=app
```

Endpoints: `GET /health`, `GET /v1/taxonomy` and `POST /v1/classify` (both require `X-Api-Key`).
