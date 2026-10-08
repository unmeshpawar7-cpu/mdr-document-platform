namespace Mdr.Application.Classification;

/// <summary>One document to classify.</summary>
public sealed record ClassificationItem(string Id, string Title, string? Description, string? DocumentNumber);

/// <summary>An already-registered document used as a near-duplicate reference.</summary>
public sealed record ExistingDocumentHint(string Id, string Title, string? DocumentNumber);

public sealed record ClassificationRequest(IReadOnlyList<ClassificationItem> Items, IReadOnlyList<ExistingDocumentHint> ExistingDocuments);

/// <summary>Raw suggestion as returned by the classifier; codes are validated by the caller.</summary>
public sealed record ClassificationSuggestion(
    string Id,
    string? Discipline,
    string? DocumentType,
    string? Category,
    string? Priority,
    double Confidence,
    IReadOnlyList<string> MissingMetadata,
    string? PotentialDuplicateOf,
    string? Rationale,
    string ClassifierVersion);

/// <summary>Batch outcome. A classifier never throws for transport failures; it returns <see cref="Unavailable"/>.</summary>
public sealed record ClassificationResponse(bool Succeeded, IReadOnlyList<ClassificationSuggestion> Suggestions, string? FailureReason)
{
    public static ClassificationResponse Success(IReadOnlyList<ClassificationSuggestion> suggestions) => new(true, suggestions, null);

    public static ClassificationResponse Unavailable(string reason) =>
        new(false, Array.Empty<ClassificationSuggestion>(), reason);
}

/// <summary>Advisory AI classification of MDR documents (Python sidecar over HTTP, or a null implementation).</summary>
public interface IDocumentClassifier
{
    bool IsEnabled { get; }

    Task<ClassificationResponse> ClassifyAsync(ClassificationRequest request, CancellationToken cancellationToken);
}
