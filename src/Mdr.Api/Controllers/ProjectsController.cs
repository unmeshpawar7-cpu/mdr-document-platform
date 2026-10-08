using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application.Common.Paging;
using Mdr.Application.Projects;
using Mdr.Application.Uploads;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Mdr.Api.Controllers;

/// <summary>Projects, their WBS and upload history.</summary>
[ApiController]
[Route("api/projects")]
[Produces("application/json")]
[Authorize(Policy = Policies.CanRead)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status403Forbidden)]
public sealed class ProjectsController : ControllerBase
{
    private readonly IProjectService _projects;
    private readonly IMdrUploadQueryService _uploads;

    public ProjectsController(IProjectService projects, IMdrUploadQueryService uploads)
    {
        _projects = projects;
        _uploads = uploads;
    }

    /// <summary>Registers a new project.</summary>
    /// <response code="201">Project created; Location points to the project.</response>
    /// <response code="400">Invalid code, name or client.</response>
    /// <response code="409">A project with the same code exists.</response>
    [HttpPost]
    [Authorize(Policy = Policies.CanManage)]
    [ProducesResponseType(typeof(ApiResponse<ProjectDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ApiResponse<ProjectDto>>> Create([FromBody] CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = await _projects.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = project.Id }, ApiResponse.Ok(project));
    }

    /// <summary>Lists projects (paged, optional search over code/name/client and status filter).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ProjectDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiResponse<PagedResult<ProjectDto>>>> List(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _projects.ListAsync(new ProjectListFilter(search, status), new PageRequest(page, pageSize), cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }

    /// <summary>Gets a project with its WBS elements and document/upload counts.</summary>
    /// <response code="404">Project not found.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ProjectDetailsDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<ProjectDetailsDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        Ok(ApiResponse.Ok(await _projects.GetAsync(id, cancellationToken)));

    /// <summary>Bulk-adds WBS elements. Parents may be included in the same request.</summary>
    /// <response code="201">Elements created.</response>
    /// <response code="400">Invalid codes or names.</response>
    /// <response code="409">One or more codes already exist.</response>
    /// <response code="422">A parent element is missing.</response>
    [HttpPost("{id:guid}/wbs")]
    [Authorize(Policy = Policies.CanManage)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<WbsElementDto>>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WbsElementDto>>>> AddWbs(
        Guid id,
        [FromBody] AddWbsElementsRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _projects.AddWbsElementsAsync(id, request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id }, ApiResponse.Ok(created));
    }

    /// <summary>Upload history of a project, newest first.</summary>
    [HttpGet("{id:guid}/uploads")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UploadHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<PagedResult<UploadHistoryItemDto>>>> Uploads(
        Guid id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await _uploads.GetProjectUploadsAsync(id, new PageRequest(page, pageSize), cancellationToken);
        return Ok(ApiResponse.Ok(result));
    }
}
