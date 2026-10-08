using Mdr.Application.Common.Abstractions;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Projects;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Application.Processing;

public enum ProcessingOutcome
{
    NotFound,
    AlreadyProcessed,
    Completed,
    CompletedWithErrors,
    Failed,
}

/// <summary>Runs the full CSV pipeline for one queued upload.</summary>
public interface IMdrUploadProcessor
{
    Task<ProcessingOutcome> ProcessAsync(Guid uploadId, CancellationToken cancellationToken);
}

/// <summary>
/// Orchestrates: file validation → header/schema validation → business validation → duplicate detection →
/// persistence → AI enrichment → report/finalisation. Rows are streamed; only compact parsed rows of
/// candidate documents are held in memory.
/// </summary>
public sealed class MdrUploadProcessor : IMdrUploadProcessor
{
    private readonly IUploadRepository _uploads;
    private readonly IProjectRepository _projects;
    private readonly IDocumentRepository _documents;
    private readonly IValidationIssueRepository _issues;
    private readonly IReferenceDataRepository _referenceData;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _storage;
    private readonly IMdrCsvReader _csvReader;
    private readonly IFileValidationStage _fileStage;
    private readonly IHeaderMapper _headerMapper;
    private readonly ISchemaValidationStage _schemaStage;
    private readonly IBusinessValidationStage _businessStage;
    private readonly IDuplicateDetectionStage _duplicateStage;
    private readonly IPersistenceStage _persistenceStage;
    private readonly IClassificationEnrichmentStage _enrichmentStage;
    private readonly IAuditTrail _audit;
    private readonly IOptions<MdrProcessingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MdrUploadProcessor> _logger;

    public MdrUploadProcessor(
        IUploadRepository uploads,
        IProjectRepository projects,
        IDocumentRepository documents,
        IValidationIssueRepository issues,
        IReferenceDataRepository referenceData,
        IUnitOfWork unitOfWork,
        IFileStorage storage,
        IMdrCsvReader csvReader,
        IFileValidationStage fileStage,
        IHeaderMapper headerMapper,
        ISchemaValidationStage schemaStage,
        IBusinessValidationStage businessStage,
        IDuplicateDetectionStage duplicateStage,
        IPersistenceStage persistenceStage,
        IClassificationEnrichmentStage enrichmentStage,
        IAuditTrail audit,
        IOptions<MdrProcessingOptions> options,
        TimeProvider timeProvider,
        ILogger<MdrUploadProcessor> logger)
    {
        _uploads = uploads;
        _projects = projects;
        _documents = documents;
        _issues = issues;
        _referenceData = referenceData;
        _unitOfWork = unitOfWork;
        _storage = storage;
        _csvReader = csvReader;
        _fileStage = fileStage;
        _headerMapper = headerMapper;
        _schemaStage = schemaStage;
        _businessStage = businessStage;
        _duplicateStage = duplicateStage;
        _persistenceStage = persistenceStage;
        _enrichmentStage = enrichmentStage;
        _audit = audit;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<ProcessingOutcome> ProcessAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var upload = await _uploads.GetByIdAsync(uploadId, cancellationToken);
        if (upload is null)
        {
            _logger.LogWarning("Upload {UploadId} not found; skipping", uploadId);
            return ProcessingOutcome.NotFound;
        }

        if (upload.IsTerminal)
        {
            _logger.LogInformation("Upload {UploadId} already in terminal status {Status}; skipping", uploadId, upload.Status);
            return ProcessingOutcome.AlreadyProcessed;
        }

        if (upload.ProcessingAttempts >= _options.Value.MaxProcessingAttempts)
        {
            upload.Fail($"Processing abandoned after {upload.ProcessingAttempts} attempts.", UtcNow);
            RecordCompletionAudit(upload);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ProcessingOutcome.Failed;
        }

        if (upload.Status == UploadStatus.Processing)
        {
            // Crash recovery: remove partial results of the interrupted attempt so re-processing is idempotent.
            var removedDocuments = await _documents.DeleteByUploadAsync(upload.Id, cancellationToken);
            var removedIssues = await _issues.DeleteByUploadAsync(upload.Id, cancellationToken);
            _logger.LogWarning(
                "Re-processing interrupted upload {UploadId}; removed {Documents} documents and {Issues} issues from the previous attempt",
                upload.Id,
                removedDocuments,
                removedIssues);
        }

        upload.StartProcessing(UtcNow);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Processing upload {UploadId} (attempt {Attempt})", upload.Id, upload.ProcessingAttempts);

        try
        {
            var project = await _projects.GetByIdAsync(upload.ProjectId, cancellationToken);
            if (project is null)
            {
                return await FailAsync(upload, "Project no longer exists.", Array.Empty<ValidationFinding>(), cancellationToken);
            }

            return await RunPipelineAsync(upload, project, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Processing of upload {UploadId} cancelled; it will be recovered on next start", uploadId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Processing of upload {UploadId} failed unexpectedly", uploadId);
            _unitOfWork.ClearChangeTracker();
            var fresh = await _uploads.GetByIdAsync(uploadId, CancellationToken.None);
            if (fresh is not null && !fresh.IsTerminal)
            {
                await _documents.DeleteByUploadAsync(uploadId, CancellationToken.None);
                fresh.Fail($"Unexpected processing error ({ex.GetType().Name}). See server logs for upload {uploadId}.", UtcNow);
                RecordCompletionAudit(fresh);
                await _unitOfWork.SaveChangesAsync(CancellationToken.None);
            }

            return ProcessingOutcome.Failed;
        }
    }

