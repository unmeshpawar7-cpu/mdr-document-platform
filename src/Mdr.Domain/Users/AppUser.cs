using Mdr.Domain.Common;

namespace Mdr.Domain.Users;

/// <summary>An API user. Passwords are stored as ASP.NET Core Identity PBKDF2 hashes, never in clear text.</summary>
public sealed class AppUser
{
    public const int MaxUserNameLength = 100;
    public const int MaxDisplayNameLength = 200;
    public const int MaxPasswordHashLength = 500;
    public const int MaxRolesLength = 200;

    private AppUser()
    {
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Comma-separated role names (see <see cref="AppRoles"/>).</summary>
    public string Roles { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static AppUser Create(string userName, string displayName, IEnumerable<string> roles, DateTime nowUtc)
    {
        var roleList = roles.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (roleList.Count == 0)
        {
            throw new DomainException("user_roles_required", "A user needs at least one role.");
        }

        var unknown = roleList.FirstOrDefault(r => !AppRoles.IsKnown(r));
        if (unknown is not null)
        {
            throw new DomainException("user_role_unknown", $"Role '{unknown}' is not a known application role.");
        }

        return new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = Text.Required(userName, "user_name", MaxUserNameLength).ToLowerInvariant(),
            DisplayName = Text.Required(displayName, "display_name", MaxDisplayNameLength),
            Roles = string.Join(",", roleList),
            IsActive = true,
            CreatedAtUtc = nowUtc,
        };
    }

    public IReadOnlyList<string> GetRoles() =>
        Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void SetPasswordHash(string passwordHash)
    {
        PasswordHash = Text.Required(passwordHash, "password_hash", MaxPasswordHashLength);
    }

    public void Deactivate() => IsActive = false;
}
