using System.Text.RegularExpressions;
using Mdr.Domain.Common;
using Mdr.Domain.Projects;
using Mdr.Domain.ReferenceData;
using Mdr.Domain.Validation;

namespace Mdr.Domain.Documents;

/// <summary>
/// A planned deliverable in a project's Master Deliverables Register. Document numbers are unique per project.
/// AI fields are advisory: they never change the provided discipline / type, they only raise <see cref="NeedsReview"/>.
/// </summary>
public sealed class MdrDocument
{
    public const int MaxTitleLength = 250;
    public const int MaxDescriptionLength = 2000;
    public const int MaxRevisionLength = 5;
    public const int MaxCategoryLength = 100;
    public const int MaxClassifierVersionLength = 100;
    public const int MaxRationaleLength = 1000;
    public const int MaxMissingMetadataLength = 500;
    public const int MaxDuplicateReferenceLength = 64;

    private static readonly Regex RevisionPattern = new(
        "^[A-Z0-9]{1,3}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private MdrDocument()
    {
    }

    public Guid Id { get; private set; }

    public Guid ProjectId { get; private set; }

    public Guid UploadId { get; private set; }

    /// <summary>CSV row number (header = row 1) the document was imported from.</summary>
    public int SourceRowNumber { get; private set; }

    public string DocumentNumber { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public int DisciplineId { get; private set; }

    public int DocumentTypeId { get; private set; }

    public int OfficeId { get; private set; }

    public int GateSetId { get; private set; }

    public Guid WbsElementId { get; private set; }

    public int Quantity { get; private set; }

    public string Revision { get; private set; } = string.Empty;

    public DateOnly? PlannedDate { get; private set; }

    public DocumentStatus Status { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Discipline? Discipline { get; private set; }

    public DocumentType? DocumentType { get; private set; }

    public Office? Office { get; private set; }

    public GateSet? GateSet { get; private set; }

    public WbsElement? WbsElement { get; private set; }

    public string? AiSuggestedDisciplineCode { get; private set; }

    public string? AiSuggestedDocumentTypeCode { get; private set; }

    public string? AiCategory { get; private set; }

    public AiPriority? AiPriority { get; private set; }

    public double? AiConfidence { get; private set; }

    public string? AiClassifierVersion { get; private set; }

    public string? AiPotentialDuplicateOf { get; private set; }

    public string? AiMissingMetadata { get; private set; }

    public string? AiRationale { get; private set; }

    public bool AiDisciplineMismatch { get; private set; }

    public bool AiDocumentTypeMismatch { get; private set; }

    public DateTime? AiClassifiedAtUtc { get; private set; }

    /// <summary>True when the AI disagrees with the provided values, is not confident, or suspects a duplicate.</summary>
    public bool NeedsReview { get; private set; }

    public static bool IsValidRevision(string? revision) =>
        !string.IsNullOrWhiteSpace(revision) && RevisionPattern.IsMatch(revision);

    public static MdrDocument Create(
        Guid projectId,
        Guid uploadId,
        int sourceRowNumber,
        string documentNumber,
        string title,
        string? description,
        int disciplineId,
        int documentTypeId,
        int officeId,
        int gateSetId,
        Guid wbsElementId,
        int quantity,
        string revision,
        DateOnly? plannedDate,
        DocumentStatus status,
        DateTime createdAtUtc)
    {
        if (quantity <= 0)
        {
            throw new DomainException("document_quantity_invalid", "Quantity must be greater than zero.");
        }

        var normalizedRevision = (revision ?? string.Empty).Trim().ToUpperInvariant();
        if (!IsValidRevision(normalizedRevision))
        {
            throw new DomainException("document_revision_invalid", "Revision must be 1-3 upper-case letters or digits.");
        }

        var cleanDescription = Text.NullIfBlank(description);
        if (cleanDescription is not null && cleanDescription.Length > MaxDescriptionLength)
        {
            throw new DomainException("document_description_too_long", $"Description must be at most {MaxDescriptionLength} characters.");
        }

        return new MdrDocument
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            UploadId = uploadId,
            SourceRowNumber = sourceRowNumber,
            DocumentNumber = Text.Required(
                Documents.DocumentNumber.Normalize(documentNumber),
                "document_number",
                Documents.DocumentNumber.MaxLength),
            Title = Text.Required(title, "document_title", MaxTitleLength),
            Description = cleanDescription,
            DisciplineId = disciplineId,
            DocumentTypeId = documentTypeId,
            OfficeId = officeId,
            GateSetId = gateSetId,
            WbsElementId = wbsElementId,
            Quantity = quantity,
            Revision = normalizedRevision,
            PlannedDate = plannedDate,
            Status = status,
            CreatedAtUtc = createdAtUtc,
        };
    }

    /// <summary>
    /// Applies an advisory AI classification and returns the validation codes describing why the
    /// document needs human review (empty when the AI agrees with confidence).
    /// </summary>
    public IReadOnlyList<string> ApplyClassification(
        AiClassification classification,
        string providedDisciplineCode,
        string providedDocumentTypeCode,
        double lowConfidenceThreshold,
        DateTime classifiedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(classification);

        var confidence = double.IsNaN(classification.Confidence) ? 0d : Math.Clamp(classification.Confidence, 0d, 1d);

        AiSuggestedDisciplineCode = Text.Truncate(Text.NullIfBlank(classification.DisciplineCode), ReferenceDataEntity.MaxCodeLength);
        AiSuggestedDocumentTypeCode = Text.Truncate(Text.NullIfBlank(classification.DocumentTypeCode), ReferenceDataEntity.MaxCodeLength);
        AiCategory = Text.Truncate(Text.NullIfBlank(classification.Category), MaxCategoryLength);
        AiPriority = classification.Priority;
        AiConfidence = Math.Round(confidence, 4);
        AiClassifierVersion = Text.Truncate(classification.ClassifierVersion, MaxClassifierVersionLength);
        AiPotentialDuplicateOf = Text.Truncate(Text.NullIfBlank(classification.PotentialDuplicateOf), MaxDuplicateReferenceLength);
        AiRationale = Text.Truncate(Text.NullIfBlank(classification.Rationale), MaxRationaleLength);
        AiMissingMetadata = classification.MissingMetadata.Count == 0
            ? null
            : Text.Truncate(string.Join(",", classification.MissingMetadata), MaxMissingMetadataLength);
        AiClassifiedAtUtc = classifiedAtUtc;

        AiDisciplineMismatch = AiSuggestedDisciplineCode is not null
            && !string.Equals(AiSuggestedDisciplineCode, providedDisciplineCode, StringComparison.OrdinalIgnoreCase);
        AiDocumentTypeMismatch = AiSuggestedDocumentTypeCode is not null
            && !string.Equals(AiSuggestedDocumentTypeCode, providedDocumentTypeCode, StringComparison.OrdinalIgnoreCase);

        var reasons = new List<string>();
        if (AiDisciplineMismatch)
        {
            reasons.Add(ValidationCodes.AiDisciplineMismatch);
        }

        if (AiDocumentTypeMismatch)
        {
            reasons.Add(ValidationCodes.AiDocumentTypeMismatch);
        }

        if (confidence < lowConfidenceThreshold)
        {
            reasons.Add(ValidationCodes.AiLowConfidence);
        }

        if (AiPotentialDuplicateOf is not null)
        {
            reasons.Add(ValidationCodes.AiPotentialDuplicate);
        }

        NeedsReview = reasons.Count > 0;
        return reasons;
    }
}