    private async Task<ProcessingOutcome> RunPipelineAsync(MdrUpload upload, Project project, CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var findings = new List<ValidationFinding>();

        // Stage 1: file validation (defensive re-check of the stored file).
        findings.AddRange(_fileStage.ValidateMetadata(upload.OriginalFileName, upload.FileSizeBytes));
        if (!HasErrors(findings))
        {
            Stream stream;
            try
            {
                stream = await _storage.OpenReadAsync(upload.StoragePath, cancellationToken);
            }
            catch (IOException ex)
            {
                findings.Add(ValidationFinding.FileLevel(ValidationCodes.FileUnreadable, $"Stored file could not be opened: {ex.GetType().Name}."));
                return await FailAsync(upload, "Stored file could not be opened.", findings, cancellationToken);
            }

            await using (stream)
            {
                findings.AddRange(await _fileStage.ValidateContentAsync(stream, cancellationToken));
            }
        }

        if (HasErrors(findings))
        {
            return await FailAsync(upload, "File validation failed.", findings, cancellationToken);
        }

        // Stages 2 + 3: header mapping, schema and business validation while streaming rows.
        var referenceData = await _referenceData.GetSnapshotAsync(cancellationToken);
        var wbs = await _projects.GetWbsLookupAsync(project.Id, cancellationToken);
        var context = new BusinessValidationContext(project.Code, referenceData, wbs, DateOnly.FromDateTime(UtcNow));

        var candidates = new List<ResolvedMdrRow>();
        var occurrences = new List<DocumentNumberOccurrence>();
        var invalidRows = new HashSet<int>();
        var totalRows = 0;
        var tooManyRows = false;
        HeaderMapping? mapping = null;

        await using (var stream = await _storage.OpenReadAsync(upload.StoragePath, cancellationToken))
        {
            await foreach (var record in _csvReader.ReadRecordsAsync(stream, cancellationToken))
            {
                if (mapping is null)
                {
                    if (record.IsBlank)
                    {
                        continue;
                    }

                    mapping = _headerMapper.Map(record.Fields);
                    findings.AddRange(mapping.Findings);
                    if (!mapping.IsUsable)
                    {
                        break;
                    }

                    continue;
                }

                if (record.IsBlank)
                {
                    continue;
                }

                totalRows++;
                if (totalRows > options.MaxRows)
                {
                    tooManyRows = true;
                    findings.Add(ValidationFinding.FileLevel(
                        ValidationCodes.TooManyRows,
                        $"The file has more than {options.MaxRows} data rows. Split it into smaller files."));
                    break;
                }

                var schema = _schemaStage.Validate(record, mapping);
                var business = _businessStage.Validate(schema.Row, context);
                findings.AddRange(schema.Findings);
                findings.AddRange(business.Findings);

                if (schema.Row.DocumentNumber is not null)
                {
                    occurrences.Add(new DocumentNumberOccurrence(record.RowNumber, schema.Row.DocumentNumber));
                }

                if (schema.HasErrors || business.HasErrors || business.Resolved is null)
                {
                    invalidRows.Add(record.RowNumber);
                }
                else
                {
                    candidates.Add(business.Resolved);
                }
            }
        }

        if (mapping is null)
        {
            findings.Add(ValidationFinding.FileLevel(ValidationCodes.HeaderMissing, "The file contains no header row."));
            return await FailAsync(upload, "The file contains no header row.", findings, cancellationToken);
        }

        if (!mapping.IsUsable)
        {
            return await FailAsync(upload, "Header validation failed.", findings, cancellationToken);
        }

        if (tooManyRows)
        {
            return await FailAsync(upload, $"The file exceeds the maximum of {options.MaxRows} rows.", findings, cancellationToken);
        }

        if (totalRows == 0)
        {
            findings.Add(ValidationFinding.FileLevel(ValidationCodes.NoDataRows, "The file has a header but no data rows."));
            return await FailAsync(upload, "The file has no data rows.", findings, cancellationToken);
        }

        // Stage 4: duplicates (in file + existing, one set-based query).
        var duplicates = await _duplicateStage.DetectAsync(project.Id, occurrences, cancellationToken);
        findings.AddRange(duplicates.Findings);
        invalidRows.UnionWith(duplicates.DuplicateRowNumbers);
        candidates.RemoveAll(c => duplicates.DuplicateRowNumbers.Contains(c.RowNumber));

        upload.RecordValidationResults(totalRows, candidates.Count, invalidRows.Count, duplicates.DuplicateRowNumbers.Count);

        // Stage 5: persistence (single transaction, batched).
        var persistence = await _persistenceStage.PersistAsync(
            new PersistenceRequest(upload, candidates, findings, HasRejectedRows: invalidRows.Count > 0),
            cancellationToken);

        if (persistence.RejectedByPolicy)
        {
            upload.Fail(
                $"Rejected by policy {nameof(ProcessingPolicy.RejectFileOnAnyError)}: {invalidRows.Count} of {totalRows} rows failed validation. Nothing was persisted.",
                UtcNow);
            RecordCompletionAudit(upload);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ProcessingOutcome.Failed;
        }

        // Stage 6: advisory AI enrichment (never fails the upload).
        var enrichment = await _enrichmentStage.EnrichAsync(
            new EnrichmentRequest(upload.Id, project.Id, persistence.Documents, referenceData),
            cancellationToken);

        // Stage 7: report / finalisation.
        if (enrichment.Findings.Count > 0)
        {
            var now = UtcNow;
            _issues.AddRange(enrichment.Findings.Select(f => f.ToIssue(upload.Id, now)));
        }

        upload.Complete(UtcNow);
        RecordCompletionAudit(upload, new { enrichment.ClassifiedCount, enrichment.NeedsReviewCount, enrichment.FailedBatches });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Upload {UploadId} finished with {Status}: total {Total}, valid {Valid}, invalid {Invalid}, duplicates {Duplicates}, inserted {Inserted}",
            upload.Id,
            upload.Status,
            upload.TotalRows,
            upload.ValidRows,
            upload.InvalidRows,
            upload.DuplicateRows,
            upload.InsertedRows);

