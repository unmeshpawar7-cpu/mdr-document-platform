using System.ComponentModel.DataAnnotations;

namespace Mdr.Infrastructure.Classification;

/// <summary>Settings for the Python AI classification sidecar (section <c>Classifier</c>).</summary>
public sealed class ClassifierOptions
{
    public const string SectionName = "Classifier";

    /// <summary>When false, <see cref="NullDocumentClassifier"/> is used and uploads are stored without AI suggestions.</summary>
    public bool Enabled { get; set; }

    [Required]
    [Url]
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Shared secret sent as <c>X-Api-Key</c>; supplied via environment variable, never committed.</summary>
    public string? ApiKey { get; set; }

    [Range(1, 300)]
    public int AttemptTimeoutSeconds { get; set; } = 10;

    [Range(2, 600)]
    public int TotalTimeoutSeconds { get; set; } = 30;

    [Range(1, 5)]
    public int MaxRetryAttempts { get; set; } = 2;
}
