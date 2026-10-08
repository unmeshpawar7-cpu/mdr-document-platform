using Mdr.Application.ReferenceData;

namespace Mdr.Application.Common.Persistence;

public interface IReferenceDataRepository
{
    /// <summary>Loads all active reference data into an immutable snapshot.</summary>
    Task<ReferenceDataSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
