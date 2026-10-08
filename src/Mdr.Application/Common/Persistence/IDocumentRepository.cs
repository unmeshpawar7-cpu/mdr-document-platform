using Mdr.Application.Classification;
using Mdr.Domain.Documents;

namespace Mdr.Application.Common.Persistence;

public interface IDocumentRepository
{
    /// <summary>
    /// Returns which of <paramref name="documentNumbers"/> already exist in the project.
    /// Implementations must use a single set-based query (no per-row lookups).
    /// </summary>
    Task<IReadOnlySet<string>> GetExistingDocumentNumbersAsync(
        Guid projectId,
        IReadOnlyCollection<string> documentNumbers,
        CancellationToken cancellationToken);

    /// <summary>Tracked documents by id.</summary>
    Task<IReadOnlyList<MdrDocument>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Most recent document titles of the project (excluding one upload) used as near-duplicate hints for the AI.</summary>
    Task<IReadOnlyList<ExistingDocumentHint>> GetRecentHintsAsync(
        Guid projectId,
        Guid excludeUploadId,
        int maxCount,
        CancellationToken cancellationToken);

    void AddRange(IEnumerable<MdrDocument> documents);

    /// <summary>Set-based delete used when an interrupted upload is re-processed.</summary>
    Task<int> DeleteByUploadAsync(Guid uploadId, CancellationToken cancellationToken);
}
