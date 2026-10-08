using FluentAssertions;
using Mdr.Domain.Common;
using Mdr.Domain.Uploads;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Domain;

public sealed class MdrUploadTests
{
    private static MdrUpload NewUpload() =>
        MdrUpload.CreateQueued(Guid.NewGuid(), "mdr.csv", "2026/10/x.csv", 100, new string('a', 64), "controller", "corr", TestData.Now);

    [Fact]
    public void New_upload_is_queued_with_a_concurrency_version()
    {
        var upload = NewUpload();
        upload.Status.Should().Be(UploadStatus.Queued);
        upload.Version.Should().NotBe(Guid.Empty);
        upload.IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void Lifecycle_completes_with_errors_when_rows_were_rejected()
    {
        var upload = NewUpload();
        upload.StartProcessing(TestData.Now);
        upload.RecordValidationResults(totalRows: 10, validRows: 7, invalidRows: 3, duplicateRows: 1);
        upload.RecordInserted(7);
        upload.Complete(TestData.Now.AddSeconds(5));

        upload.Status.Should().Be(UploadStatus.CompletedWithErrors);
        upload.ProcessingDuration.Should().Be(TimeSpan.FromSeconds(5));
        upload.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public void Lifecycle_completes_when_all_rows_are_valid()
    {
        var upload = NewUpload();
        upload.StartProcessing(TestData.Now);
        upload.RecordValidationResults(5, 5, 0, 0);
        upload.RecordInserted(5);
        upload.Complete(TestData.Now);
        upload.Status.Should().Be(UploadStatus.Completed);
    }

    [Fact]
    public void Restarting_a_processing_upload_increments_attempts_and_resets_counters()
    {
        var upload = NewUpload();
        upload.StartProcessing(TestData.Now);
        upload.RecordValidationResults(5, 5, 0, 0);
        upload.StartProcessing(TestData.Now.AddMinutes(1));

        upload.ProcessingAttempts.Should().Be(2);
        upload.TotalRows.Should().Be(0);
    }

    [Fact]
    public void Inconsistent_counters_are_rejected()
    {
        var upload = NewUpload();
        upload.StartProcessing(TestData.Now);
        var act = () => upload.RecordValidationResults(10, 5, 4, 0);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("upload_counters_inconsistent");
    }

    [Fact]
    public void Terminal_upload_cannot_be_restarted_or_failed_again()
    {
        var upload = NewUpload();
        upload.Fail("bad header", TestData.Now);
        upload.Status.Should().Be(UploadStatus.Failed);
        upload.FailureReason.Should().Be("bad header");

        upload.Invoking(u => u.StartProcessing(TestData.Now)).Should().Throw<DomainException>();
        upload.Invoking(u => u.Fail("again", TestData.Now)).Should().Throw<DomainException>();
    }

    [Fact]
    public void Checksum_must_be_a_sha256_hex_string()
    {
        var act = () => MdrUpload.CreateQueued(Guid.NewGuid(), "mdr.csv", "p.csv", 10, "abc", "u", null, TestData.Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("upload_checksum_invalid");
    }
}
