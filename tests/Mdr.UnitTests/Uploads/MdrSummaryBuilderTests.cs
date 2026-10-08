using FluentAssertions;
using Mdr.Application.Uploads;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Uploads;

public sealed class MdrSummaryBuilderTests
{
    private static UploadStatusDto Upload(int total = 10, int valid = 8, int invalid = 2) => new(
        Guid.NewGuid(), Guid.NewGuid(), "NWP-001", "mdr.csv", 100, new string('a', 64), "controller", TestData.Now,
        "CompletedWithErrors", new UploadCountersDto(total, valid, invalid, 1, valid), 1,
        TestData.Now, TestData.Now.AddSeconds(3), 3000, null);

    private static UploadSummaryData Data(
        UploadStatusDto? upload = null,
        IReadOnlyList<CountByKey>? byCode = null,
        int documents = 8,
        int classified = 8,
        int needsReview = 2,
        int disciplineMismatch = 1,
        int typeMismatch = 0,
        double? averageConfidence = 0.81234) => new(
            upload ?? Upload(),
            new[] { new CountByKey("MEC", 3), new CountByKey("ELE", 5), new CountByKey("CIV", 0) },
            new[] { new CountByKey("DWG", 4), new CountByKey("SPE", 4) },
            new[] { new CountByKey("STD", 8) },
            new[] { new CountByKey("Error", 3), new CountByKey("Info", 4) },
            byCode ?? new[]
            {
                new CountByKey(ValidationCodes.RequiredMissing, 2),
                new CountByKey(ValidationCodes.UnknownWbs, 1),
                new CountByKey(ValidationCodes.AiLowConfidence, 1),
                new CountByKey(ValidationCodes.AiDisciplineMismatch, 1),
                new CountByKey(ValidationCodes.AiPotentialDuplicate, 2),
            },
            documents,
            classified,
            needsReview,
            disciplineMismatch,
            typeMismatch,
            averageConfidence);

    private readonly MdrSummaryBuilder _builder = new();

    [Fact]
    public void Orders_counts_descending_and_drops_zero_buckets()
    {
        var summary = _builder.Build(Data());
        summary.DocumentsByDiscipline.Select(c => c.Key).Should().Equal("ELE", "MEC");
        summary.DocumentsByDocumentType.Select(c => c.Key).Should().Equal(new[] { "DWG", "SPE" }, "ties are ordered by key");
    }

    [Fact]
    public void Always_reports_all_three_severities()
    {
        var summary = _builder.Build(Data());
        summary.IssuesBySeverity.Should().Equal(new CountByKey("Error", 3), new CountByKey("Warning", 0), new CountByKey("Info", 4));
    }

    [Fact]
    public void Computes_rates_ai_statistics_and_duration()
    {
        var summary = _builder.Build(Data());

        summary.ValidRowRate.Should().Be(0.8);
        summary.ProcessingDurationMs.Should().Be(3000);
        summary.Ai.Attempted.Should().BeTrue();
        summary.Ai.NeedsReviewRate.Should().Be(0.25);
        summary.Ai.DisciplineAgreementRate.Should().Be(0.875);
        summary.Ai.DocumentTypeAgreementRate.Should().Be(1.0);
        summary.Ai.AverageConfidence.Should().Be(0.8123);
        summary.Ai.LowConfidenceCount.Should().Be(1);
        summary.Ai.PotentialDuplicateCount.Should().Be(2);
        summary.TopIssueCodes.First().Key.Should().BeOneOf(ValidationCodes.RequiredMissing, ValidationCodes.AiPotentialDuplicate);
    }

    [Fact]
    public void Handles_uploads_without_documents_or_ai()
    {
        var summary = _builder.Build(Data(Upload(total: 0, valid: 0, invalid: 0), Array.Empty<CountByKey>(), documents: 0, classified: 0, needsReview: 0, disciplineMismatch: 0, averageConfidence: null));

        summary.ValidRowRate.Should().Be(0);
        summary.Ai.Attempted.Should().BeFalse();
        summary.Ai.DisciplineAgreementRate.Should().BeNull();
        summary.Ai.AverageConfidence.Should().BeNull();
    }

    [Fact]
    public void Classifier_outage_is_visible_in_the_summary()
    {
        var summary = _builder.Build(Data(byCode: new[] { new CountByKey(ValidationCodes.AiUnavailable, 2) }, classified: 0, needsReview: 0, disciplineMismatch: 0, averageConfidence: null));
        summary.Ai.Attempted.Should().BeTrue();
        summary.Ai.UnavailableBatchCount.Should().Be(2);
        summary.Ai.NotClassifiedCount.Should().Be(8);
    }
}
