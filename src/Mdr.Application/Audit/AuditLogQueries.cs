using Mdr.Application.Common.Paging;

namespace Mdr.Application.Audit;

public sealed record AuditLogDto(
    long Id,
    DateTime OccurredAtUtc,
    string UserName,
    string Action,
    string EntityType,
    string EntityId,
    string? Details,
    string? CorrelationId);

public sealed record AuditLogFilter(string? EntityType, string? EntityId, string? Action, string? UserName);

public interface IAuditLogQueries
{
    Task<PagedResult<AuditLogDto>> ListAsync(AuditLogFilter filter, PageRequest page, CancellationToken cancellationToken);
}
