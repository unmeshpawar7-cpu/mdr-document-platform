namespace Mdr.Application.Uploads;

/// <summary>Command created by the API from a multipart upload.</summary>
public sealed record UploadMdrFileCommand(Guid ProjectId, string FileName, long Length, Stream Content);

public sealed record UploadAcceptedDto(Guid UploadId, Guid ProjectId, string Status, string StatusUrl, DateTime UploadedAtUtc);

public sealed record UploadCountersDto(int TotalRows, int ValidRows, int InvalidRows, int DuplicateRows, int InsertedRows);

public sealed record UploadStatusDto(
    Guid Id,
    Guid ProjectId,
    string ProjectCode,
    string OriginalFileName,
    long FileSizeBytes,
    string ChecksumSha256,
    string UploadedBy,
    DateTime UploadedAtUtc,
    string Status,
    UploadCountersDto Counters,
    int ProcessingAttempts,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    long? ProcessingDurationMs,
    string? FailureReason);

public sealed record UploadHistoryItemDto(
    Guid Id,
    string OriginalFileName,
    string UploadedBy,
    DateTime UploadedAtUtc,
    string Status,
    UploadCountersDto Counters,
    DateTime? FinishedAtUtc);

public sealed record ValidationIssueDto(
    long Id,
    int RowNumber,
    string? Column,
    string Severity,
    string Code,
    string Message,
    string? OffendingValue);

public sealed record AiSuggestionDto(
    string? Discipline,
    string? DocumentType,
    string? Category,
    string? Priority,
    double? Confidence,
    string? PotentialDuplicateOf,
    string? MissingMetadata,
    string? Rationale,
    string? ClassifierVersion);

public sealed record MdrDocumentDto(
    Guid Id,
    int SourceRowNumber,
    string DocumentNumber,
    string Title,
    string? Description,
    string Discipline,
    string DocumentType,
    string Office,
    string GateSet,
    string Wbs,
    int Quantity,
    string Revision,
    DateOnly? PlannedDate,
    string Status,
    bool NeedsReview,
    AiSuggestionDto? Ai);

public sealed record IssueFilter(string? Severity, string? Code, int? Row);

public sealed record DocumentFilter(string? Discipline, string? DocumentType, bool? NeedsReview);

public sealed record CountByKey(string Key, int Count);

/// <summary>Raw aggregates loaded from the database; turned into a <see cref="UploadSummaryDto"/> by <see cref="MdrSummaryBuilder"/>.</summary>
public sealed record UploadSummaryData(
    UploadStatusDto Upload,
    IReadOnlyList<CountByKey> DocumentsByDiscipline,
    IReadOnlyList<CountByKey> DocumentsByDocumentType,
    IReadOnlyList<CountByKey> DocumentsByGateSet,
    IReadOnlyList<CountByKey> IssuesBySeverity,
    IReadOnlyList<CountByKey> IssuesByCode,
    int DocumentCount,
    int ClassifiedCount,
    int NeedsReviewCount,
    int DisciplineMismatchCount,
    int DocumentTypeMismatchCount,
    double? AverageConfidence);

public sealed record AiSummaryDto(
    bool Attempted,
    int ClassifiedCount,
    int NotClassifiedCount,
    int NeedsReviewCount,
    double NeedsReviewRate,
    double? DisciplineAgreementRate,
    double? DocumentTypeAgreementRate,
    double? AverageConfidence,
    int LowConfidenceCount,
    int PotentialDuplicateCount,
    int UnavailableBatchCount);

public sealed record UploadSummaryDto(
    Guid UploadId,
    string Status,
    UploadCountersDto Counters,
    double ValidRowRate,
    IReadOnlyList<CountByKey> DocumentsByDiscipline,
    IReadOnlyList<CountByKey> DocumentsByDocumentType,
    IReadOnlyList<CountByKey> DocumentsByGateSet,
    IReadOnlyList<CountByKey> IssuesBySeverity,
    IReadOnlyList<CountByKey> TopIssueCodes,
    AiSummaryDto Ai,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    long? ProcessingDurationMs);

public sealed record ValidationReportFile(string FileName, string ContentType, byte[] Content);
