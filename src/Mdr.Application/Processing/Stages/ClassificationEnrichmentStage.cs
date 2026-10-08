using Mdr.Application.Classification;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Application.Processing.Csv;
using Mdr.Application.ReferenceData;
using Mdr.Domain.Documents;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Application.Processing.Stages;

public sealed record EnrichmentRequest(
    Guid UploadId,
    Guid ProjectId,
    IReadOnlyList<PersistedDocument> Documents,
    ReferenceDataSnapshot ReferenceData);

public sealed record EnrichmentResult(
    bool Attempted,
    int ClassifiedCount,
    int NeedsReviewCount,
    int FailedBatches,
    IReadOnlyList<ValidationFinding> Findings)
{
    public static EnrichmentResult Skipped { get; } = new(false, 0, 0, 0, Array.Empty<ValidationFinding>());
}

/// <summary>
/// Stage 6 (optional): advisory AI classification of the inserted documents. Classifier failures, timeouts and
/// even database errors while saving suggestions are logged and turned into Info findings; this stage never fails
/// the upload (only cancellation propagates). Suggestions with unknown codes or unknown duplicate ids are discarded.
/// </summary>
public interface IClassificationEnrichmentStage
{
    Task<EnrichmentResult> EnrichAsync(EnrichmentRequest request, CancellationToken cancellationToken);
}

public sealed class ClassificationEnrichmentStage : IClassificationEnrichmentStage
{
    private readonly IDocumentClassifier _classifier;
    private readonly IDocumentRepository _documents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOptions<MdrProcessingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ClassificationEnrichmentStage> _logger;

