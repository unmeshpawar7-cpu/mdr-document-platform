using Mdr.Domain.Projects;

namespace Mdr.Application.Common.Persistence;

public interface IProjectRepository
{
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Tracked project without its WBS elements.</summary>
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked project including all WBS elements (needed to enforce WBS invariants).</summary>
    Task<Project?> GetWithWbsAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>WBS code to element id lookup for one project (single query, no tracking).</summary>
    Task<IReadOnlyDictionary<string, Guid>> GetWbsLookupAsync(Guid projectId, CancellationToken cancellationToken);

    void Add(Project project);
}
