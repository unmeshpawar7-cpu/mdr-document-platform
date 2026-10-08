using FluentValidation;
using Mdr.Application.Auth;
using Mdr.Application.Common.Exceptions;
using Mdr.Domain.Users;
using Mdr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Mdr.Infrastructure.Identity;

/// <summary>Verifies credentials with ASP.NET Core Identity's PBKDF2 <see cref="IPasswordHasher{TUser}"/> and issues JWTs.</summary>
public sealed class AuthService : IAuthService
{
    // Verifying against a dummy hash for unknown users keeps response timing similar (limits user enumeration).
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<AppUser>().HashPassword(null!, "not-a-real-password"));

    private readonly MdrDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly JwtTokenService _tokens;
    private readonly IValidator<TokenRequest> _validator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        MdrDbContext db,
        IPasswordHasher<AppUser> hasher,
        JwtTokenService tokens,
        IValidator<TokenRequest> validator,
        ILogger<AuthService> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _validator = validator;
        _logger = logger;
    }

    public async Task<TokenResponse> IssueTokenAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        await _validator.ValidateAndThrowAsync(request, cancellationToken);

        var userName = request.UserName.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserName == userName, cancellationToken);
        if (user is null || !user.IsActive)
        {
            _hasher.VerifyHashedPassword(null!, DummyHash.Value, request.Password);
            _logger.LogWarning("Token request rejected for unknown or inactive user");
            throw new AuthenticationFailedException();
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Token request rejected: wrong password for {UserName}", user.UserName);
            throw new AuthenticationFailedException();
        }

        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(_hasher.HashPassword(user, request.Password));
            await _db.SaveChangesAsync(cancellationToken);
        }

        var (token, expiresAtUtc) = _tokens.CreateToken(user);
        return new TokenResponse(token, "Bearer", expiresAtUtc, user.UserName, user.GetRoles());
    }
}
