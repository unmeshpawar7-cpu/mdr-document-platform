using Mdr.Application.Audit;
using Mdr.Application.Auth;
using Mdr.Application.Classification;
using Mdr.Application.Common.Abstractions;
using Mdr.Application.Common.Persistence;
using Mdr.Application.Processing;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Reporting;
using Mdr.Application.Projects;
using Mdr.Application.Uploads;
using Mdr.Domain.Users;
using Mdr.Infrastructure.Auditing;
using Mdr.Infrastructure.Background;
using Mdr.Infrastructure.Classification;
using Mdr.Infrastructure.Csv;
using Mdr.Infrastructure.Identity;
using Mdr.Infrastructure.Persistence;
using Mdr.Infrastructure.Persistence.Queries;
using Mdr.Infrastructure.Persistence.Repositories;
using Mdr.Infrastructure.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "MdrDatabase";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The connection string is read when the DbContext is first resolved, so test hosts can override it.
        services.AddDbContext<MdrDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(ConnectionStringName)
                ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");
            options.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(MdrDbContext).Assembly.FullName));
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IUploadRepository, UploadRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IValidationIssueRepository, ValidationIssueRepository>();
        services.AddScoped<IReferenceDataRepository, ReferenceDataRepository>();
        services.AddScoped<IMdrQueries, MdrQueries>();
        services.AddScoped<IProjectQueries, ProjectQueries>();
        services.AddScoped<IAuditLogQueries, AuditLogQueries>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        services.TryAddScoped<ICurrentUser, SystemCurrentUser>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IMdrCsvReader, CsvHelperMdrReader>();
        services.AddSingleton<IValidationReportWriter, CsvValidationReportWriter>();

        services.AddSingleton<IUploadQueue, ChannelUploadQueue>();
        services.AddHostedService<UploadProcessingWorker>();

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DemoUsersOptions>().Bind(configuration.GetSection(DemoUsersOptions.SectionName));
        services.AddSingleton<JwtTokenService>();
        services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
        services.AddScoped<IAuthService, AuthService>();

        AddClassifier(services, configuration);
        return services;
    }

    private static void AddClassifier(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ClassifierOptions>()
            .Bind(configuration.GetSection(ClassifierOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var section = configuration.GetSection(ClassifierOptions.SectionName);
        if (!section.GetValue<bool>(nameof(ClassifierOptions.Enabled)))
        {
            services.AddSingleton<IDocumentClassifier, NullDocumentClassifier>();
            return;
        }

        var attemptSeconds = Math.Max(1, section.GetValue(nameof(ClassifierOptions.AttemptTimeoutSeconds), 10));
        var totalSeconds = Math.Max(attemptSeconds + 1, section.GetValue(nameof(ClassifierOptions.TotalTimeoutSeconds), 30));
        var retries = Math.Clamp(section.GetValue(nameof(ClassifierOptions.MaxRetryAttempts), 2), 1, 5);

        static Uri BaseUri(IServiceProvider sp) =>
            new(sp.GetRequiredService<IOptions<ClassifierOptions>>().Value.BaseUrl.TrimEnd('/') + "/");

        services.AddHttpClient<IDocumentClassifier, HttpDocumentClassifier>((sp, client) => client.BaseAddress = BaseUri(sp))
            .AddStandardResilienceHandler(options =>
            {
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptSeconds);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(totalSeconds);
                options.Retry.MaxRetryAttempts = retries;
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(Math.Max(30, attemptSeconds * 2));
            });

        services.AddHttpClient(ClassifierHealthCheck.HttpClientName, (sp, client) =>
        {
            client.BaseAddress = BaseUri(sp);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
    }

    /// <summary>True when the classifier sidecar is enabled in configuration (used to register its health check).</summary>
    public static bool IsClassifierEnabled(IConfiguration configuration) =>
        configuration.GetSection(ClassifierOptions.SectionName).GetValue<bool>(nameof(ClassifierOptions.Enabled));
}
