using System.Text;
using FluentAssertions;
using Mdr.Application.Common.Abstractions;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Projects;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Mdr.Infrastructure.Csv;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Processing;

/// <summary>Runs the whole pipeline (real stages and CsvHelper reader) against in-memory fakes.</summary>
public sealed class MdrUploadProcessorTests
{
    private readonly FakeUploadRepository _uploads = new();
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeDocumentRepository _documents = new();
    private readonly FakeIssueRepository _issues = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly FakeClassifier _classifier = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly MdrProcessingOptions _options = new();
    private readonly Project _project = TestData.CreateProject();

    public MdrUploadProcessorTests() => _projects.Projects.Add(_project);

    private MdrUploadProcessor CreateProcessor()
    {
        var options = Options.Create(_options);
        var clock = new TestClock(TestData.Now);
        return new MdrUploadProcessor(
            _uploads,
            _projects,
            _documents,
            _issues,
            new FakeReferenceDataRepository(),
            _unitOfWork,
            _storage,
            new CsvHelperMdrReader(),
            new FileValidationStage(options),
            new HeaderMapper(),
            new SchemaValidationStage(),
            new BusinessValidationStage(),
            new DuplicateDetectionStage(_documents),
            new PersistenceStage(_documents, _issues, _unitOfWork, options, clock, NullLogger<PersistenceStage>.Instance),
            new ClassificationEnrichmentStage(_classifier, _documents, _unitOfWork, options, clock, NullLogger<ClassificationEnrichmentStage>.Instance),
            _audit,
            options,
            clock,
            NullLogger<MdrUploadProcessor>.Instance);
    }

    private MdrUpload Queue(byte[] content, string fileName = "mdr.csv")
    {
        var path = _storage.Put(content);
        var upload = MdrUpload.CreateQueued(_project.Id, fileName, path, Math.Max(1, content.Length), new string('c', 64), "controller", "corr-1", TestData.Now);
        _uploads.Add(upload);
        return upload;
    }

    private MdrUpload QueueSample(string sample) => Queue(File.ReadAllBytes(TestData.SamplePath(sample)));