    public ClassificationEnrichmentStage(
        IDocumentClassifier classifier,
        IDocumentRepository documents,
        IUnitOfWork unitOfWork,
        IOptions<MdrProcessingOptions> options,
        TimeProvider timeProvider,
        ILogger<ClassificationEnrichmentStage> logger)
    {
        _classifier = classifier;
        _documents = documents;
        _unitOfWork = unitOfWork;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<EnrichmentResult> EnrichAsync(EnrichmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_classifier.IsEnabled || request.Documents.Count == 0)
        {
            return EnrichmentResult.Skipped;
        }

        var options = _options.Value;
        var findings = new List<ValidationFinding>();
        var classified = 0;
        var needsReview = 0;
        var failedBatches = 0;

        IReadOnlyList<ExistingDocumentHint> hints;
        try
        {
            hints = options.AiMaxExistingHints == 0
                ? Array.Empty<ExistingDocumentHint>()
                : await _documents.GetRecentHintsAsync(request.ProjectId, request.UploadId, options.AiMaxExistingHints, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Could not load duplicate hints for upload {UploadId}; continuing without them", request.UploadId);
            hints = Array.Empty<ExistingDocumentHint>();
        }

        var knownIds = new HashSet<string>(hints.Select(h => h.Id), StringComparer.OrdinalIgnoreCase);
        foreach (var document in request.Documents)
        {
            knownIds.Add(document.Id.ToString());
        }

        var batchNumber = 0;
        foreach (var batch in request.Documents.Chunk(options.AiBatchSize))
        {
            batchNumber++;
            IReadOnlyList<MdrDocument> entities = Array.Empty<MdrDocument>();
            try
            {
                var classificationRequest = new ClassificationRequest(
                    batch.Select(d => new ClassificationItem(d.Id.ToString(), d.Title, d.Description, d.DocumentNumber)).ToList(),
                    hints);

                var response = await _classifier.ClassifyAsync(classificationRequest, cancellationToken);
                if (!response.Succeeded)
                {
                    failedBatches++;
                    findings.Add(ValidationFinding.FileLevel(
                        ValidationCodes.AiUnavailable,
                        $"AI classification unavailable for batch {batchNumber} ({batch.Length} documents): {response.FailureReason}. Documents were stored without suggestions."));
                    continue;
                }

                var suggestions = response.Suggestions
                    .GroupBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var snapshots = batch.ToDictionary(d => d.Id);

                entities = await _documents.GetByIdsAsync(batch.Select(d => d.Id).ToList(), cancellationToken);
                var now = _timeProvider.GetUtcNow().UtcDateTime;

                foreach (var entity in entities)
                {
                    if (!suggestions.TryGetValue(entity.Id.ToString(), out var suggestion) || !snapshots.TryGetValue(entity.Id, out var snapshot))
                    {
                        continue;
                    }

                    var classification = Sanitize(suggestion, snapshot, request.ReferenceData, knownIds, findings);
                    var reasons = entity.ApplyClassification(
                        classification,
                        snapshot.DisciplineCode,
                        snapshot.DocumentTypeCode,
                        options.AiLowConfidenceThreshold,
                        now);

                    classified++;
                    if (entity.NeedsReview)
                    {
                        needsReview++;
                    }

                    foreach (var reason in reasons)
                    {
                        findings.Add(DescribeReason(reason, snapshot, classification));
                    }

                    if (classification.MissingMetadata.Count > 0)
                    {
                        findings.Add(ValidationFinding.Create(
                            snapshot.RowNumber,
                            null,
                            ValidationCodes.AiMissingMetadata,
                            $"AI suggests adding: {string.Join(", ", classification.MissingMetadata)}.",
                            string.Join(",", classification.MissingMetadata)));
                    }
                }

                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                failedBatches++;
                _logger.LogWarning(ex, "AI enrichment batch {Batch} failed for upload {UploadId}", batchNumber, request.UploadId);
                findings.Add(ValidationFinding.FileLevel(
                    ValidationCodes.AiUnavailable,
                    $"AI enrichment failed for batch {batchNumber} ({batch.Length} documents): {ex.GetType().Name}. Documents were stored without suggestions."));
            }
            finally
            {
                _unitOfWork.Detach(entities);
            }
        }

        _logger.LogInformation(
            "AI enrichment for upload {UploadId}: {Classified} classified, {NeedsReview} need review, {FailedBatches} failed batches",
            request.UploadId,
            classified,
            needsReview,
            failedBatches);

        return new EnrichmentResult(true, classified, needsReview, failedBatches, findings);
    }

    /// <summary>Hallucination guard: only codes present in the reference data and ids we sent are accepted.</summary>
    private static AiClassification Sanitize(
        ClassificationSuggestion suggestion,
        PersistedDocument document,
        ReferenceDataSnapshot referenceData,
        HashSet<string> knownIds,
        List<ValidationFinding> findings)
    {
        string? Accept(string? code, Func<string?, object?> find, string label)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            if (find(code) is null)
            {
                findings.Add(ValidationFinding.Create(
                    document.RowNumber,
                    null,
                    ValidationCodes.AiSuggestionRejected,
                    $"AI suggested unknown {label} '{code}'; suggestion discarded.",
                    code));
                return null;
            }

            return code.Trim().ToUpperInvariant();
        }

        var discipline = Accept(suggestion.Discipline, c => referenceData.FindDiscipline(c), "discipline");
        var documentType = Accept(suggestion.DocumentType, c => referenceData.FindDocumentType(c), "document type");

        AiPriority? priority = null;
        if (!string.IsNullOrWhiteSpace(suggestion.Priority)
            && Enum.TryParse<AiPriority>(suggestion.Priority, ignoreCase: true, out var parsedPriority)
            && Enum.IsDefined(parsedPriority)
            && !int.TryParse(suggestion.Priority, out _))
        {
            priority = parsedPriority;
        }

        var duplicateOf = suggestion.PotentialDuplicateOf;
        if (!string.IsNullOrWhiteSpace(duplicateOf)
            && (!knownIds.Contains(duplicateOf) || string.Equals(duplicateOf, document.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            duplicateOf = null;
        }

        var confidence = double.IsFinite(suggestion.Confidence) ? Math.Clamp(suggestion.Confidence, 0d, 1d) : 0d;

        return new AiClassification(
            discipline,
            documentType,
            suggestion.Category,
            priority,
            confidence,
            suggestion.MissingMetadata ?? Array.Empty<string>(),
            string.IsNullOrWhiteSpace(duplicateOf) ? null : duplicateOf,
            suggestion.Rationale,
            string.IsNullOrWhiteSpace(suggestion.ClassifierVersion) ? "unknown" : suggestion.ClassifierVersion);
    }

    private static ValidationFinding DescribeReason(string code, PersistedDocument document, AiClassification classification)
    {
        var message = code switch
        {
            ValidationCodes.AiDisciplineMismatch =>
                $"AI suggests discipline {classification.DisciplineCode} but the row says {document.DisciplineCode}.",
            ValidationCodes.AiDocumentTypeMismatch =>
                $"AI suggests document type {classification.DocumentTypeCode} but the row says {document.DocumentTypeCode}.",
            ValidationCodes.AiLowConfidence =>
                $"AI confidence {classification.Confidence:0.00} is below the review threshold.",
            ValidationCodes.AiPotentialDuplicate =>
                $"Title is very similar to document {classification.PotentialDuplicateOf}.",
            _ => "AI flagged the document for review.",
        };

        var column = code switch
        {
            ValidationCodes.AiDisciplineMismatch => MdrColumns.Discipline,
            ValidationCodes.AiDocumentTypeMismatch => MdrColumns.DocumentType,
            ValidationCodes.AiPotentialDuplicate => MdrColumns.Title,
            _ => null,
        };

        return ValidationFinding.Create(document.RowNumber, column, code, message, document.DocumentNumber);
    }
}
