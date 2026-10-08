using Mdr.Domain.Uploads;

namespace Mdr.Application.Common.Persistence;

public interface IUploadRepository
{
    /// <summary>Tracked upload.</summary>
    Task<MdrUpload?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> ExistsWithChecksumAsync(Guid projectId, string checksumSha256, CancellationToken cancellationToken);

    /// <summary>Ids of uploads still <c>Queued</c> or <c>Processing</c>, oldest first (used for crash recovery).</summary>
    Task<IReadOnlyList<Guid>> GetPendingIdsAsync(CancellationToken cancellationToken);

    void Add(MdrUpload upload);
}
