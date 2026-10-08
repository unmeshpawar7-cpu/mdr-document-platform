using Mdr.Domain.Common;

namespace Mdr.Domain.Auditing;

/// <summary>Append-only record of a state-changing action (who, what, when, correlation id).</summary>
public sealed class AuditLog
{
    public const int MaxUserNameLength = 100;
    public const int MaxActionLength = 100;
    public const int MaxEntityTypeLength = 100;
    public const int MaxEntityIdLength = 100;
    public const int MaxDetailsLength = 4000;
    public const int MaxCorrelationIdLength = 64;

    private AuditLog()
    {
    }

    public long Id { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string? Details { get; private set; }

    public string? CorrelationId { get; private set; }

    public static AuditLog Create(
        string userName,
        string action,
        string entityType,
        string entityId,
        string? details,
        string? correlationId,
        DateTime occurredAtUtc)
    {
        return new AuditLog
        {
            OccurredAtUtc = occurredAtUtc,
            UserName = Text.Truncate(string.IsNullOrWhiteSpace(userName) ? "system" : userName, MaxUserNameLength)!,
            Action = Text.Required(action, "audit_action", MaxActionLength),
            EntityType = Text.Required(entityType, "audit_entity_type", MaxEntityTypeLength),
            EntityId = Text.Required(entityId, "audit_entity_id", MaxEntityIdLength),
            Details = Text.Truncate(details, MaxDetailsLength),
            CorrelationId = Text.Truncate(correlationId, MaxCorrelationIdLength),
        };
    }
}
