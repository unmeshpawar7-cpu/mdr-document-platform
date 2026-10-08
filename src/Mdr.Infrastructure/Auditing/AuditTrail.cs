using System.Text.Json;
using Mdr.Application.Common.Abstractions;
using Mdr.Domain.Auditing;
using Mdr.Infrastructure.Persistence;

namespace Mdr.Infrastructure.Auditing;

/// <summary>Adds audit rows to the current DbContext so they commit with the change they describe.</summary>
public sealed class AuditTrail : IAuditTrail
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly MdrDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuditTrail(MdrDbContext db, ICurrentUser currentUser, TimeProvider timeProvider)
    {
        _db = db;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public void Record(string action, string entityType, string entityId, object? details = null, string? userName = null, string? correlationId = null)
    {
        var serialized = details is null ? null : JsonSerializer.Serialize(details, details.GetType(), Json);
        _db.AuditLogs.Add(AuditLog.Create(
            userName ?? _currentUser.UserName,
            action,
            entityType,
            entityId,
            serialized,
            correlationId ?? _currentUser.CorrelationId,
            _timeProvider.GetUtcNow().UtcDateTime));
    }
}

/// <summary>Fallback identity for code running outside an HTTP request (background jobs).</summary>
public sealed class SystemCurrentUser : ICurrentUser
{
    public string UserName => "system";

    public bool IsAuthenticated => false;

    public string? CorrelationId => null;
}
