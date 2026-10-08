using System.Runtime.CompilerServices;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Processing.Reporting;
using Mdr.Application.Uploads;
using Mdr.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Infrastructure.Persistence.Queries;

/// <summary>Read-side projections for uploads (AsNoTracking, DTO projection, paging in the database).</summary>
public sealed class MdrQueries : IMdrQueries
{
    private readonly MdrDbContext _db;

    public MdrQueries(MdrDbContext db) => _db = db;

    public async Task<UploadStatusDto?> GetUploadAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var row = await (
                from u in _db.Uploads.AsNoTracking()
                join p in _db.Projects.AsNoTracking() on u.ProjectId equals p.Id
                where u.Id == uploadId
                select new
                {
                    u.Id,
                    u.ProjectId,
                    ProjectCode = p.Code,
                    u.OriginalFileName,
                    u.FileSizeBytes,
                    u.ChecksumSha256,
                    u.UploadedBy,
                    u.UploadedAtUtc,
                    u.Status,
                    u.TotalRows,
                    u.ValidRows,
                    u.InvalidRows,
                    u.DuplicateRows,
                    u.InsertedRows,
                    u.ProcessingAttempts,
                    u.StartedAtUtc,
                    u.FinishedAtUtc,
                    u.FailureReason,
                })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        long? durationMs = row.StartedAtUtc.HasValue && row.FinishedAtUtc.HasValue
            ? (long)(row.FinishedAtUtc.Value - row.StartedAtUtc.Value).TotalMilliseconds
            : null;

