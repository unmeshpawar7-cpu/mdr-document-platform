using System.ComponentModel.DataAnnotations;

namespace Mdr.Application.Options;

/// <summary>What happens to valid rows when a file also contains invalid rows.</summary>
public enum ProcessingPolicy
{
    /// <summary>Default: persist every valid row, report the invalid ones (status CompletedWithErrors).</summary>
    PersistValidRows = 0,

    /// <summary>All-or-nothing: any Error rejects the whole file (status Failed, nothing persisted).</summary>
    RejectFileOnAnyError = 1,
}

/// <summary>Settings for MDR upload validation and background processing (section <c>MdrProcessing</c>).</summary>
public sealed class MdrProcessingOptions
{
    public const string SectionName = "MdrProcessing";

    /// <summary>Only CSV files are accepted.</summary>
    public const string AllowedExtension = ".csv";

    [Range(typeof(long), "1", "104857600")]
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;

    [Range(1, 1_000_000)]
    public int MaxRows { get; set; } = 50_000;

    public ProcessingPolicy Policy { get; set; } = ProcessingPolicy.PersistValidRows;

    [Range(10, 5000)]
    public int PersistenceBatchSize { get; set; } = 500;

    [Range(1, 10_000)]
    public int QueueCapacity { get; set; } = 100;

    [Range(1, 16)]
    public int MaxParallelUploads { get; set; } = 2;

    [Range(1, 10)]
    public int MaxProcessingAttempts { get; set; } = 3;

    [Range(1, 100)]
    public int AiBatchSize { get; set; } = 50;

    [Range(0.0, 1.0)]
    public double AiLowConfidenceThreshold { get; set; } = 0.6;

    [Range(0, 5000)]
    public int AiMaxExistingHints { get; set; } = 500;
}
