using Mdr.Application.Audit;
using Mdr.Application.Common.Paging;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Infrastructure.Persistence.Queries;

public sealed class AuditLogQueries : IAuditLogQueries
{
    private readonly MdrDbContext _db;

    public AuditLogQueries(MdrDbContext db) => _db = db;

    public async Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var query = _db.AuditLogs.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.EntityType))
        {
            var entityType = filter.EntityType.Trim();
            query = query.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(filter.EntityId))
        {
            var entityId = filter.EntityId.Trim();
            query = query.Where(a => a.EntityId == entityId);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            var action = filter.Action.Trim();
            query = query.Where(a => a.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(filter.UserName))
        {
            var userName = filter.UserName.Trim();
            query = query.Where(a => a.UserName == userName);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.Id)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(a => new AuditLogDto(a.Id, a.OccurredAtUtc, a.UserName, a.Action, a.EntityType, a.EntityId, a.Details, a.CorrelationId))
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditLogDto>(items, page.Page, page.PageSize, total);
    }
}