        return new UploadStatusDto(
            row.Id,
            row.ProjectId,
            row.ProjectCode,
            row.OriginalFileName,
            row.FileSizeBytes,
            row.ChecksumSha256,
            row.UploadedBy,
            row.UploadedAtUtc,
            row.Status.ToString(),
            new UploadCountersDto(row.TotalRows, row.ValidRows, row.InvalidRows, row.DuplicateRows, row.InsertedRows),
            row.ProcessingAttempts,
            row.StartedAtUtc,
            row.FinishedAtUtc,
            durationMs,
            row.FailureReason);
    }

    public async Task<PagedResult<ValidationIssueDto>> GetIssuesAsync(
        Guid uploadId,
        IssueFilter filter,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = _db.ValidationIssues.AsNoTracking().Where(i => i.UploadId == uploadId);

        if (!string.IsNullOrWhiteSpace(filter.Severity))
        {
            if (!Enum.TryParse<IssueSeverity>(filter.Severity.Trim(), ignoreCase: true, out var severity) || !Enum.IsDefined(severity)
                || int.TryParse(filter.Severity, out _))
            {
                throw new RequestValidationException(
                    "Invalid filter.",
                    new[] { new FieldError("severity", "Severity must be Error, Warning or Info.") });
            }

            query = query.Where(i => i.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(filter.Code))
        {
            var code = filter.Code.Trim().ToUpperInvariant();
            query = query.Where(i => i.Code == code);
        }

        if (filter.Row.HasValue)
        {
            var rowNumber = filter.Row.Value;
            query = query.Where(i => i.RowNumber == rowNumber);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(i => i.RowNumber)
            .ThenBy(i => i.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(i => new { i.Id, i.RowNumber, i.ColumnName, i.Severity, i.Code, i.Message, i.OffendingValue })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(i => new ValidationIssueDto(i.Id, i.RowNumber, i.ColumnName, i.Severity.ToString(), i.Code, i.Message, i.OffendingValue))
            .ToList();
        return new PagedResult<ValidationIssueDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<PagedResult<MdrDocumentDto>> GetDocumentsAsync(
        Guid uploadId,
        DocumentFilter filter,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = _db.Documents.AsNoTracking().Where(d => d.UploadId == uploadId);

        if (!string.IsNullOrWhiteSpace(filter.Discipline))
        {
            var discipline = filter.Discipline.Trim().ToUpperInvariant();
            query = query.Where(d => d.Discipline!.Code == discipline);
        }

        if (!string.IsNullOrWhiteSpace(filter.DocumentType))
        {
            var documentType = filter.DocumentType.Trim().ToUpperInvariant();
            query = query.Where(d => d.DocumentType!.Code == documentType);
        }

        if (filter.NeedsReview.HasValue)
        {
            var needsReview = filter.NeedsReview.Value;
            query = query.Where(d => d.NeedsReview == needsReview);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(d => d.SourceRowNumber)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(d => new
            {
                d.Id,
                d.SourceRowNumber,
                d.DocumentNumber,
                d.Title,
                d.Description,
                Discipline = d.Discipline!.Code,
                DocumentType = d.DocumentType!.Code,
                Office = d.Office!.Code,
                GateSet = d.GateSet!.Code,
                Wbs = d.WbsElement!.Code,
                d.Quantity,
                d.Revision,
                d.PlannedDate,
                d.Status,
                d.NeedsReview,
                d.AiSuggestedDisciplineCode,
                d.AiSuggestedDocumentTypeCode,
                d.AiCategory,
                d.AiPriority,
                d.AiConfidence,
                d.AiPotentialDuplicateOf,
                d.AiMissingMetadata,
                d.AiRationale,
                d.AiClassifierVersion,
                d.AiClassifiedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(d => new MdrDocumentDto(
                d.Id,
                d.SourceRowNumber,
                d.DocumentNumber,
                d.Title,
                d.Description,
                d.Discipline,
                d.DocumentType,
                d.Office,
                d.GateSet,
                d.Wbs,
                d.Quantity,
                d.Revision,
                d.PlannedDate,
                d.Status.ToString(),
                d.NeedsReview,
                d.AiClassifiedAtUtc.HasValue
                    ? new AiSuggestionDto(
                        d.AiSuggestedDisciplineCode,
                        d.AiSuggestedDocumentTypeCode,
                        d.AiCategory,
                        d.AiPriority?.ToString(),
                        d.AiConfidence,
                        d.AiPotentialDuplicateOf,
                        d.AiMissingMetadata,
                        d.AiRationale,
                        d.AiClassifierVersion)
                    : null))
            .ToList();

        return new PagedResult<MdrDocumentDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<UploadSummaryData?> GetSummaryDataAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var upload = await GetUploadAsync(uploadId, cancellationToken);
        if (upload is null)
        {
            return null;
        }

        var documents = _db.Documents.AsNoTracking().Where(d => d.UploadId == uploadId);
        var issues = _db.ValidationIssues.AsNoTracking().Where(i => i.UploadId == uploadId);

        // Group by foreign key in the database, map ids to codes with the (tiny) reference tables.
        var disciplineCodes = await _db.Disciplines.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var typeCodes = await _db.DocumentTypes.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);
        var gateSetCodes = await _db.GateSets.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Code, cancellationToken);

        var byDiscipline = await documents.GroupBy(d => d.DisciplineId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var byType = await documents.GroupBy(d => d.DocumentTypeId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var byGateSet = await documents.GroupBy(d => d.GateSetId)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var bySeverity = await issues.GroupBy(i => i.Severity)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        var byCode = await issues.GroupBy(i => i.Code)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);

        var documentCount = await documents.CountAsync(cancellationToken);
        var classified = await documents.CountAsync(d => d.AiClassifiedAtUtc != null, cancellationToken);
        var needsReview = await documents.CountAsync(d => d.NeedsReview, cancellationToken);
        var disciplineMismatch = await documents.CountAsync(d => d.AiDisciplineMismatch, cancellationToken);
        var typeMismatch = await documents.CountAsync(d => d.AiDocumentTypeMismatch, cancellationToken);
        var averageConfidence = await documents
            .Where(d => d.AiConfidence != null)
            .Select(d => d.AiConfidence)
            .AverageAsync(cancellationToken);

        static string Code(Dictionary<int, string> map, int id) => map.TryGetValue(id, out var code) ? code : id.ToString();

        return new UploadSummaryData(
            upload,
            byDiscipline.Select(x => new CountByKey(Code(disciplineCodes, x.Key), x.Count)).ToList(),
            byType.Select(x => new CountByKey(Code(typeCodes, x.Key), x.Count)).ToList(),
            byGateSet.Select(x => new CountByKey(Code(gateSetCodes, x.Key), x.Count)).ToList(),
            bySeverity.Select(x => new CountByKey(x.Key.ToString(), x.Count)).ToList(),
            byCode.Select(x => new CountByKey(x.Key, x.Count)).ToList(),
            documentCount,
            classified,
            needsReview,
            disciplineMismatch,
            typeMismatch,
            averageConfidence);
    }

    public async Task<PagedResult<UploadHistoryItemDto>> GetProjectUploadsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.Uploads.AsNoTracking().Where(u => u.ProjectId == projectId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(u => u.UploadedAtUtc)
            .ThenBy(u => u.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(u => new
            {
                u.Id,
                u.OriginalFileName,
                u.UploadedBy,
                u.UploadedAtUtc,
                u.Status,
                u.TotalRows,
                u.ValidRows,
                u.InvalidRows,
                u.DuplicateRows,
                u.InsertedRows,
                u.FinishedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var items = rows.Select(u => new UploadHistoryItemDto(
                u.Id,
                u.OriginalFileName,
                u.UploadedBy,
                u.UploadedAtUtc,
                u.Status.ToString(),
                new UploadCountersDto(u.TotalRows, u.ValidRows, u.InvalidRows, u.DuplicateRows, u.InsertedRows),
                u.FinishedAtUtc))
            .ToList();
        return new PagedResult<UploadHistoryItemDto>(items, page.Page, page.PageSize, total);
    }

    public async IAsyncEnumerable<ValidationReportRow> StreamReportRowsAsync(
        Guid uploadId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var query = _db.ValidationIssues.AsNoTracking()
            .Where(i => i.UploadId == uploadId)
            .OrderBy(i => i.RowNumber)
            .ThenBy(i => i.Id)
            .Select(i => new { i.RowNumber, i.ColumnName, i.Severity, i.Code, i.Message, i.OffendingValue });

        await foreach (var i in query.AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            yield return new ValidationReportRow(i.RowNumber, i.ColumnName, i.Severity.ToString(), i.Code, i.Message, i.OffendingValue);
        }
    }
}
