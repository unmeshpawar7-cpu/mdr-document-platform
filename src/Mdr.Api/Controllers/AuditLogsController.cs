using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application.Audit;
using Mdr.Application.Common.Paging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mdr.Api.Controllers;

/// <summary>Audit trail of state-changing actions (Admin only).</summary>
[ApiController]
[Route("api/audit-logs")]
[Produces("application/json")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class AuditLogsController : ControllerBase
{
    private readonly IAuditLogQueries _queries;

    public AuditLogsController(IAuditLogQueries queries) => _queries = queries;

    /// <summary>Lists audit entries, newest first, with optional filters.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AuditLogDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> List(
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] string? action,
        [FromQuery] string? userName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _queries.ListAsync(new AuditLogFilter(entityType, entityId, action, userName), new PageRequest(page, pageSize), cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }
}
