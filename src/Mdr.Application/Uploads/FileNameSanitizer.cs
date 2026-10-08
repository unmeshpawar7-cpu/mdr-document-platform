namespace Mdr.Application.Uploads;

/// <summary>
/// Produces a display-safe version of a client file name. The result is only stored for display and audit;
/// content is always written under a generated storage name.
/// </summary>
public static class FileNameSanitizer
{
    public const int MaxLength = 260;

    public static string Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "upload.csv";
        }

        // Strip any client path (both separators, regardless of server OS).
        var name = fileName.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !char.IsControl(c) && Array.IndexOf(invalid, c) < 0).ToArray()).Trim().TrimStart('.');
        if (cleaned.Length == 0)
        {
            return "upload.csv";
        }

        return cleaned.Length <= MaxLength ? cleaned : cleaned[^MaxLength..];
    }
}
