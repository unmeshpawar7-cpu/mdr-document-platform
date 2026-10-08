using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application.ReferenceData;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mdr.Api.Controllers;

/// <summary>Controlled vocabulary used to validate MDR rows.</summary>
[ApiController]
[Route("api/reference-data")]
[Produces("application/json")]
[Authorize(Policy = Policies.CanRead)]
public sealed class ReferenceDataController : ControllerBase
{
    private readonly IReferenceDataService _service;

    public ReferenceDataController(IReferenceDataService service) => _service = service;

    /// <summary>Active disciplines, document types, offices and gate sets (with allowed document types).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ReferenceDataDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<ReferenceDataDto>>> Get(CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await _service.GetAsync(cancellationToken)));
}
