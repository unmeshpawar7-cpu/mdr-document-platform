using System.Text;

namespace Mdr.Application.Processing.Csv;

/// <summary>A canonical MDR column with the header aliases accepted for it.</summary>
public sealed record MdrColumn(string Name, bool Required, int MaxLength, IReadOnlyList<string> Aliases);

/// <summary>The MDR CSV schema. Header matching is case-, whitespace- and punctuation-insensitive.</summary>
public static class MdrColumns
{
    public const string DocumentNumber = "DocumentNumber";
    public const string Title = "Title";
    public const string Description = "Description";
    public const string Discipline = "Discipline";
    public const string DocumentType = "DocumentType";
    public const string Wbs = "WBS";
    public const string GateSet = "GateSet";
    public const string Office = "Office";
    public const string Quantity = "Quantity";
    public const string Revision = "Revision";
    public const string PlannedDate = "PlannedDate";
    public const string Status = "Status";

    public static IReadOnlyList<MdrColumn> All { get; } = new List<MdrColumn>
    {
        new(DocumentNumber, true, 50, new[] { "Document Number", "Document No", "Doc No", "Doc Number", "DocNo" }),
        new(Title, true, 250, new[] { "Document Title" }),
        new(Description, false, 2000, new[] { "Desc", "Document Description" }),
        new(Discipline, true, 10, new[] { "Discipline Code" }),
        new(DocumentType, true, 10, new[] { "Document Type", "Doc Type", "Type", "Document Type Code" }),
        new(Wbs, true, 30, new[] { "WBS Code", "WBS Element" }),
        new(GateSet, true, 10, new[] { "Gate Set", "Gates" }),
        new(Office, true, 10, new[] { "Office Code", "Originating Office" }),
        new(Quantity, true, 10, new[] { "Qty" }),
        new(Revision, true, 5, new[] { "Rev" }),
        new(PlannedDate, false, 20, new[] { "Planned Date", "Planned Issue Date", "Target Date" }),
        new(Status, false, 20, new[] { "Document Status" }),
    };

    public static MdrColumn Get(string name) => All.First(c => c.Name == name);

    /// <summary>Lower-cases and strips everything that is not a letter or digit (BOM, spaces, '_', '-', '.').</summary>
    public static string NormalizeHeader(string? header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(header.Length);
        foreach (var ch in header)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLowerInvariant(ch));
            }
        }

        return builder.ToString();
    }
}
