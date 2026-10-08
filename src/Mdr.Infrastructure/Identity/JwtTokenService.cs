using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Mdr.Domain.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mdr.Infrastructure.Identity;

/// <summary>Creates HS256-signed access tokens carrying name and role claims.</summary>
public sealed class JwtTokenService
{
    private readonly IOptions<JwtOptions> _options;
    private readonly TimeProvider _timeProvider;

    public JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
    }

    public static SymmetricSecurityKey CreateSigningKey(string signingKey) => new(Encoding.UTF8.GetBytes(signingKey));

    public (string Token, DateTime ExpiresAtUtc) CreateToken(AppUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(options.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.Name, user.UserName),
            new("display_name", user.DisplayName),
        };
        claims.AddRange(user.GetRoles().Select(role => new Claim(ClaimTypes.Role, role)));

        var credentials = new SigningCredentials(CreateSigningKey(options.SigningKey), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
