using Mdr.Domain.Users;
using Mdr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure.Identity;

public sealed class DemoUserOptions
{
    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public List<string> Roles { get; set; } = new();
}

/// <summary>Demo users from configuration section <c>DemoUsers</c>. Seeded by the host in Development only.</summary>
public sealed class DemoUsersOptions
{
    public const string SectionName = "DemoUsers";

    public List<DemoUserOptions> Users { get; set; } = new();
}

public static class DemoUserSeeder
{
    /// <summary>Creates configured users that do not exist yet. Existing users are never modified.</summary>
    public static async Task<int> SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<MdrDbContext>();
        var hasher = provider.GetRequiredService<IPasswordHasher<AppUser>>();
        var options = provider.GetRequiredService<IOptions<DemoUsersOptions>>().Value;
        var time = provider.GetRequiredService<TimeProvider>();
        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoUserSeeder));

        var created = 0;
        foreach (var demo in options.Users.Where(u => !string.IsNullOrWhiteSpace(u.UserName) && !string.IsNullOrWhiteSpace(u.Password)))
        {
            var userName = demo.UserName.Trim().ToLowerInvariant();
            if (await db.Users.AnyAsync(u => u.UserName == userName, cancellationToken))
            {
                continue;
            }

            var user = AppUser.Create(userName, string.IsNullOrWhiteSpace(demo.DisplayName) ? userName : demo.DisplayName, demo.Roles, time.GetUtcNow().UtcDateTime);
            user.SetPasswordHash(hasher.HashPassword(user, demo.Password));
            db.Users.Add(user);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} demo users", created);
        }

        return created;
    }
}
