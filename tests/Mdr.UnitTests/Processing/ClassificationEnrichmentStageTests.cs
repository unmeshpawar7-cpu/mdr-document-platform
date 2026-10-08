using FluentAssertions;
using Mdr.Application.Classification;
using Mdr.Application.Options;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Documents;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Processing;

public sealed class ClassificationEnrichmentStageTests
{
    private readonly FakeDocumentRepository _documents = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeClassifier _classifier = new();
    private readonly Guid _projectId = Guid.NewGuid();
    private readonly Guid _uploadId = Guid.NewGuid();

    private ClassificationEnrichmentStage CreateStage(int batchSize = 50) => new(
        _classifier,
        _documents,
        _unitOfWork,
        Options.Create(new MdrProcessingOptions { AiBatchSize = batchSize, AiLowConfidenceThreshold = 0.6 }),
        new TestClock(TestData.Now),
        NullLogger<ClassificationEnrichmentStage>.Instance);

    private List<PersistedDocument> Seed(int count)
    {
        var result = new List<PersistedDocument>();
        for (var i = 1; i <= count; i++)
        {
            var number = $"NWP-001-PUN-MEC-DWG-{i:D4}";
            var document = MdrDocument.Create(_projectId, _uploadId, i + 1, number, $"Pump GA {i}", "desc", 6, 3, 3, 1, Guid.NewGuid(), 1, "A", null, DocumentStatus.Planned, TestData.Now);
            _documents.Documents.Add(document);
            result.Add(new PersistedDocument(document.Id, i + 1, number, document.Title, document.Description, "MEC", "DWG"));
        }

        return result;
    }

    private EnrichmentRequest Request(IReadOnlyList<PersistedDocument> documents) => new(_uploadId, _projectId, documents, TestData.Snapshot);

    [Fact]
    public async Task Agreeing_classifier_sets_ai_fields_without_review()
    {
        var documents = Seed(3);
        var result = await CreateStage().EnrichAsync(Request(documents), default);

        result.ClassifiedCount.Should().Be(3);
        result.NeedsReviewCount.Should().Be(0);
        result.Findings.Should().BeEmpty();
        _documents.Documents.Should().OnlyContain(d => d.AiClassifierVersion == "fake-1" && !d.NeedsReview);
        _unitOfWork.SaveChangesCalls.Should().Be(1);
    }

    [Fact]
    public async Task Batches_requests_by_configured_size()
    {
        await CreateStage(batchSize: 2).EnrichAsync(Request(Seed(5)), default);
        _classifier.Requests.Select(r => r.Items.Count).Should().Equal(2, 2, 1);
    }

    [Fact]
    public async Task Disagreement_and_low_confidence_flag_review_with_info_findings()
    {
        var documents = Seed(1);
        _classifier.Responder = request => ClassificationResponse.Success(new[]
        {
            new ClassificationSuggestion(request.Items[0].Id, "ELE", "DWG", "Electrical", "High", 0.4, new[] { "description" }, null, "cable keywords", "taxonomy-v1"),
        });

        var result = await CreateStage().EnrichAsync(Request(documents), default);

        result.NeedsReviewCount.Should().Be(1);
        result.Findings.Select(f => f.Code).Should().BeEquivalentTo(new[]
        {
            ValidationCodes.AiDisciplineMismatch,
            ValidationCodes.AiLowConfidence,
            ValidationCodes.AiMissingMetadata,
        });
        result.Findings.Should().OnlyContain(f => f.Severity == IssueSeverity.Info && f.RowNumber == 2);
        _documents.Documents[0].AiPriority.Should().Be(AiPriority.High);
    }

    [Fact]
    public async Task Unknown_codes_and_unknown_duplicate_ids_are_discarded()
    {
        var documents = Seed(1);
        _classifier.Responder = request => ClassificationResponse.Success(new[]
        {
            new ClassificationSuggestion(request.Items[0].Id, "PLUMBING", "DWG", null, "Urgent", 0.95, Array.Empty<string>(), "not-a-known-id", null, "llm-v1"),
        });

        var result = await CreateStage().EnrichAsync(Request(documents), default);

        var document = _documents.Documents[0];
        document.AiSuggestedDisciplineCode.Should().BeNull();
        document.AiPriority.Should().BeNull();
        document.AiPotentialDuplicateOf.Should().BeNull();
        document.NeedsReview.Should().BeFalse();
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.AiSuggestionRejected);
    }

    [Fact]
    public async Task Duplicate_hint_referencing_an_existing_document_is_kept()
    {
        var existingId = Guid.NewGuid().ToString();
        _documents.Hints.Add(new ExistingDocumentHint(existingId, "Raw water intake pump GA", "NWP-001-PUN-MEC-DWG-0900"));
        var documents = Seed(1);
        _classifier.Responder = request => ClassificationResponse.Success(new[]
        {
            new ClassificationSuggestion(request.Items[0].Id, "MEC", "DWG", null, "Low", 0.9, Array.Empty<string>(), existingId, null, "v1"),
        });

        var result = await CreateStage().EnrichAsync(Request(documents), default);

        _documents.Documents[0].AiPotentialDuplicateOf.Should().Be(existingId);
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.AiPotentialDuplicate);
        _classifier.Requests[0].ExistingDocuments.Should().ContainSingle();
    }

    [Fact]
    public async Task Unavailable_classifier_degrades_to_an_info_finding()
    {
        _classifier.Responder = _ => ClassificationResponse.Unavailable("timeout");
        var result = await CreateStage().EnrichAsync(Request(Seed(2)), default);

        result.FailedBatches.Should().Be(1);
        result.ClassifiedCount.Should().Be(0);
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.AiUnavailable && f.Severity == IssueSeverity.Info);
    }

    [Fact]
    public async Task Exceptions_never_escape_the_stage()
    {
        _classifier.Responder = _ => throw new HttpRequestException("connection refused");
        var result = await CreateStage().EnrichAsync(Request(Seed(2)), default);
        result.FailedBatches.Should().Be(1);
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.AiUnavailable);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        _classifier.Responder = _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var act = () => CreateStage().EnrichAsync(Request(Seed(1)), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Disabled_classifier_is_skipped()
    {
        _classifier.IsEnabled = false;
        var result = await CreateStage().EnrichAsync(Request(Seed(2)), default);
        result.Attempted.Should().BeFalse();
        _classifier.Requests.Should().BeEmpty();
    }
}
