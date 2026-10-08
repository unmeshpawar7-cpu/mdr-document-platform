using FluentAssertions;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Processing;

public sealed class DuplicateDetectionStageTests
{
    private readonly FakeDocumentRepository _documents = new();
    private readonly Guid _projectId = Guid.NewGuid();

    [Fact]
    public async Task First_occurrence_wins_and_later_rows_are_flagged()
    {
        var stage = new DuplicateDetectionStage(_documents);
        var result = await stage.DetectAsync(_projectId, new[]
        {
            new DocumentNumberOccurrence(7, "NWP-001-PUN-MEC-DWG-0001"),
            new DocumentNumberOccurrence(2, "NWP-001-PUN-MEC-DWG-0001"),
            new DocumentNumberOccurrence(3, "NWP-001-PUN-MEC-DWG-0002"),
            new DocumentNumberOccurrence(9, "NWP-001-PUN-MEC-DWG-0001"),
        }, default);

        result.DuplicateRowNumbers.Should().BeEquivalentTo(new[] { 7, 9 });
        result.Findings.Should().HaveCount(2).And.OnlyContain(f => f.Code == ValidationCodes.DuplicateInFile);
        result.Findings.First().Message.Should().Contain("row 2");
    }

    [Fact]
    public async Task Existing_numbers_are_found_with_a_single_query()
    {
        _documents.ExistingNumbers.Add("NWP-001-PUN-MEC-DWG-0002");
        var stage = new DuplicateDetectionStage(_documents);
        var occurrences = Enumerable.Range(1, 500)
            .Select(i => new DocumentNumberOccurrence(i + 1, $"NWP-001-PUN-MEC-DWG-{i:D4}"))
            .ToList();

        var result = await stage.DetectAsync(_projectId, occurrences, default);

        _documents.ExistingNumberQueries.Should().Be(1, "duplicate lookup must be set-based, not N+1");
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.DuplicateExisting && f.RowNumber == 3);
        result.DuplicateRowNumbers.Should().BeEquivalentTo(new[] { 3 });
    }

    [Fact]
    public async Task No_occurrences_means_no_query()
    {
        var stage = new DuplicateDetectionStage(_documents);
        var result = await stage.DetectAsync(_projectId, Array.Empty<DocumentNumberOccurrence>(), default);
        result.Findings.Should().BeEmpty();
        _documents.ExistingNumberQueries.Should().Be(0);
    }
}
