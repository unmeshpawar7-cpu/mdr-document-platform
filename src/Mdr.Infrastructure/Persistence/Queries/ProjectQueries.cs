using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Projects;
using Mdr.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Infrastructure.Persistence.Queries;

public sealed class ProjectQueries : IProjectQueries
{
    private readonly MdrDbContext _db;

    public ProjectQueries(MdrDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid projectId, CancellationToken cancellationToken) =>
        _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId, cancellationToken);

    public async Task<PagedResult<ProjectDto>> ListAsync(ProjectListFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.Projects.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(p => p.Code.Contains(search) || p.Name.Contains(search) || p.Client.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.Status))
        {
            if (!Enum.TryParse<ProjectStatus>(filter.Status.Trim(), ignoreCase: true, out var status) || !Enum.IsDefined(status))
            {
                throw new RequestValidationException(
                    "Invalid filter.",
                    new[] { new FieldError("status", "Status must be Active, OnHold or Closed.") });
            }

            query = query.Where(p => p.Status == status);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(p => p.Code)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(p => new { p.Id, p.Code, p.Name, p.Client, p.Status, p.CreatedAtUtc, p.CreatedBy })
            .ToListAsync(cancellationToken);

        var items = rows.Select(p => new ProjectDto(p.Id, p.Code, p.Name, p.Client, p.Status.ToString(), p.CreatedAtUtc, p.CreatedBy)).ToList();
        return new PagedResult<ProjectDto>(items, page.Page, page.PageSize, total);
    }

    public async Task<ProjectDetailsDto?> GetAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var project = await _db.Projects.AsNoTracking()
            .Where(p => p.Id == projectId)
            .Select(p => new { p.Id, p.Code, p.Name, p.Client, p.Status, p.CreatedAtUtc, p.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return null;
        }

        var wbs = await _db.WbsElements.AsNoTracking()
            .Where(w => w.ProjectId == projectId)
            .OrderBy(w => w.Level)
            .ThenBy(w => w.Code)
            .Select(w => new WbsElementDto(w.Id, w.Code, w.Name, w.Level, w.ParentCode))
            .ToListAsync(cancellationToken);
        var documentCount = await _db.Documents.AsNoTracking().CountAsync(d => d.ProjectId == projectId, cancellationToken);
        var uploadCount = await _db.Uploads.AsNoTracking().CountAsync(u => u.ProjectId == projectId, cancellationToken);

        return new ProjectDetailsDto(
            project.Id,
            project.Code,
            project.Name,
            project.Client,
            project.Status.ToString(),
            project.CreatedAtUtc,
            project.CreatedBy,
            documentCount,
            uploadCount,
            wbs);
    }
}
