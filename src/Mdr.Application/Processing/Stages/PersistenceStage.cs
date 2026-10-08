using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Domain.Documents;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Application.Processing.Stages;

public sealed record PersistenceRequest(
    MdrUpload Upload,
    IReadOnlyList<ResolvedMdrRow> ValidRows,
    IReadOnlyList<ValidationFinding> Findings,
    bool HasRejectedRows);

/// <summary>Lightweight snapshot of an inserted document (entities are detached after each batch).</summary>
public sealed record PersistedDocument(
    Guid Id,
    int RowNumber,
    string DocumentNumber,
    string Title,
    string? Description,
    string DisciplineCode,
    string DocumentTypeCode);

public sealed record PersistenceResult(int InsertedCount, bool RejectedByPolicy, IReadOnlyList<PersistedDocument> Documents);

/// <summary>
/// Stage 5: writes valid rows (policy permitting), all validation findings and the upload counters inside one
/// transaction, in batches of <see cref="MdrProcessingOptions.PersistenceBatchSize"/>. Each batch is saved and then
/// detached so the change tracker stays small for large files.
/// </summary>
public interface IPersistenceStage
{
    Task<PersistenceResult> PersistAsync(PersistenceRequest request, CancellationToken cancellationToken);
}

public sealed class PersistenceStage : IPersistenceStage
{
    private readonly IDocumentRepository _documents;
    private readonly IValidationIssueRepository _issues;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<MdrProcessingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PersistenceStage> _logger;

    public PersistenceStage(
        IDocumentRepository documents,
        IValidationIssueRepository issues,
        IUnitOfWork unitOfWork,
        IOptions<MdrProcessingOptions> options,
        TimeProvider timeProvider,
        ILogger<PersistenceStage> logger)
    {
        _documents = documents;
        _issues = issues;
        _unitOfWork = unitOfWork;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PersistenceResult> PersistAsync(PersistenceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = _options.Value;
        var upload = request.Upload;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rejectedByPolicy = options.Policy == ProcessingPolicy.RejectFileOnAnyError && request.HasRejectedRows;
        var persisted = new List<PersistedDocument>();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(cancellationToken);

        if (!rejectedByPolicy)
        {
            foreach (var batch in request.ValidRows.Chunk(options.PersistenceBatchSize))
            {
                var documents = batch.Select(row => MdrDocument.Create(
                        upload.ProjectId,
                        upload.Id,
                        row.RowNumber,
                        row.DocumentNumber,
                        row.Title,
                        row.Description,
                        row.Discipline.Id,
                        row.DocumentType.Id,
                        row.Office.Id,
                        row.GateSet.Id,
                        row.WbsElementId,
                        row.Quantity,
                        row.Revision,
                        row.PlannedDate,
                        row.Status,
                        now))
                    .ToList();

                _documents.AddRange(documents);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                _unitOfWork.Detach(documents);

                for (var i = 0; i < documents.Count; i++)
                {
                    var document = documents[i];
                    persisted.Add(new PersistedDocument(
                        document.Id,
                        document.SourceRowNumber,
                        document.DocumentNumber,
                        document.Title,
                        document.Description,
                        batch[i].Discipline.Code,
                        batch[i].DocumentType.Code));
                }
            }
        }

        foreach (var batch in request.Findings.Chunk(options.PersistenceBatchSize))
        {
            var issues = batch.Select(f => f.ToIssue(upload.Id, now)).ToList();
            _issues.AddRange(issues);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            _unitOfWork.Detach<ValidationIssue>(issues);
        }

        upload.RecordInserted(persisted.Count);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Persisted {Inserted} documents and {Issues} validation issues for upload {UploadId} (policy {Policy}, rejected {Rejected})",
            persisted.Count,
            request.Findings.Count,
            upload.Id,
            options.Policy,
            rejectedByPolicy);

        return new PersistenceResult(persisted.Count, rejectedByPolicy, persisted);
    }
}
