using FluentValidation;
using Mdr.Application.Common.Abstractions;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Common.Persistence;
using Mdr.Domain.Projects;

namespace Mdr.Application.Projects;

public interface IProjectService
{
    Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken);

    Task<PagedResult<ProjectDto>> ListAsync(ProjectListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<ProjectDetailsDto> GetAsync(Guid projectId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WbsElementDto>> AddWbsElementsAsync(Guid projectId, AddWbsElementsRequest request, CancellationToken cancellationToken);
}

public sealed class ProjectService : IProjectService
{
    private readonly IProjectRepository _projects;
    private readonly IProjectQueries _queries;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IValidator<CreateProjectRequest> _createValidator;
    private readonly IValidator<AddWbsElementsRequest> _wbsValidator;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditTrail _audit;
    private readonly TimeProvider _timeProvider;

    public ProjectService(
        IProjectRepository projects,
        IProjectQueries queries,
        IUnitOfWork unitOfWork,
        IValidator<CreateProjectRequest> createValidator,
        IValidator<AddWbsElementsRequest> wbsValidator,
        ICurrentUser currentUser,
        IAuditTrail audit,
        TimeProvider timeProvider)
    {
        _projects = projects;
        _queries = queries;
        _unitOfWork = unitOfWork;
        _createValidator = createValidator;
        _wbsValidator = wbsValidator;
        _currentUser = currentUser;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken);

        var code = request.Code.Trim().ToUpperInvariant();
        if (await _projects.CodeExistsAsync(code, cancellationToken))
        {
            throw new ConflictException("project_code_exists", $"A project with code {code} already exists.");
        }

        var project = Project.Create(code, request.Name, request.Client, _currentUser.UserName, _timeProvider.GetUtcNow().UtcDateTime);
        _projects.Add(project);
        _audit.Record(AuditActions.ProjectCreated, nameof(Project), project.Id.ToString(), new { project.Code, project.Name, project.Client });

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (_unitOfWork.IsUniqueConstraintViolation(ex))
        {
            throw new ConflictException("project_code_exists", $"A project with code {code} already exists.");
        }

        return ToDto(project);
    }

    public Task<PagedResult<ProjectDto>> ListAsync(ProjectListFilter filter, PageRequest page, CancellationToken cancellationToken) =>
        _queries.ListAsync(filter, page, cancellationToken);

    public async Task<ProjectDetailsDto> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
        await _queries.GetAsync(projectId, cancellationToken) ?? throw new NotFoundException("Project", projectId);

    public async Task<IReadOnlyList<WbsElementDto>> AddWbsElementsAsync(Guid projectId, AddWbsElementsRequest request, CancellationToken cancellationToken)
    {
        await _wbsValidator.ValidateAndThrowAsync(request, cancellationToken);

        var project = await _projects.GetWithWbsAsync(projectId, cancellationToken)
            ?? throw new NotFoundException("Project", projectId);

        var existingCodes = project.WbsElements.Select(w => w.Code).ToHashSet(StringComparer.Ordinal);
        var conflicts = request.Elements.Select(e => e.Code.Trim()).Where(existingCodes.Contains).ToList();
        if (conflicts.Count > 0)
        {
            throw new ConflictException("wbs_code_exists", $"WBS code(s) already exist in project {project.Code}: {string.Join(", ", conflicts)}.");
        }

        // Apply parents before children so a whole branch can be created in one request.
        var created = request.Elements
            .OrderBy(e => WbsCode.GetLevel(e.Code.Trim()))
            .ThenBy(e => e.Code.Trim(), StringComparer.Ordinal)
            .Select(e => project.AddWbsElement(e.Code, e.Name))
            .ToList();

        _audit.Record(
            AuditActions.WbsElementsAdded,
            nameof(Project),
            project.Id.ToString(),
            new { project.Code, Count = created.Count, Codes = created.Select(c => c.Code).ToList() });
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return created.Select(w => new WbsElementDto(w.Id, w.Code, w.Name, w.Level, w.ParentCode)).ToList();
    }

    private static ProjectDto ToDto(Project project) =>
        new(project.Id, project.Code, project.Name, project.Client, project.Status.ToString(), project.CreatedAtUtc, project.CreatedBy);
}
