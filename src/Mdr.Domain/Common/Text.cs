namespace Mdr.Domain.Common;

/// <summary>Small string helpers shared by the domain model.</summary>
public static class Text
{
    /// <summary>Truncates <paramref name="value"/> to at most <paramref name="maxLength"/> characters.</summary>
    public static string? Truncate(string? value, int maxLength)
    {
        if (value is null)
        {
            return null;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    /// <summary>Trims the value and returns <c>null</c> when it is empty or whitespace.</summary>
    public static string? NullIfBlank(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    /// <summary>Throws a <see cref="DomainException"/> when the value is blank or too long.</summary>
    public static string Required(string? value, string field, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{field}_required", $"{field} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{field}_too_long", $"{field} must be at most {maxLength} characters.");
        }

        return trimmed;
    }
}
