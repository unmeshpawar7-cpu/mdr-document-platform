using FluentValidation;

namespace Mdr.Application.Auth;

/// <summary>Credentials exchanged for a JWT.</summary>
public sealed record TokenRequest(string UserName, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string UserName, IReadOnlyList<string> Roles);

/// <summary>Verifies credentials and issues JWTs (implemented in Infrastructure with PasswordHasher and JwtSecurityTokenHandler).</summary>
public interface IAuthService
{
    /// <summary>Throws <c>AuthenticationFailedException</c> for unknown users, wrong passwords or inactive accounts.</summary>
    Task<TokenResponse> IssueTokenAsync(TokenRequest request, CancellationToken cancellationToken);
}

public sealed class TokenRequestValidator : AbstractValidator<TokenRequest>
{
    public TokenRequestValidator()
    {
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(200);
    }
}
