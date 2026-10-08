namespace Mdr.Application.Common.Abstractions;

/// <summary>
/// Records audit entries. Entries are added to the current unit of work and persisted with the
/// next <c>SaveChangesAsync</c>, so they commit atomically with the change they describe.
/// </summary>
public interface IAuditTrail
{
    void Record(string action, string entityType, string entityId, object? details = null, string? userName = null, string? correlationId = null);
}

/// <summary>Stable audit action names.</summary>
public static class AuditActions
{
    public const string ProjectCreated = "Project.Created";
    public const string WbsElementsAdded = "Project.WbsElementsAdded";
    public const string UploadQueued = "Upload.Queued";
    public const string UploadCompleted = "Upload.Completed";
    public const string UploadFailed = "Upload.Failed";
}
