using System.Text.RegularExpressions;

namespace Mdr.Domain.Projects;

/// <summary>
/// Work Breakdown Structure codes are dot-separated numeric segments, e.g. <c>1</c>, <c>1.2</c>, <c>1.2.3</c>.
/// Up to six levels, each segment 1-3 digits.
/// </summary>
public static class WbsCode
{
    public const int MaxLength = 30;
    public const int MaxLevels = 6;

    private static readonly Regex Pattern = new(
        @"^\d{1,3}(\.\d{1,3}){0,5}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static bool IsValid(string? code) =>
        !string.IsNullOrWhiteSpace(code) && code.Length <= MaxLength && Pattern.IsMatch(code);

    /// <summary>Number of segments in the code (1 for a top-level element).</summary>
    public static int GetLevel(string code) => code.Split('.').Length;

    /// <summary>Returns the parent code (<c>1.2</c> for <c>1.2.3</c>) or <c>null</c> for top-level codes.</summary>
    public static string? GetParent(string code)
    {
        var index = code.LastIndexOf('.');
        return index < 0 ? null : code[..index];
    }
}
