namespace Mdr.Domain.Uploads;

/// <summary>Processing state of an MDR upload batch.</summary>
public enum UploadStatus
{
    /// <summary>File stored and waiting for the background processor.</summary>
    Queued = 0,

    /// <summary>Background processor is validating / persisting the file.</summary>
    Processing = 1,

    /// <summary>All rows were valid and persisted.</summary>
    Completed = 2,

    /// <summary>Processing finished; some rows were rejected and valid rows were persisted.</summary>
    CompletedWithErrors = 3,

    /// <summary>The file could not be processed or was rejected by policy; nothing was persisted.</summary>
    Failed = 4,
}
