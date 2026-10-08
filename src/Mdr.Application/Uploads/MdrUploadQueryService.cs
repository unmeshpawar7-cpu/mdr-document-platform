using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Processing.Reporting;

namespace Mdr.Application.Uploads;

public interface IMdrUploadQueryService
{
    Task<UploadStatusDto> GetUploadAsync(Guid uploadId, CancellationToken cancellationToken);

    Task<PagedResult<ValidationIssueDto>> GetIssuesAsync(Guid uploadId, IssueFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<PagedResult<MdrDocumentDto>> GetDocumentsAsync(Guid uploadId, DocumentFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<UploadSummaryDto> GetSummaryAsync(Guid uploadId, CancellationToken cancellationToken);

    Task<ValidationReportFile> GetReportAsync(Guid uploadId, CancellationToken cancellationToken);

    Task<PagedResult<UploadHistoryItemDto>> GetProjectUploadsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken);
}

public sealed class MdrUploadQueryService : IMdrUploadQueryService
{
    private readonly IMdrQueries _queries;
    private readonly IValidationReportWriter _reportWriter;
    private readonly MdrSummaryBuilder _summaryBuilder;
    private readonly Projects.IProjectQueries _projectQueries;

    public MdrUploadQueryService(
        IMdrQueries queries,
        IValidationReportWriter reportWriter,
        MdrSummaryBuilder summaryBuilder,
        Projects.IProjectQueries projectQueries)
    {
        _queries = queries;
        _reportWriter = reportWriter;
        _summaryBuilder = summaryBuilder;
        _projectQueries = projectQueries;
    }

    public async Task<UploadStatusDto> GetUploadAsync(Guid uploadId, CancellationToken cancellationToken) =>
        await _queries.GetUploadAsync(uploadId, cancellationToken) ?? throw new NotFoundException("Upload", uploadId);

    public async Task<PagedResult<ValidationIssueDto>> GetIssuesAsync(Guid uploadId, IssueFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        await GetUploadAsync(uploadId, cancellationToken);
        return await _queries.GetIssuesAsync(uploadId, filter, page, cancellationToken);
    }

    public async Task<PagedResult<MdrDocumentDto>> GetDocumentsAsync(Guid uploadId, DocumentFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        await GetUploadAsync(uploadId, cancellationToken);
        return await _queries.GetDocumentsAsync(uploadId, filter, page, cancellationToken);
    }

    public async Task<UploadSummaryDto> GetSummaryAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        var data = await _queries.GetSummaryDataAsync(uploadId, cancellationToken) ?? throw new NotFoundException("Upload", uploadId);
        return _summaryBuilder.Build(data);
    }

    public async Task<ValidationReportFile> GetReportAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        await GetUploadAsync(uploadId, cancellationToken);
        using var buffer = new MemoryStream();
        await _reportWriter.WriteAsync(_queries.StreamReportRowsAsync(uploadId, cancellationToken), buffer, cancellationToken);
        return new ValidationReportFile($"mdr-validation-report-{uploadId:N}.csv", "text/csv", buffer.ToArray());
    }

    public async Task<PagedResult<UploadHistoryItemDto>> GetProjectUploadsAsync(Guid projectId, PageRequest page, CancellationToken cancellationToken)
    {
        if (!await _projectQueries.ExistsAsync(projectId, cancellationToken))
        {
            throw new NotFoundException("Project", projectId);
        }

        return await _queries.GetProjectUploadsAsync(projectId, page, cancellationToken);
    }
}