        return upload.Status == UploadStatus.Completed ? ProcessingOutcome.Completed : ProcessingOutcome.CompletedWithErrors;
    }

    private async Task<ProcessingOutcome> FailAsync(
        MdrUpload upload,
        string reason,
        IReadOnlyList<ValidationFinding> findings,
        CancellationToken cancellationToken)
    {
        var now = UtcNow;
        foreach (var batch in findings.Chunk(_options.Value.PersistenceBatchSize))
        {
            _issues.AddRange(batch.Select(f => f.ToIssue(upload.Id, now)));
        }

        upload.Fail(reason, now);
        RecordCompletionAudit(upload);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogWarning("Upload {UploadId} failed: {Reason}", upload.Id, reason);
        return ProcessingOutcome.Failed;
    }

    private void RecordCompletionAudit(MdrUpload upload, object? extra = null)
    {
        var action = upload.Status == UploadStatus.Failed ? AuditActions.UploadFailed : AuditActions.UploadCompleted;
        _audit.Record(
            action,
            nameof(MdrUpload),
            upload.Id.ToString(),
            new
            {
                Status = upload.Status.ToString(),
                upload.TotalRows,
                upload.ValidRows,
                upload.InvalidRows,
                upload.DuplicateRows,
                upload.InsertedRows,
                upload.FailureReason,
                Extra = extra,
            },
            userName: "system",
            correlationId: upload.CorrelationId);
    }

    private static bool HasErrors(IEnumerable<ValidationFinding> findings) => findings.Any(f => f.IsError);
}
