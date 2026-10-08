using FluentAssertions;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Processing;

public sealed class PersistenceStageTests
{
    private readonly FakeDocumentRepository _documents = new();
    private readonly FakeIssueRepository _issues = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private PersistenceStage CreateStage(ProcessingPolicy policy, int batchSize = 10) => new(
        _documents,
        _issues,
        _unitOfWork,
        Options.Create(new MdrProcessingOptions { Policy = policy, PersistenceBatchSize = batchSize }),
        new TestClock(TestData.Now),
        NullLogger<PersistenceStage>.Instance);

    private static (MdrUpload Upload, List<ResolvedMdrRow> Rows) Arrange(int validRows, int invalidRows)
    {
        var project = TestData.CreateProject();
        var context = TestData.Context(project);
        var stage = new BusinessValidationStage();
        var rows = Enumerable.Range(1, validRows)
            .Select(i => stage.Validate(TestData.ValidRow(i + 1) with { DocumentNumber = $"NWP-001-PUN-MEC-DWG-{i:D4}" }, context).Resolved!)
            .ToList();

        var upload = MdrUpload.CreateQueued(project.Id, "mdr.csv", "p.csv", 10, new string('b', 64), "u", null, TestData.Now);
        upload.StartProcessing(TestData.Now);
        upload.RecordValidationResults(validRows + invalidRows, validRows, invalidRows, 0);
        return (upload, rows);
    }

    private static List<ValidationFinding> Findings(int count) =>
        Enumerable.Range(1, count).Select(i => ValidationFinding.Create(i + 1, "Title", ValidationCodes.RequiredMissing, "Title is required.")).ToList();

    [Fact]
    public async Task Persists_valid_rows_in_batches_inside_one_transaction()
    {
        var (upload, rows) = Arrange(validRows: 25, invalidRows: 3);
        var result = await CreateStage(ProcessingPolicy.PersistValidRows).PersistAsync(new PersistenceRequest(upload, rows, Findings(3), true), default);

        result.InsertedCount.Should().Be(25);
        result.RejectedByPolicy.Should().BeFalse();
        result.Documents.Select(d => d.RowNumber).Should().BeEquivalentTo(rows.Select(r => r.RowNumber));
        _documents.Documents.Should().HaveCount(25);
        _issues.Issues.Should().HaveCount(3).And.OnlyContain(i => i.UploadId == upload.Id);
        upload.InsertedRows.Should().Be(25);

        _unitOfWork.TransactionsBegun.Should().Be(1);
        _unitOfWork.Commits.Should().Be(1);
        // 3 document batches (10 + 10 + 5) + 1 issue batch + 1 final counter save.
        _unitOfWork.SaveChangesCalls.Should().Be(5);
        _unitOfWork.DetachedEntities.Should().Be(28);
    }

    [Fact]
    public async Task Reject_policy_persists_no_documents_when_any_row_failed()
    {
        var (upload, rows) = Arrange(validRows: 5, invalidRows: 1);
        var result = await CreateStage(ProcessingPolicy.RejectFileOnAnyError).PersistAsync(new PersistenceRequest(upload, rows, Findings(1), true), default);

        result.RejectedByPolicy.Should().BeTrue();
        result.InsertedCount.Should().Be(0);
        _documents.Documents.Should().BeEmpty();
        _issues.Issues.Should().HaveCount(1, "findings are still stored so the report explains the rejection");
        _unitOfWork.Commits.Should().Be(1);
    }

    [Fact]
    public async Task Reject_policy_persists_everything_when_the_file_is_clean()
    {
        var (upload, rows) = Arrange(validRows: 4, invalidRows: 0);
        var result = await CreateStage(ProcessingPolicy.RejectFileOnAnyError).PersistAsync(new PersistenceRequest(upload, rows, Array.Empty<ValidationFinding>(), false), default);
        result.InsertedCount.Should().Be(4);
    }

    [Fact]
    public async Task Failure_mid_batch_does_not_commit()
    {
        var (upload, rows) = Arrange(validRows: 25, invalidRows: 0);
        _unitOfWork.OnSave = call =>
        {
            if (call == 2)
            {
                throw new InvalidOperationException("db down");
            }
        };

        var act = () => CreateStage(ProcessingPolicy.PersistValidRows).PersistAsync(new PersistenceRequest(upload, rows, Array.Empty<ValidationFinding>(), false), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _unitOfWork.Commits.Should().Be(0);
    }
}
