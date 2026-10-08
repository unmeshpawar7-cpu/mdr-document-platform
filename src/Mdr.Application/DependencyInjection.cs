using FluentValidation;
using Mdr.Application.Processing;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Stages;
using Mdr.Application.Projects;
using Mdr.Application.ReferenceData;
using Mdr.Application.Uploads;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mdr.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services and pipeline stages. Options are bound by the host.</summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<CreateProjectRequestValidator>();
        services.TryAddSingleton(TimeProvider.System);

        // Stateless pipeline stages.
        services.AddSingleton<IHeaderMapper, HeaderMapper>();
        services.AddSingleton<IFileValidationStage, FileValidationStage>();
        services.AddSingleton<ISchemaValidationStage, SchemaValidationStage>();
        services.AddSingleton<IBusinessValidationStage, BusinessValidationStage>();
        services.AddSingleton<MdrSummaryBuilder>();

        // Stages that touch the database share the job's DI scope (and DbContext).
        services.AddScoped<IDuplicateDetectionStage, DuplicateDetectionStage>();
        services.AddScoped<IPersistenceStage, PersistenceStage>();
        services.AddScoped<IClassificationEnrichmentStage, ClassificationEnrichmentStage>();
        services.AddScoped<IMdrUploadProcessor, MdrUploadProcessor>();

        services.AddScoped<IMdrUploadService, MdrUploadService>();
        services.AddScoped<IMdrUploadQueryService, MdrUploadQueryService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<IReferenceDataService, ReferenceDataService>();
        return services;
    }
}
