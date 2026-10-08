using System.Collections.Concurrent;
using System.Security.Cryptography;
using Mdr.Application.Classification;
using Mdr.Application.Common.Abstractions;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Processing;
using Mdr.Application.ReferenceData;
using Mdr.Domain.Documents;
using Mdr.Domain.Projects;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;

namespace Mdr.UnitTests.TestDoubles;

public sealed class FakeUniqueViolationException : Exception
{
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCalls { get; private set; }

    public int TransactionsBegun { get; private set; }

    public int Commits { get; private set; }

    public int DetachedEntities { get; private set; }

    public int ClearCalls { get; private set; }

    /// <summary>Optional hook: receives the 1-based SaveChanges call number and may throw.</summary>
    public Action<int>? OnSave { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveChangesCalls++;
        OnSave?.Invoke(SaveChangesCalls);
        return Task.FromResult(0);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        TransactionsBegun++;
        return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(this));
    }

    public void Detach<TEntity>(IEnumerable<TEntity> entities)
        where TEntity : class => DetachedEntities += entities.Count();

    public void ClearChangeTracker() => ClearCalls++;

    public bool IsUniqueConstraintViolation(Exception exception) => exception is FakeUniqueViolationException;

    private sealed class FakeTransaction : IUnitOfWorkTransaction
    {
        private readonly FakeUnitOfWork _owner;

        public FakeTransaction(FakeUnitOfWork owner) => _owner = owner;

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            _owner.Commits++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

public sealed class FakeDocumentRepository : IDocumentRepository
{
    public List<MdrDocument> Documents { get; } = new();

    public HashSet<string> ExistingNumbers { get; } = new(StringComparer.Ordinal);

    public List<ExistingDocumentHint> Hints { get; } = new();

    public int ExistingNumberQueries { get; private set; }

    public Task<IReadOnlySet<string>> GetExistingDocumentNumbersAsync(Guid projectId, IReadOnlyCollection<string> documentNumbers, CancellationToken cancellationToken)
    {
        ExistingNumberQueries++;
        var known = ExistingNumbers.Concat(Documents.Where(d => d.ProjectId == projectId).Select(d => d.DocumentNumber));
        IReadOnlySet<string> result = documentNumbers.Intersect(known, StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<MdrDocument>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyList<MdrDocument> result = Documents.Where(d => ids.Contains(d.Id)).ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ExistingDocumentHint>> GetRecentHintsAsync(Guid projectId, Guid excludeUploadId, int maxCount, CancellationToken cancellationToken)
    {
        IReadOnlyList<ExistingDocumentHint> result = Hints.Take(maxCount).ToList();
        return Task.FromResult(result);
    }

    public void AddRange(IEnumerable<MdrDocument> documents) => Documents.AddRange(documents);

    public Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.RemoveAll(d => d.UploadId == uploadId));
}

public sealed class FakeIssueRepository : IValidationIssueRepository
{
    public List<ValidationIssue> Issues { get; } = new();

    public void AddRange(IEnumerable<ValidationIssue> issues) => Issues.AddRange(issues);

    public Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken) =>
        Task.FromResult(Issues.RemoveAll(i => i.UploadId == uploadId));
}

public sealed class FakeUploadRepository : IUploadRepository
{
    public Dictionary<Guid, MdrUpload> Uploads { get; } = new();

    public List<Guid> Pending { get; } = new();

    public Task<MdrUpload?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Uploads.TryGetValue(id, out var upload) ? upload : null);

    public Task<bool> ExistsWithChecksumAsync(Guid projectId, string checksumSha256, CancellationToken cancellationToken) =>
        Task.FromResult(Uploads.Values.Any(u => u.ProjectId == projectId && u.ChecksumSha256 == checksumSha256));

    public Task<IReadOnlyList<Guid>> GetPendingIdsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> result = Pending.ToList();
        return Task.FromResult(result);
    }

    public void Add(MdrUpload upload) => Uploads[upload.Id] = upload;
}

public sealed class FakeProjectRepository : IProjectRepository
{
    public List<Project> Projects { get; } = new();

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Projects.Any(p => p.Code == code));

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Projects.FirstOrDefault(p => p.Id == id));

    public Task<Project?> GetWithWbsAsync(Guid id, CancellationToken cancellationToken) => GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyDictionary<string, Guid>> GetWbsLookupAsync(Guid projectId, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, Guid> lookup = Projects.Single(p => p.Id == projectId).WbsElements.ToDictionary(w => w.Code, w => w.Id);
        return Task.FromResult(lookup);
    }

    public void Add(Project project) => Projects.Add(project);
}

public sealed class FakeReferenceDataRepository : IReferenceDataRepository
{
    public Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(TestData.Snapshot);
}

public sealed class InMemoryFileStorage : IFileStorage
{
    public ConcurrentDictionary<string, byte[]> Files { get; } = new();

    public async Task<StoredFile> SaveAsync(Stream content, string extension, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maxBytes)
        {
            throw new FileSizeLimitExceededException(maxBytes);
        }

        var path = $"test/{Guid.NewGuid():N}{extension}";
        var bytes = buffer.ToArray();
        Files[path] = bytes;
        return new StoredFile(path, bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    public string Put(byte[] bytes)
    {
        var path = $"test/{Guid.NewGuid():N}.csv";
        Files[path] = bytes;
        return path;
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
    {
        if (!Files.TryGetValue(path, out var bytes))
        {
            throw new FileNotFoundException("missing", path);
        }

        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.TryRemove(path, out _);
        return Task.CompletedTask;
    }
}

public sealed class RecordingAuditTrail : IAuditTrail
{
    public List<(string Action, string EntityType, string EntityId)> Entries { get; } = new();

    public void Record(string action, string entityType, string entityId, object? details = null, string? userName = null, string? correlationId = null) =>
        Entries.Add((action, entityType, entityId));
}

public sealed class FixedCurrentUser : ICurrentUser
{
    public string UserName => "controller";

    public bool IsAuthenticated => true;

    public string? CorrelationId => "test-correlation";
}

public sealed class RecordingQueue : IUploadQueue
{
    public List<Guid> Enqueued { get; } = new();

    public int Count => Enqueued.Count;

    public ValueTask EnqueueAsync(Guid uploadId, CancellationToken cancellationToken)
    {
        Enqueued.Add(uploadId);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<Guid> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var id in Enqueued)
        {
            yield return id;
        }
    }
}

/// <summary>Classifier double: echoes the discipline/type encoded in the document number unless told otherwise.</summary>
public sealed class FakeClassifier : IDocumentClassifier
{
    public bool IsEnabled { get; set; } = true;

    public Func<ClassificationRequest, ClassificationResponse>? Responder { get; set; }

    public List<ClassificationRequest> Requests { get; } = new();

    public Task<ClassificationResponse> ClassifyAsync(ClassificationRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Responder is not null)
        {
            return Task.FromResult(Responder(request));
        }

        var suggestions = request.Items.Select(i =>
        {
            DocumentNumber.TryParse(i.DocumentNumber, out var number);
            return new ClassificationSuggestion(
                i.Id,
                number?.DisciplineCode,
                number?.DocumentTypeCode,
                "Engineering",
                "Medium",
                0.9,
                Array.Empty<string>(),
                null,
                "echo",
                "fake-1");
        }).ToList();
        return Task.FromResult(ClassificationResponse.Success(suggestions));
    }
}
