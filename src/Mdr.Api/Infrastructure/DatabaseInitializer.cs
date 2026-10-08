using Mdr.Infrastructure.Identity;
using Mdr.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mdr.Api.Infrastructure;

/// <summary>
/// Startup database tasks, all opt-in: apply migrations (<c>Database:ApplyMigrationsOnStartup</c>), create the schema
/// without migrations for SQLite/test hosts (<c>Database:EnsureCreated</c>), and seed demo users (Development only).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(WebApplication app)
    {
        var configuration = app.Configuration;
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitializer));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MdrDbContext>();
            if (configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
            {
                logger.LogInformation("Applying database migrations");
                await db.Database.MigrateAsync();
            }
            else if (configuration.GetValue<bool>("Database:EnsureCreated"))
            {
                await db.Database.EnsureCreatedAsync();
            }
        }

        if (app.Environment.IsDevelopment())
        {
            try
            {
                await DemoUserSeeder.SeedAsync(app.Services, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Demo user seeding skipped (is the database reachable and migrated?)");
            }
        }
    }
}
