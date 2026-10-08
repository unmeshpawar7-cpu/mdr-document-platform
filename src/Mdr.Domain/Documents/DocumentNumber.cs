using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Mdr.Domain.Documents;

/// <summary>
/// Parsed document number in the format
/// <c>{ProjectCode}-{Office}-{Discipline}-{DocType}-{Seq}</c>, e.g. <c>NWP-001-PUN-MEC-DWG-0012</c>.
/// <list type="bullet">
/// <item><description>ProjectCode: 2-5 letters, hyphen, 3 digits (<c>NWP-001</c>)</description></item>
/// <item><description>Office: 3 letters (<c>PUN</c>)</description></item>
/// <item><description>Discipline: 3 letters (<c>MEC</c>)</description></item>
/// <item><description>DocType: 2-3 letters (<c>DWG</c>, <c>DS</c>)</description></item>
/// <item><description>Seq: 4 digits, 0001-9999</description></item>
/// </list>
/// Input is trimmed and upper-cased before parsing.
/// </summary>
public sealed record DocumentNumber(
    string ProjectCode,
    string OfficeCode,
    string DisciplineCode,
    string DocumentTypeCode,
    int Sequence)
{
    public const int MaxLength = 50;

    private static readonly Regex Pattern = new(
        @"^(?<project>[A-Z]{2,5}-\d{3})-(?<office>[A-Z]{3})-(?<discipline>[A-Z]{3})-(?<type>[A-Z]{2,3})-(?<seq>\d{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public string Value => string.Create(
        CultureInfo.InvariantCulture,
        $"{ProjectCode}-{OfficeCode}-{DisciplineCode}-{DocumentTypeCode}-{Sequence:D4}");

    /// <summary>Canonical form used for comparison and storage: trimmed, upper-case invariant.</summary>
    public static string Normalize(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant();

    public static bool TryParse(string? raw, [NotNullWhen(true)] out DocumentNumber? result)
    {
        result = null;
        var normalized = Normalize(raw);
        if (normalized.Length == 0 || normalized.Length > MaxLength)
        {
            return false;
        }

        var match = Pattern.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        var sequence = int.Parse(match.Groups["seq"].Value, NumberStyles.None, CultureInfo.InvariantCulture);
        if (sequence == 0)
        {
            return false;
        }

        result = new DocumentNumber(
            match.Groups["project"].Value,
            match.Groups["office"].Value,
            match.Groups["discipline"].Value,
            match.Groups["type"].Value,
            sequence);
        return true;
    }

    public override string ToString() => Value;
}
