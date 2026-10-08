using Mdr.Domain.Common;

namespace Mdr.Domain.Uploads;

/// <summary>
/// One MDR CSV file submitted for a project. Tracks the processing lifecycle
/// (<c>Queued → Processing → Completed | CompletedWithErrors | Failed</c>) and row counters.
/// </summary>
public sealed class MdrUpload
{
    public const int MaxOriginalFileNameLength = 260;
    public const int MaxStoragePathLength = 500;
    public const int ChecksumLength = 64;
    public const int MaxUploadedByLength = 100;
    public const int MaxFailureReasonLength = 2000;
    public const int MaxCorrelationIdLength = 64;

    private MdrUpload()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    /// <summary>Client-supplied file name, sanitised and kept for display only (never used as a path).</summary>
    public string OriginalFileName { get; private set; } = string.Empty;

    /// <summary>Storage-relative path of the generated file name the content was saved under.</summary>
    public string StoragePath { get; private set; } = string.Empty;

    public long FileSizeBytes { get; private set; }

    /// <summary>Lower-case hex SHA-256 of the file content; unique per project.</summary>
    public string ChecksumSha256 { get; private set; } = string.Empty;

    public string UploadedBy { get; private set; } = string.Empty;

    public DateTime UploadedAtUtc { get; private set; }

    public UploadStatus Status { get; private set; }

    public int TotalRows { get; private set; }

    public int ValidRows { get; private set; }

    public int InvalidRows { get; private set; }

    public int DuplicateRows { get; private set; }

    public int InsertedRows { get; private set; }

    public int ProcessingAttempts { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Correlation id of the HTTP request that created the upload; flows into background logs.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>Optimistic concurrency token, regenerated on every state change.</summary>
    public Guid Version { get; private set; }

    public bool IsTerminal =>
        Status is UploadStatus.Completed or UploadStatus.CompletedWithErrors or UploadStatus.Failed;

    public TimeSpan? ProcessingDuration =>
        StartedAtUtc.HasValue && FinishedAtUtc.HasValue ? FinishedAtUtc.Value - StartedAtUtc.Value : null;

    public static MdrUpload CreateQueued(
        Guid projectId,
        string originalFileName,
        string storagePath,
        long fileSizeBytes,
        string checksumSha256,
        string uploadedBy,
        string? correlationId,
        DateTime nowUtc)
    {
        if (projectId == Guid.Empty)
        {
            throw new DomainException("upload_project_required", "An upload must belong to a project.");
        }

        if (fileSizeBytes <= 0)
        {
            throw new DomainException("upload_empty", "An upload must contain data.");
        }

        if (string.IsNullOrWhiteSpace(checksumSha256) || checksumSha256.Length != ChecksumLength)
        {
            throw new DomainException("upload_checksum_invalid", "Checksum must be a 64 character SHA-256 hex string.");
        }

        return new MdrUpload
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            OriginalFileName = Text.Required(originalFileName, "file_name", MaxOriginalFileNameLength),
            StoragePath = Text.Required(storagePath, "storage_path", MaxStoragePathLength),
            FileSizeBytes = fileSizeBytes,
            ChecksumSha256 = checksumSha256.ToLowerInvariant(),
            UploadedBy = Text.Required(uploadedBy, "uploaded_by", MaxUploadedByLength),
            UploadedAtUtc = nowUtc,
            Status = UploadStatus.Queued,
            CorrelationId = Text.Truncate(correlationId, MaxCorrelationIdLength),
            Version = Guid.NewGuid(),
        };
    }

    /// <summary>
    /// Moves the upload into <see cref="UploadStatus.Processing"/>. Allowed from <c>Queued</c> and
    /// from <c>Processing</c> (crash recovery re-runs an interrupted job). Counters are reset.
    /// </summary>
    public void StartProcessing(DateTime nowUtc)
    {
        if (Status is not (UploadStatus.Queued or UploadStatus.Processing))
        {
            throw new DomainException("upload_invalid_transition", $"Cannot start processing an upload in status {Status}.");
        }

        Status = UploadStatus.Processing;
        ProcessingAttempts++;
        StartedAtUtc = nowUtc;
        FinishedAtUtc = null;
        FailureReason = null;
        TotalRows = 0;
        ValidRows = 0;
        InvalidRows = 0;
        DuplicateRows = 0;
        InsertedRows = 0;
        Version = Guid.NewGuid();
    }

    /// <summary>Records the outcome of validation. <paramref name="duplicateRows"/> is a subset of the invalid rows.</summary>
    public void RecordValidationResults(int totalRows, int validRows, int invalidRows, int duplicateRows)
    {
        EnsureProcessing();
        if (totalRows < 0 || validRows < 0 || invalidRows < 0 || duplicateRows < 0)
        {
            throw new DomainException("upload_counters_negative", "Row counters cannot be negative.");
        }

        if (validRows + invalidRows != totalRows)
        {
            throw new DomainException("upload_counters_inconsistent", "Valid and invalid rows must add up to the total.");
        }

        if (duplicateRows > invalidRows)
        {
            throw new DomainException("upload_counters_inconsistent", "Duplicate rows are a subset of invalid rows.");
        }

        TotalRows = totalRows;
        ValidRows = validRows;
        InvalidRows = invalidRows;
        DuplicateRows = duplicateRows;
        Version = Guid.NewGuid();
    }

    public void RecordInserted(int insertedRows)
    {
        EnsureProcessing();
        if (insertedRows < 0 || insertedRows > ValidRows)
        {
            throw new DomainException("upload_counters_inconsistent", "Inserted rows must be between zero and the number of valid rows.");
        }

        InsertedRows = insertedRows;
        Version = Guid.NewGuid();
    }

    /// <summary>Finishes processing: <c>CompletedWithErrors</c> when any row was rejected, otherwise <c>Completed</c>.</summary>
    public void Complete(DateTime nowUtc)
    {
        EnsureProcessing();
        Status = InvalidRows > 0 ? UploadStatus.CompletedWithErrors : UploadStatus.Completed;
        FinishedAtUtc = nowUtc;
        Version = Guid.NewGuid();
    }

    /// <summary>Marks the upload as failed. Nothing from a failed upload is kept in the register.</summary>
    public void Fail(string reason, DateTime nowUtc)
    {
        if (IsTerminal)
        {
            throw new DomainException("upload_invalid_transition", $"Upload is already in terminal status {Status}.");
        }

        Status = UploadStatus.Failed;
        InsertedRows = 0;
        FailureReason = Text.Truncate(string.IsNullOrWhiteSpace(reason) ? "Unknown failure." : reason.Trim(), MaxFailureReasonLength);
        StartedAtUtc ??= nowUtc;
        FinishedAtUtc = nowUtc;
        Version = Guid.NewGuid();
    }

    private void EnsureProcessing()
    {
        if (Status != UploadStatus.Processing)
        {
            throw new DomainException("upload_invalid_transition", $"Upload must be Processing but is {Status}.");
        }
    }
}
