namespace Mdr.Domain.Documents;

/// <summary>
/// An advisory classification produced by the AI sidecar for one document. Codes have already been
/// checked against reference data by the application layer; unknown codes arrive here as <c>null</c>.
/// </summary>
public sealed record AiClassification(
    string? DisciplineCode,
    string? DocumentTypeCode,
    string? Category,
    AiPriority? Priority,
    double Confidence,
    IReadOnlyList<string> MissingMetadata,
    string? PotentialDuplicateOf,
    string? Rationale,
    string ClassifierVersion);
