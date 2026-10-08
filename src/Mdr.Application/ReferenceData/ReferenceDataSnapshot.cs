namespace Mdr.Application.ReferenceData;

public sealed record ReferenceItem(int Id, string Code, string Name);

public sealed record OfficeItem(int Id, string Code, string Name, string City, string Country);

public sealed record GateSetItem(int Id, string Code, string Name, IReadOnlyList<string> Stages, string Description);

/// <summary>Allows a gate set to be used with a document type.</summary>
public sealed record GateSetRule(int DocumentTypeId, int GateSetId);

/// <summary>Immutable, case-insensitive view of the active reference data used during validation.</summary>
public sealed class ReferenceDataSnapshot
{
    private readonly Dictionary<string, ReferenceItem> _disciplines;
    private readonly Dictionary<string, ReferenceItem> _documentTypes;
    private readonly Dictionary<string, OfficeItem> _offices;
    private readonly Dictionary<string, GateSetItem> _gateSets;
    private readonly HashSet<(int DocumentTypeId, int GateSetId)> _rules;

    public ReferenceDataSnapshot(
        IEnumerable<ReferenceItem> disciplines,
        IEnumerable<ReferenceItem> documentTypes,
        IEnumerable<OfficeItem> offices,
        IEnumerable<GateSetItem> gateSets,
        IEnumerable<GateSetRule> rules)
    {
        _disciplines = disciplines.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        _documentTypes = documentTypes.ToDictionary(d => d.Code, StringComparer.OrdinalIgnoreCase);
        _offices = offices.ToDictionary(o => o.Code, StringComparer.OrdinalIgnoreCase);
        _gateSets = gateSets.ToDictionary(g => g.Code, StringComparer.OrdinalIgnoreCase);
        _rules = rules.Select(r => (r.DocumentTypeId, r.GateSetId)).ToHashSet();
    }

    public IReadOnlyCollection<ReferenceItem> Disciplines => _disciplines.Values;

    public IReadOnlyCollection<ReferenceItem> DocumentTypes => _documentTypes.Values;

    public IReadOnlyCollection<OfficeItem> Offices => _offices.Values;

    public IReadOnlyCollection<GateSetItem> GateSets => _gateSets.Values;

    public ReferenceItem? FindDiscipline(string? code) => Find(_disciplines, code);

    public ReferenceItem? FindDocumentType(string? code) => Find(_documentTypes, code);

    public OfficeItem? FindOffice(string? code) => Find(_offices, code);

    public GateSetItem? FindGateSet(string? code) => Find(_gateSets, code);

    public bool IsGateSetAllowed(int documentTypeId, int gateSetId) => _rules.Contains((documentTypeId, gateSetId));

    public IReadOnlyList<string> AllowedGateSetCodes(int documentTypeId) =>
        _gateSets.Values
            .Where(g => _rules.Contains((documentTypeId, g.Id)))
            .Select(g => g.Code)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<GateSetRule> Rules =>
        _rules.Select(r => new GateSetRule(r.DocumentTypeId, r.GateSetId)).ToList();

    private static T? Find<T>(Dictionary<string, T> source, string? code)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        return source.TryGetValue(code.Trim(), out var value) ? value : null;
    }
}
