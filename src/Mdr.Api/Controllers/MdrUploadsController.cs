using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Mdr.Api.Controllers;

/// <summary>MDR CSV uploads: asynchronous processing, status polling, validation results and reports.</summary>
[ApiController]
[Route("api/mdr")]
[Produces("application/json")]
[Authorize(Policy = Policies.CanRead)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class MdrUploadsController : ControllerBase
{
    private readonly IMdrUploadService _uploadService;
    private readonly IMdrUploadQueryService _queries;

    public MdrUploadsController(IMdrUploadService uploadService, IMdrUploadQueryService queries)
    {
        _uploadService = uploadService;
        _queries = queries;
    }

    /// <summary>Uploads an MDR CSV for background processing.</summary>
    /// <remarks>
    /// Size and extension are checked before the body is read; the file is streamed to storage under a generated
    /// name while its SHA-256 is computed. Re-uploading identical content for the same project returns 409.
    /// Poll the <c>Location</c> URL until the status is Completed, CompletedWithErrors or Failed.
    /// </remarks>
    /// <response code="202">Accepted; Location points to the upload status resource.</response>
    /// <response code="400">File rejected (extension, size, encoding, missing header) - see errors[].code.</response>
    /// <response code="404">Project not found.</response>
    /// <response code="409">Identical file already uploaded for this project.</response>
    /// <response code="422">Project is not active.</response>
    /// <response code="429">Upload rate limit exceeded.</response>
    [HttpPost("upload")]
    [Authorize(Policy = Policies.CanManage)]
    [EnableRateLimiting(RateLimitPolicies.Uploads)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(ApiResponse<UploadAcceptedDto>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Upload([FromForm] UploadMdrForm form, CancellationToken cancellationToken)
    {
        if (form.File is null || form.ProjectId is null)
        {
            throw new RequestValidationException(
                "Validation failed.",
                new[] { new FieldError("file", "Both projectId and file are required.") });
        }

        await using var content = form.File.OpenReadStream();
        var accepted = await _uploadService.UploadAsync(
            new UploadMdrFileCommand(form.ProjectId.Value, form.File.FileName, form.File.Length, content),
            cancellationToken);
        return Accepted(accepted.StatusUrl, ApiResponse.Ok(accepted, "Upload queued for processing."));
    }

    /// <summary>Upload status and row counters.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UploadStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UploadStatusDto>>> GetStatus(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await _queries.GetUploadAsync(id, cancellationToken)));

    /// <summary>Validation issues of an upload (paged; filter by severity, code or row).</summary>
    [HttpGet("{id:guid}/validation")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ValidationIssueDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<ValidationIssueDto>>>> GetValidation(
        Guid id,
        [FromQuery] string? severity,
        [FromQuery] string? code,
        [FromQuery] int? row,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.GetIssuesAsync(id, new IssueFilter(severity, code, row), new PageRequest(page, pageSize), cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Documents persisted from an upload (paged; filter by discipline, type or needsReview).</summary>
    [HttpGet("{id:guid}/documents")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<MdrDocumentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<MdrDocumentDto>>>> GetDocuments(
        Guid id,
        [FromQuery] string? discipline,
        [FromQuery] string? documentType,
        [FromQuery] bool? needsReview,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.GetDocumentsAsync(
            id,
            new DocumentFilter(discipline, documentType, needsReview),
            new PageRequest(page, pageSize),
            cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Aggregated counts by discipline, type, gate set and severity, AI review statistics and duration.</summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(ApiResponse<UploadSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<UploadSummaryDto>>> GetSummary(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await _queries.GetSummaryAsync(id, cancellationToken)));

    /// <summary>Downloads the validation report as CSV (formula-injection safe).</summary>
    [HttpGet("{id:guid}/report")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetReport(Guid id, CancellationToken cancellationToken)
    {
        var report = await _queries.GetReportAsync(id, cancellationToken);
        return File(report.Content, report.ContentType, report.FileName);
    }
}
