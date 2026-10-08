using Mdr.Application.Classification;
using Mdr.Application.Common.Persistence;
using Mdr.Application.ReferenceData;
using Mdr.Domain.Documents;
using Mdr.Domain.Projects;
using Mdr.Domain.Uploads;
using Mdr.Domain.Validation;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Infrastructure.Persistence.Repositories;

public sealed class ProjectRepository : IProjectRepository
{
    private readonly MdrDbContext _db;

    public ProjectRepository(MdrDbContext db) => _db = db;

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        _db.Projects.AnyAsync(p => p.Code == code, cancellationToken);

    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Projects.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Project?> GetWithWbsAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Projects.Include(p => p.WbsElements).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<string, Guid>> GetWbsLookupAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var rows = await _db.WbsElements.AsNoTracking()
            .Where(w => w.ProjectId == projectId)
            .Select(w => new { w.Code, w.Id })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(r => r.Code, r => r.Id, StringComparer.Ordinal);
    }

    public void Add(Project project) => _db.Projects.Add(project);
}

public sealed class UploadRepository : IUploadRepository
{
    private readonly MdrDbContext _db;

    public UploadRepository(MdrDbContext db) => _db = db;

    public Task<MdrUpload?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Uploads.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> ExistsWithChecksumAsync(Guid projectId, string checksumSha256, CancellationToken cancellationToken)
    {
        var checksum = checksumSha256.ToLowerInvariant();
        return _db.Uploads.AnyAsync(u => u.ProjectId == projectId && u.ChecksumSha256 == checksum, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetPendingIdsAsync(CancellationToken cancellationToken) =>
        await _db.Uploads.AsNoTracking()
            .Where(u => u.Status == UploadStatus.Queued || u.Status == UploadStatus.Processing)
            .OrderBy(u => u.UploadedAtUtc)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

    public void Add(MdrUpload upload) => _db.Uploads.Add(upload);
}

public sealed class DocumentRepository : IDocumentRepository
{
    private readonly MdrDbContext _db;

    public DocumentRepository(MdrDbContext db) => _db = db;

    public async Task<IReadOnlySet<string>> GetExistingDocumentNumbersAsync(
        Guid projectId,
        IReadOnlyCollection<string> documentNumbers,
        CancellationToken cancellationToken)
    {
        if (documentNumbers.Count == 0)
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        // One round trip: EF Core 8 sends the list as a single JSON parameter
        // (OPENJSON on SQL Server, json_each on SQLite) - no per-row lookups.
        var numbers = documentNumbers.ToList();
        var existing = await _db.Documents.AsNoTracking()
            .Where(d => d.ProjectId == projectId && numbers.Contains(d.DocumentNumber))
            .Select(d => d.DocumentNumber)
            .ToListAsync(cancellationToken);
        return existing.ToHashSet(StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<MdrDocument>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var list = ids.ToList();
        return await _db.Documents.Where(d => list.Contains(d.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExistingDocumentHint>> GetRecentHintsAsync(
        Guid projectId,
        Guid excludeUploadId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        var rows = await _db.Documents.AsNoTracking()
            .Where(d => d.ProjectId == projectId && d.UploadId != excludeUploadId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(maxCount)
            .Select(d => new { d.Id, d.Title, d.DocumentNumber })
            .ToListAsync(cancellationToken);
        return rows.Select(r => new ExistingDocumentHint(r.Id.ToString(), r.Title, r.DocumentNumber)).ToList();
    }

    public void AddRange(IEnumerable<MdrDocument> documents) => _db.Documents.AddRange(documents);

    public Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken) =>
        _db.Documents.Where(d => d.UploadId == uploadId).ExecuteDeleteAsync(cancellationToken);
}

public sealed class ValidationIssueRepository : IValidationIssueRepository
{
    private readonly MdrDbContext _db;

    public ValidationIssueRepository(MdrDbContext db) => _db = db;

    public void AddRange(IEnumerable<ValidationIssue> issues) => _db.ValidationIssues.AddRange(issues);

    public Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken) =>
        _db.ValidationIssues.Where(i => i.UploadId == uploadId).ExecuteDeleteAsync(cancellationToken);
}

public sealed class ReferenceDataRepository : IReferenceDataRepository
{
    private readonly MdrDbContext _db;

    public ReferenceDataRepository(MdrDbContext db) => _db = db;

    public async Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var disciplines = await _db.Disciplines.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new ReferenceItem(x.Id, x.Code, x.Name)).ToListAsync(cancellationToken);
        var documentTypes = await _db.DocumentTypes.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new ReferenceItem(x.Id, x.Code, x.Name)).ToListAsync(cancellationToken);
        var offices = await _db.Offices.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new OfficeItem(x.Id, x.Code, x.Name, x.City, x.Country)).ToListAsync(cancellationToken);
        var gateSetRows = await _db.GateSets.AsNoTracking().Where(x => x.IsActive)
            .Select(x => new { x.Id, x.Code, x.Name, x.Stages, x.Description }).ToListAsync(cancellationToken);
        var rules = await _db.DocumentTypeGateSets.AsNoTracking()
            .Select(x => new GateSetRule(x.DocumentTypeId, x.GateSetId)).ToListAsync(cancellationToken);

        var gateSets = gateSetRows.Select(g => new GateSetItem(
            g.Id,
            g.Code,
            g.Name,
            g.Stages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            g.Description));

        return new ReferenceDataSnapshot(disciplines, documentTypes, offices, gateSets, rules);
    }
}
