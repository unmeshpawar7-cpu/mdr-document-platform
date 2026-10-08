using Mdr.Application.Common.Abstractions;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Options;
using Mdr.Application.Processing;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Common;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Application.Uploads;

public interface IMdrUploadService
{
    /// <summary>Validates and stores the file, creates a <c>Queued</c> upload and enqueues it for background processing.</summary>
    Task<UploadAcceptedDto> UploadAsync(UploadMdrFileCommand command, CancellationToken cancellationToken);
}

public sealed class MdrUploadService : IMdrUploadService
{
    private readonly IProjectRepository _projects;
    private readonly IUploadRepository _uploads;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileStorage _storage;
    private readonly IFileValidationStage _fileValidation;
    private readonly IUploadQueue _queue;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditTrail _audit;
    private readonly IOptions<MdrProcessingOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MdrUploadService> _logger;

    public MdrUploadService(
        IProjectRepository projects,
        IUploadRepository uploads,
        IUnitOfWork unitOfWork,
        IFileStorage storage,
        IFileValidationStage fileValidation,
        IUploadQueue queue,
        ICurrentUser currentUser,
        IAuditTrail audit,
        IOptions<MdrProcessingOptions> options,
        TimeProvider timeProvider,
        ILogger<MdrUploadService> logger)
    {
        _projects = projects;
        _uploads = uploads;
        _unitOfWork = unitOfWork;
        _storage = storage;
        _fileValidation = fileValidation;
        _queue = queue;
        _currentUser = currentUser;
        _audit = audit;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<UploadAcceptedDto> UploadAsync(UploadMdrFileCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var project = await _projects.GetByIdAsync(command.ProjectId, cancellationToken)
            ?? throw new NotFoundException("Project", command.ProjectId);
        if (!project.AcceptsUploads)
        {
            throw new DomainException("project_not_active", $"Project {project.Code} is {project.Status} and does not accept uploads.");
        }

        var displayName = FileNameSanitizer.Sanitize(command.FileName);

        // Cheap checks first: extension and declared size, before any byte is read.
        ThrowIfErrors(_fileValidation.ValidateMetadata(displayName, command.Length));

        StoredFile stored;
        try
        {
            stored = await _storage.SaveAsync(command.Content, MdrProcessingOptions.AllowedExtension, _options.Value.MaxFileSizeBytes, cancellationToken);
        }
        catch (FileSizeLimitExceededException ex)
        {
            throw Invalid(ValidationCodes.FileTooLarge, ex.Message);
        }

        var keepFile = false;
        try
        {
            await using (var content = await _storage.OpenReadAsync(stored.Path, cancellationToken))
            {
                ThrowIfErrors(await _fileValidation.ValidateContentAsync(content, cancellationToken));
            }

            if (await _uploads.ExistsWithChecksumAsync(project.Id, stored.Sha256, cancellationToken))
            {
                throw DuplicateFile();
            }

            var upload = MdrUpload.CreateQueued(
                project.Id,
                displayName,
                stored.Path,
                stored.SizeBytes,
                stored.Sha256,
                _currentUser.UserName,
                _currentUser.CorrelationId,
                _timeProvider.GetUtcNow().UtcDateTime);

            _uploads.Add(upload);
            _audit.Record(
                AuditActions.UploadQueued,
                nameof(MdrUpload),
                upload.Id.ToString(),
                new { ProjectCode = project.Code, upload.OriginalFileName, upload.FileSizeBytes, upload.ChecksumSha256 });

            try
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex) when (_unitOfWork.IsUniqueConstraintViolation(ex))
            {
                // Lost a race with an identical concurrent upload (unique ProjectId + ChecksumSha256 index).
                throw DuplicateFile();
            }

            keepFile = true;
            _logger.LogInformation(
                "Upload {UploadId} queued for project {ProjectCode} ({Bytes} bytes, sha256 {Checksum})",
                upload.Id,
                project.Code,
                upload.FileSizeBytes,
                upload.ChecksumSha256);

            // If the client disconnects while the bounded queue is full, the upload stays Queued and is
            // picked up by the startup recovery sweep.
            await _queue.EnqueueAsync(upload.Id, cancellationToken);

            return new UploadAcceptedDto(upload.Id, project.Id, upload.Status.ToString(), $"/api/mdr/{upload.Id}", upload.UploadedAtUtc);
        }
        finally
        {
            if (!keepFile)
            {
                await _storage.DeleteAsync(stored.Path, CancellationToken.None);
            }
        }
    }

    private static ConflictException DuplicateFile() =>
        new("upload_duplicate_file", "An identical file (same SHA-256 checksum) has already been uploaded for this project.");

    private static RequestValidationException Invalid(string code, string message) =>
        new("File validation failed.", new[] { new FieldError("file", message, code) });

    private static void ThrowIfErrors(IReadOnlyList<ValidationFinding> findings)
    {
        var errors = findings.Where(f => f.IsError).ToList();
        if (errors.Count > 0)
        {
            throw new RequestValidationException(
                "File validation failed.",
                errors.Select(e => new FieldError("file", e.Message, e.Code)).ToList());
        }
    }
}
