using Mdr.Application.Common.Paging;
using Mdr.Application.Processing.Reporting;

namespace Mdr.Application.Uploads;

/// <summary>Read side for uploads: AsNoTracking projections straight to DTOs (implemented in Infrastructure).</summary>
public interface IMdrQueries
{
    Task<UploadStatusDto?> GetUploadAsync(Guid uploadId, CancellationToken cancellationToken);

    Task<PagedResult<ValidationIssueDto>> GetIssuesAsync(Guid uploadId, IssueFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<PagedResult<MdrDocumentDto>> GetDocumentsAsync(Guid uploadId, DocumentFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<UploadSummaryData?> GetSummaryDataAsync(Guid uploadId, CancellationToken cancellationToken);

    Task<PagedResult<UploadHistoryItemDto>> GetProjectUploadsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);

    IAsyncEnumerable<ValidationReportRow> StreamReportRowsAsync(Guid uploadId, CancellationToken cancellationToken);
}
