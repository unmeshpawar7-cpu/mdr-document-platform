using System.ComponentModel.DataAnnotations;

namespace Mdr.Infrastructure.Identity;

/// <summary>JWT issuing/validation settings (section <c>Jwt</c>). The signing key comes from the environment.</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "mdr-document-platform";

    [Required]
    public string Audience { get; set; } = "mdr-document-platform-clients";

    /// <summary>HS256 key, at least 32 characters (shorter keys throw at runtime).</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    [Range(5, 1440)]
    public int AccessTokenMinutes { get; set; } = 60;
}
