using Mdr.Application.Common.Paging;

namespace Mdr.Application.Projects;

/// <summary>Read side for projects (AsNoTracking projections, implemented in Infrastructure).</summary>
public interface IProjectQueries
{
    Task<bool> ExistsAsync(Guid projectId, CancellationToken cancellationToken);

    Task<PagedResult<ProjectDto>> ListAsync(ProjectListFilter filter, PageRequest page, CancellationToken cancellationToken);

    Task<ProjectDetailsDto?> GetAsync(Guid projectId, CancellationToken cancellationToken);
}