    [Fact]
    public async Task Valid_sample_completes_and_persists_every_row()
    {
        var upload = QueueSample("mdr-valid.csv");

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Completed);
        upload.Status.Should().Be(UploadStatus.Completed);
        upload.TotalRows.Should().Be(40);
        upload.InsertedRows.Should().Be(40);
        _documents.Documents.Should().HaveCount(40).And.OnlyContain(d => d.AiClassifiedAtUtc != null && !d.NeedsReview);
        _issues.Issues.Should().NotContain(i => i.Severity == IssueSeverity.Error);
        _audit.Entries.Should().ContainSingle(e => e.Action == AuditActions.UploadCompleted);
    }

    [Fact]
    public async Task Error_sample_reports_every_rule_and_persists_only_valid_rows()
    {
        var first = QueueSample("mdr-valid.csv");
        await CreateProcessor().ProcessAsync(first.Id, default);
        var upload = Queue(File.ReadAllBytes(TestData.SamplePath("mdr-with-errors.csv")));

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.CompletedWithErrors);
        upload.TotalRows.Should().Be(27);
        upload.ValidRows.Should().Be(6);
        upload.InvalidRows.Should().Be(21);
        upload.DuplicateRows.Should().Be(2);
        upload.InsertedRows.Should().Be(6);

        var codes = _issues.Issues.Where(i => i.UploadId == upload.Id).Select(i => i.Code).ToHashSet();
        codes.Should().Contain(new[]
        {
            ValidationCodes.RequiredMissing, ValidationCodes.MaxLengthExceeded, ValidationCodes.InvalidInteger,
            ValidationCodes.InvalidDate, ValidationCodes.MalformedRow, ValidationCodes.DocumentNumberFormat,
            ValidationCodes.DocumentNumberProjectMismatch, ValidationCodes.DocumentNumberOfficeMismatch,
            ValidationCodes.DocumentNumberDisciplineMismatch, ValidationCodes.DocumentNumberTypeMismatch,
            ValidationCodes.DuplicateInFile, ValidationCodes.DuplicateExisting, ValidationCodes.UnknownDiscipline,
            ValidationCodes.UnknownDocumentType, ValidationCodes.UnknownOffice, ValidationCodes.UnknownGateSet,
            ValidationCodes.GateSetNotAllowed, ValidationCodes.UnknownWbs, ValidationCodes.QuantityNotPositive,
            ValidationCodes.InvalidRevision, ValidationCodes.PlannedDateInPast, ValidationCodes.InvalidStatus,
        });
        _issues.Issues.Should().Contain(i => i.Code == ValidationCodes.DuplicateInFile && i.RowNumber == 14);
        _issues.Issues.Should().Contain(i => i.Code == ValidationCodes.DuplicateExisting && i.RowNumber == 15);
    }

    [Fact]
    public async Task Reject_policy_fails_the_upload_and_keeps_the_findings()
    {
        _options.Policy = ProcessingPolicy.RejectFileOnAnyError;
        var upload = QueueSample("mdr-with-errors.csv");

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Failed);
        upload.Status.Should().Be(UploadStatus.Failed);
        upload.FailureReason.Should().Contain("RejectFileOnAnyError");
        _documents.Documents.Should().BeEmpty();
        _issues.Issues.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Missing_required_column_fails_with_file_level_findings()
    {
        var upload = Queue(Encoding.UTF8.GetBytes("DocumentNumber,Title\nNWP-001-PUN-MEC-DWG-0001,Pump GA\n"));

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Failed);
        _issues.Issues.Should().OnlyContain(i => i.RowNumber == 0);
        _issues.Issues.Should().Contain(i => i.Code == ValidationCodes.RequiredColumnMissing);
    }

    [Fact]
    public async Task Header_only_file_fails_with_no_data_rows()
    {
        var upload = Queue(Encoding.UTF8.GetBytes(string.Join(",", TestData.Header) + "\n"));
        await CreateProcessor().ProcessAsync(upload.Id, default);
        _issues.Issues.Should().ContainSingle(i => i.Code == ValidationCodes.NoDataRows);
    }

    [Fact]
    public async Task Too_many_rows_fails_the_upload()
    {
        _options.MaxRows = 5;
        var upload = QueueSample("mdr-valid.csv");
        await CreateProcessor().ProcessAsync(upload.Id, default);
        upload.Status.Should().Be(UploadStatus.Failed);
        _issues.Issues.Should().Contain(i => i.Code == ValidationCodes.TooManyRows);
        _documents.Documents.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_stored_file_fails_gracefully()
    {
        var upload = MdrUpload.CreateQueued(_project.Id, "mdr.csv", "test/missing.csv", 10, new string('d', 64), "u", null, TestData.Now);
        _uploads.Add(upload);

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Failed);
        _issues.Issues.Should().ContainSingle(i => i.Code == ValidationCodes.FileUnreadable);
    }

    [Fact]
    public async Task Unavailable_classifier_still_completes_the_upload()
    {
        _classifier.Responder = _ => Mdr.Application.Classification.ClassificationResponse.Unavailable("connection refused");
        var upload = QueueSample("mdr-valid.csv");

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Completed);
        _issues.Issues.Should().Contain(i => i.Code == ValidationCodes.AiUnavailable && i.Severity == IssueSeverity.Info);
        _documents.Documents.Should().OnlyContain(d => d.AiClassifiedAtUtc == null);
    }

    [Fact]
    public async Task Recovered_processing_upload_removes_partial_results_before_rerunning()
    {
        var upload = QueueSample("mdr-valid.csv");
        upload.StartProcessing(TestData.Now); // simulate a crash mid-processing
        _issues.Issues.Add(ValidationIssue.Create(upload.Id, 2, null, IssueSeverity.Info, ValidationCodes.AiUnavailable, "stale", null, TestData.Now));

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Completed);
        upload.ProcessingAttempts.Should().Be(2);
        _issues.Issues.Should().NotContain(i => i.Message == "stale");
        _documents.Documents.Should().HaveCount(40);
    }

    [Fact]
    public async Task Upload_exceeding_max_attempts_is_abandoned()
    {
        _options.MaxProcessingAttempts = 1;
        var upload = QueueSample("mdr-valid.csv");
        upload.StartProcessing(TestData.Now);

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Failed);
        upload.FailureReason.Should().Contain("abandoned");
    }

    [Fact]
    public async Task Terminal_and_unknown_uploads_are_skipped()
    {
        var upload = QueueSample("mdr-valid.csv");
        upload.Fail("x", TestData.Now);

        (await CreateProcessor().ProcessAsync(upload.Id, default)).Should().Be(ProcessingOutcome.AlreadyProcessed);
        (await CreateProcessor().ProcessAsync(Guid.NewGuid(), default)).Should().Be(ProcessingOutcome.NotFound);
    }

    [Fact]
    public async Task Unexpected_errors_mark_the_upload_failed()
    {
        var upload = QueueSample("mdr-valid.csv");
        _unitOfWork.OnSave = call =>
        {
            if (call == 2)
            {
                throw new InvalidOperationException("deadlock");
            }
        };

        var outcome = await CreateProcessor().ProcessAsync(upload.Id, default);

        outcome.Should().Be(ProcessingOutcome.Failed);
        upload.Status.Should().Be(UploadStatus.Failed);
        upload.FailureReason.Should().Contain("InvalidOperationException");
        _unitOfWork.ClearCalls.Should().Be(1);
    }
}
