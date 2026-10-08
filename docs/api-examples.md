# API examples

Base URL `http://localhost:8080` (docker compose, Development environment with demo users).
All responses use the envelope `{ "success": true, "data": ..., "message": null }`; errors use
`{ "success": false, "message": "...", "errors": [{ "field": "...", "message": "...", "code": "..." }], "traceId": "..." }`.

## 1. Token

```bash
TOKEN=$(curl -s http://localhost:8080/api/auth/token \
  -H 'Content-Type: application/json' \
  -d '{"userName":"controller","password":"Controller!Demo2026"}' | jq -r .data.accessToken)
```

## 2. Project and WBS

```bash
PROJECT=$(curl -s http://localhost:8080/api/projects -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"code":"NWP-001","name":"Northwind Water Plant","client":"Northwind Utilities (fictional)"}' | jq -r .data.id)

curl -s http://localhost:8080/api/projects/$PROJECT/wbs -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"elements":[
    {"code":"1","name":"Project management"},{"code":"1.1","name":"Engineering management"},
    {"code":"2","name":"Intake"},{"code":"2.1","name":"Raw water intake"},{"code":"2.2","name":"Screening"},
    {"code":"3","name":"Treatment"},{"code":"3.1","name":"Coagulation"},{"code":"3.2","name":"Filtration"},
    {"code":"3.3","name":"Disinfection"},{"code":"4","name":"Utilities"},{"code":"4.1","name":"Power"},
    {"code":"4.2","name":"Controls"},{"code":"5","name":"Buildings and civil"},{"code":"5.1","name":"Site works"},
    {"code":"5.2","name":"Process buildings"}]}'
```

## 3. Upload (202) and poll

```bash
curl -si http://localhost:8080/api/mdr/upload -H "Authorization: Bearer $TOKEN" \
  -F projectId=$PROJECT -F file=@samples/mdr-with-errors.csv
# HTTP/1.1 202 Accepted
# Location: /api/mdr/3f6c...
```

```json
{ "success": true,
  "data": { "uploadId": "3f6c0d8e-...", "projectId": "9b1e...", "status": "Queued",
            "statusUrl": "/api/mdr/3f6c0d8e-...", "uploadedAtUtc": "2026-10-08T09:15:02.113Z" },
  "message": "Upload queued for processing." }
```

```bash
curl -s http://localhost:8080/api/mdr/$UPLOAD -H "Authorization: Bearer $TOKEN"
```

```json
{ "success": true, "data": {
    "id": "3f6c0d8e-...", "projectCode": "NWP-001", "originalFileName": "mdr-with-errors.csv",
    "status": "CompletedWithErrors",
    "counters": { "totalRows": 27, "validRows": 6, "invalidRows": 21, "duplicateRows": 2, "insertedRows": 6 },
    "processingAttempts": 1, "processingDurationMs": 412, "failureReason": null } }
```
(Counters as produced after `mdr-valid.csv` was uploaded first; the duration is illustrative.)

## 4. Results

```bash
curl -s "http://localhost:8080/api/mdr/$UPLOAD/validation?severity=Error&page=1&pageSize=20" -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/mdr/$UPLOAD/validation?code=MDR034_GATESET_NOT_ALLOWED" -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/mdr/$UPLOAD/documents?discipline=MEC&needsReview=true" -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/mdr/$UPLOAD/summary" -H "Authorization: Bearer $TOKEN"
curl -s "http://localhost:8080/api/mdr/$UPLOAD/report" -H "Authorization: Bearer $TOKEN" -o report.csv
curl -s "http://localhost:8080/api/projects/$PROJECT/uploads" -H "Authorization: Bearer $TOKEN"
```

Validation issue item:

```json
{ "id": 118, "rowNumber": 20, "column": "GateSet", "severity": "Error", "code": "MDR034_GATESET_NOT_ALLOWED",
  "message": "Gate set 'PROC' is not allowed for document type 'PID'. Allowed: STD.", "offendingValue": "PROC" }
```

## 5. Errors

```bash
# Identical file again -> 409
curl -s http://localhost:8080/api/mdr/upload -H "Authorization: Bearer $TOKEN" -F projectId=$PROJECT -F file=@samples/mdr-with-errors.csv
```

```json
{ "success": false, "message": "An identical file (same SHA-256 checksum) has already been uploaded for this project.",
  "errors": [{ "field": "", "message": "An identical file ...", "code": "upload_duplicate_file" }], "traceId": "c0ffee..." }
```

```bash
# Wrong extension -> 400 with MDR101
curl -s http://localhost:8080/api/mdr/upload -H "Authorization: Bearer $TOKEN" -F projectId=$PROJECT -F file=@notes.txt
```

## 6. Sidecar directly

```bash
curl -s http://localhost:8000/v1/classify -H "X-Api-Key: $CLASSIFIER_API_KEY" -H 'Content-Type: application/json' -d '{
  "items": [{"id": "a1", "title": "Raw water intake pump datasheet", "description": "Datasheet for vertical turbine pumps",
             "documentNumber": "NWP-001-PUN-MEC-DS-0002"}],
  "existingDocuments": [{"id": "e9", "title": "Intake pump datasheet"}] }'
```

```json
{ "classifierVersion": "taxonomy-2026.10+tfidf-1", "provider": "offline", "promptVersion": null,
  "results": [{ "id": "a1", "discipline": "MEC", "documentType": "DS", "category": "Rotating equipment",
                "priority": "Medium", "confidence": 0.972, "missingMetadata": [], "potentialDuplicateOf": null,
                "rationale": "discipline MEC via pump, pumps; type DS via datasheet",
                "classifierVersion": "taxonomy-2026.10+tfidf-1" }],
  "usage": { "estimatedInputTokens": 0, "estimatedOutputTokens": 0, "llmAttempts": 0 } }
```
