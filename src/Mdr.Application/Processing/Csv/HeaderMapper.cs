using Mdr.Domain.Validation;

namespace Mdr.Application.Processing.Csv;

/// <summary>Result of mapping a CSV header row onto the canonical MDR columns.</summary>
public sealed class HeaderMapping
{
    public HeaderMapping(IReadOnlyDictionary<string, int> columnIndexes, IReadOnlyList<ValidationFinding> findings, int headerFieldCount)
    {
        ColumnIndexes = columnIndexes;
        Findings = findings;
        HeaderFieldCount = headerFieldCount;
    }

    /// <summary>Canonical column name to zero-based field index.</summary>
    public IReadOnlyDictionary<string, int> ColumnIndexes { get; }

    public IReadOnlyList<ValidationFinding> Findings { get; }

    public int HeaderFieldCount { get; }

    public bool IsUsable => !Findings.Any(f => f.IsError);

    public bool HasColumn(string name) => ColumnIndexes.ContainsKey(name);
}

public interface IHeaderMapper
{
    HeaderMapping Map(IReadOnlyList<string> headers);
}

/// <summary>Maps header cells to canonical columns using normalised names and aliases.</summary>
public sealed class HeaderMapper : IHeaderMapper
{
    private static readonly Dictionary<string, string> Lookup = BuildLookup();

    public HeaderMapping Map(IReadOnlyList<string> headers)
    {
        var findings = new List<ValidationFinding>();
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < headers.Count; i++)
        {
            var raw = headers[i];
            var normalized = MdrColumns.NormalizeHeader(raw);
            if (normalized.Length == 0)
            {
                findings.Add(ValidationFinding.FileLevel(
                    ValidationCodes.UnknownColumn,
                    $"Header cell {i + 1} is blank and will be ignored.",
                    raw));
                continue;
            }

            if (!Lookup.TryGetValue(normalized, out var canonical))
            {
                findings.Add(ValidationFinding.FileLevel(
                    ValidationCodes.UnknownColumn,
                    $"Column '{raw.Trim()}' is not recognised and will be ignored.",
                    raw.Trim()));
                continue;
            }

            if (indexes.ContainsKey(canonical))
            {
                findings.Add(ValidationFinding.FileLevel(
                    ValidationCodes.DuplicateColumn,
                    $"Column '{raw.Trim()}' maps to '{canonical}', which is already mapped from column {indexes[canonical] + 1}.",
                    raw.Trim()));
                continue;
            }

            indexes[canonical] = i;
        }

        if (indexes.Count == 0)
        {
            // Nothing recognisable: most likely the header row is missing and the first row is data.
            return new HeaderMapping(
                indexes,
                new[]
                {
                    ValidationFinding.FileLevel(
                        ValidationCodes.HeaderMissing,
                        "No recognised MDR column was found in the first row; the header row appears to be missing."),
                },
                headers.Count);
        }

        foreach (var column in MdrColumns.All.Where(c => c.Required && !indexes.ContainsKey(c.Name)))
        {
            findings.Add(ValidationFinding.FileLevel(
                ValidationCodes.RequiredColumnMissing,
                $"Required column '{column.Name}' is missing from the header.",
                column.Name));
        }

        return new HeaderMapping(indexes, findings, headers.Count);
    }

    private static Dictionary<string, string> BuildLookup()
    {
        var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var column in MdrColumns.All)
        {
            lookup[MdrColumns.NormalizeHeader(column.Name)] = column.Name;
            foreach (var alias in column.Aliases)
            {
                lookup[MdrColumns.NormalizeHeader(alias)] = column.Name;
            }
        }

        return lookup;
    }
}
