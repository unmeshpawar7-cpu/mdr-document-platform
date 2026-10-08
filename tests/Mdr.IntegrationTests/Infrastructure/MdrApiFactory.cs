using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Mdr.Application.Classification;
using Mdr.Domain.Users;
using Mdr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mdr.IntegrationTests.Infrastructure;

/// <summary>
/// Hosts the real API in-process with SQLite instead of SQL Server and a fake AI classifier.
/// A temporary SQLite file in WAL mode is used (rather than one shared in-memory connection) because the
/// background worker and polling requests use the database concurrently, each with its own DbContext.
/// </summary>
public class MdrApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string SigningKey = "integration-tests-signing-key-0123456789abcdef";
    public const string Password = "Integration!Pass1";

    public static readonly (string UserName, string Role)[] Users =
    {
        ("admin", AppRoles.Admin),
        ("controller", AppRoles.DocumentController),
        ("viewer", AppRoles.Viewer),
    };

    private readonly string _workDirectory = Path.Combine(Path.GetTempPath(), "mdr-it", Guid.NewGuid().ToString("N"));

    public MdrApiFactory()
    {
        Directory.CreateDirectory(_workDirectory);
    }

    public FakeDocumentClassifier Classifier { get; } = new();

    public string ConnectionString => new SqliteConnectionStringBuilder { DataSource = Path.Combine(_workDirectory, "mdr.db") }.ToString();

    protected virtual int UploadsPerMinute => 1000;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = SigningKey,
            ["Storage:RootPath"] = Path.Combine(_workDirectory, "uploads"),
            ["Database:EnsureCreated"] = "true",
            ["Database:ApplyMigrationsOnStartup"] = "false",
            ["RateLimiting:UploadsPerMinute"] = UploadsPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RateLimiting:AuthAttemptsPerMinute"] = "1000",
            ["Classifier:Enabled"] = "false",
            ["MdrProcessing:MaxFileSizeBytes"] = "1048576",
        };

        builder.UseEnvironment("Testing");
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<MdrDbContext>>();
            services.AddDbContext<MdrDbContext>(options => options.UseSqlite(ConnectionString));
            services.RemoveAll<IDocumentClassifier>();
            services.AddSingleton<IDocumentClassifier>(Classifier);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MdrDbContext>();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        foreach (var (userName, role) in Users)
        {
            if (await db.Users.AnyAsync(u => u.UserName == userName))
            {
                continue;
            }

            var user = AppUser.Create(userName, $"Test {role}", new[] { role }, DateTime.UtcNow);
            user.SetPasswordHash(hasher.HashPassword(user, Password));
            db.Users.Add(user);
        }

        await db.SaveChangesAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_workDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort clean-up of the temp folder.
        }
    }

    public async Task<string> GetTokenAsync(string userName)
    {
        using var client = CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userName, password = Password });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("data").GetProperty("accessToken").GetString()!;
    }

    public async Task<HttpClient> CreateClientForAsync(string userName)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await GetTokenAsync(userName));
        return client;
    }
}

/// <summary>Variant with a tiny upload rate limit for the 429 test.</summary>
public sealed class LowRateLimitFactory : MdrApiFactory
{
    protected override int UploadsPerMinute => 2;
}
