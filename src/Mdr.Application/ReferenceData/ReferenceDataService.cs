using Mdr.Application.Common.Persistence;

namespace Mdr.Application.ReferenceData;

public sealed record GateSetDto(string Code, string Name, IReadOnlyList<string> Stages, string Description, IReadOnlyList<string> AllowedDocumentTypes);

public sealed record ReferenceDataDto(
    IReadOnlyList<ReferenceItem> Disciplines,
    IReadOnlyList<ReferenceItem> DocumentTypes,
    IReadOnlyList<OfficeItem> Offices,
    IReadOnlyList<GateSetDto> GateSets);

public interface IReferenceDataService
{
    Task<ReferenceDataDto> GetAsync(CancellationToken cancellationToken);
}

public sealed class ReferenceDataService : IReferenceDataService
{
    private readonly IReferenceDataRepository _repository;

    public ReferenceDataService(IReferenceDataRepository repository)
    {
        _repository = repository;
    }

    public async Task<ReferenceDataDto> GetAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _repository.GetSnapshotAsync(cancellationToken);
        var typeCodesById = snapshot.DocumentTypes.ToDictionary(t => t.Id, t => t.Code);

        var gateSets = snapshot.GateSets
            .OrderBy(g => g.Code, StringComparer.Ordinal)
            .Select(g => new GateSetDto(
                g.Code,
                g.Name,
                g.Stages,
                g.Description,
                snapshot.Rules
                    .Where(r => r.GateSetId == g.Id && typeCodesById.ContainsKey(r.DocumentTypeId))
                    .Select(r => typeCodesById[r.DocumentTypeId])
                    .OrderBy(c => c, StringComparer.Ordinal)
                    .ToList()))
            .ToList();

        return new ReferenceDataDto(
            snapshot.Disciplines.OrderBy(d => d.Code, StringComparer.Ordinal).ToList(),
            snapshot.DocumentTypes.OrderBy(d => d.Code, StringComparer.Ordinal).ToList(),
            snapshot.Offices.OrderBy(o => o.Code, StringComparer.Ordinal).ToList(),
            gateSets);
    }
}
