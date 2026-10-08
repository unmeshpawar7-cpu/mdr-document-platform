using System.Text;
using FluentAssertions;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Options;
using Mdr.Application.Processing.Stages;
using Mdr.Application.Uploads;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Uploads;

public sealed class UploadServiceTests
{
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeUploadRepository _uploads = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly InMemoryFileStorage _storage = new();
    private readonly RecordingQueue _queue = new();
    private readonly RecordingAuditTrail _audit = new();
    private readonly MdrProcessingOptions _options = new() { MaxFileSizeBytes = 4096 };
    private readonly Guid _projectId;

    public UploadServiceTests()
    {
        var project = TestData.CreateProject();
        _projects.Projects.Add(project);
        _projectId = project.Id;
    }

    private MdrUploadService CreateService() => new(
        _projects,
        _uploads,
        _unitOfWork,
        _storage,
        new FileValidationStage(Options.Create(_options)),
        _queue,
        new FixedCurrentUser(),
        _audit,
        Options.Create(_options),
        new TestClock(TestData.Now),
        NullLogger<MdrUploadService>.Instance);

    private static UploadMdrFileCommand Command(Guid projectId, string content, string fileName = "mdr.csv")
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new UploadMdrFileCommand(projectId, fileName, bytes.Length, new MemoryStream(bytes));
    }

    [Fact]
    public async Task Valid_file_is_stored_queued_audited_and_enqueued()
    {
        var accepted = await CreateService().UploadAsync(Command(_projectId, "DocumentNumber,Title\nX,Y\n", "C:\\Users\\someone\\mdr.csv"), default);

        accepted.Status.Should().Be("Queued");
        accepted.StatusUrl.Should().Be($"/api/mdr/{accepted.UploadId}");
        var upload = _uploads.Uploads[accepted.UploadId];
        upload.OriginalFileName.Should().Be("mdr.csv", "client paths are stripped");
        upload.UploadedBy.Should().Be("controller");
        upload.CorrelationId.Should().Be("test-correlation");
        upload.StoragePath.Should().NotContain("mdr.csv");
        _queue.Enqueued.Should().Equal(accepted.UploadId);
        _audit.Entries.Should().ContainSingle(e => e.Action == "Upload.Queued");
        _storage.Files.Should().ContainKey(upload.StoragePath);
    }

    [Fact]
    public async Task Identical_file_for_the_same_project_is_a_conflict_and_is_not_kept()
    {
        await CreateService().UploadAsync(Command(_projectId, "DocumentNumber,Title\nX,Y\n"), default);

        var act = () => CreateService().UploadAsync(Command(_projectId, "DocumentNumber,Title\nX,Y\n"), default);

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("upload_duplicate_file");
        _storage.Files.Should().HaveCount(1);
        _queue.Enqueued.Should().HaveCount(1);
    }

    [Fact]
    public async Task Lost_race_on_unique_index_is_reported_as_conflict()
    {
        _unitOfWork.OnSave = _ => throw new FakeUniqueViolationException();
        var act = () => CreateService().UploadAsync(Command(_projectId, "DocumentNumber,Title\nX,Y\n"), default);
        await act.Should().ThrowAsync<ConflictException>();
        _storage.Files.Should().BeEmpty();
    }

    [Theory]
    [InlineData("mdr.xlsx", "DocumentNumber\n", ValidationCodes.FileExtension)]
    [InlineData("mdr.csv", "", ValidationCodes.FileEmpty)]
    public async Task Metadata_errors_are_rejected_before_anything_is_stored(string fileName, string content, string code)
    {
        var act = () => CreateService().UploadAsync(Command(_projectId, content, fileName), default);

        var error = (await act.Should().ThrowAsync<RequestValidationException>()).Which;
        error.Errors.Should().Contain(e => e.Code == code);
        _storage.Files.Should().BeEmpty();
    }

    [Fact]
    public async Task Declared_length_lies_are_caught_while_streaming()
    {
        var bytes = new byte[8192];
        var act = () => CreateService().UploadAsync(new UploadMdrFileCommand(_projectId, "mdr.csv", 10, new MemoryStream(bytes)), default);

        (await act.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Should().Contain(e => e.Code == ValidationCodes.FileTooLarge);
    }

    [Fact]
    public async Task Content_errors_remove_the_stored_file()
    {
        var bytes = new byte[] { 0x41, 0xE9, 0x0A };
        var act = () => CreateService().UploadAsync(new UploadMdrFileCommand(_projectId, "mdr.csv", bytes.Length, new MemoryStream(bytes)), default);

        (await act.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Should().Contain(e => e.Code == ValidationCodes.FileEncoding);
        _storage.Files.Should().BeEmpty();
        _uploads.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_project_is_not_found()
    {
        var act = () => CreateService().UploadAsync(Command(Guid.NewGuid(), "DocumentNumber\n"), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("../../etc/passwd.csv", "passwd.csv")]
    [InlineData("C:\\temp\\MDR rev A.csv", "MDR rev A.csv")]
    [InlineData("  ", "upload.csv")]
    [InlineData(".hidden.csv", "hidden.csv")]
    public void File_names_are_sanitised_for_display(string input, string expected) =>
        FileNameSanitizer.Sanitize(input).Should().Be(expected);
}
