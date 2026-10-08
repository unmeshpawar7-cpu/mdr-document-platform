using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Mdr.Api.Contracts;
using Mdr.Api.Infrastructure;
using Mdr.Application;
using Mdr.Application.Common.Abstractions;
using Mdr.Application.Options;
using Mdr.Infrastructure;
using Mdr.Infrastructure.Classification;
using Mdr.Infrastructure.Identity;
using Mdr.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Mdr.Api"));

// ----- Options (validated at startup) -----
builder.Services.AddOptions<MdrProcessingOptions>()
    .Bind(builder.Configuration.GetSection(MdrProcessingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RateLimitingOptions>()
    .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Reject oversized bodies before they are buffered: multipart and Kestrel limits sit just above the file limit.
var maxUploadBytes = builder.Configuration.GetSection(MdrProcessingOptions.SectionName)
    .GetValue<long?>(nameof(MdrProcessingOptions.MaxFileSizeBytes)) ?? 10L * 1024 * 1024;
const long multipartOverheadBytes = 64 * 1024;
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxUploadBytes + multipartOverheadBytes);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxUploadBytes + multipartOverheadBytes);

// ----- Application + infrastructure -----
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

// ----- API surface -----
builder.Services.AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value is not null && entry.Value.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(error => new ApiErrorItem(
                    JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                    string.IsNullOrWhiteSpace(error.ErrorMessage) ? "The value is invalid." : error.ErrorMessage)))
                .ToList();
            return new BadRequestObjectResult(ApiErrorResponse.Create("Validation failed.", context.HttpContext.TraceIdentifier, errors));
        };
    });
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MDR Document Platform API",
        Version = "v1",
        Description = "Master Deliverables Register ingestion: asynchronous CSV validation, persistence and advisory AI classification.",
        License = new OpenApiLicense { Name = "MIT" },
    });
    var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Name = "Authorization",
        Description = "JWT from POST /api/auth/token",
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        },
    });
});

// ----- Authentication / authorization (JWT settings resolved lazily from validated options) -----
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
    {
        var jwt = jwtOptions.Value;
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role,
        };
        bearer.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create("Authentication is required.", context.HttpContext.TraceIdentifier));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(ApiErrorResponse.Create("You do not have permission to perform this action.", context.HttpContext.TraceIdentifier));
            },
        };
    });
builder.Services.AddAuthorization(Policies.Configure);

// ----- Rate limiting (per user for uploads, per IP for token requests) -----
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            ApiErrorResponse.Create("Too many requests. Retry later.", context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
    options.AddPolicy(RateLimitPolicies.Uploads, httpContext =>
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var key = httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limits.UploadsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
    options.AddPolicy(RateLimitPolicies.Auth, httpContext =>
    {
        var limits = httpContext.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limits.AuthAttemptsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        });
    });
});

// ----- Health checks -----
var healthChecks = builder.Services.AddHealthChecks()
    .AddDbContextCheck<MdrDbContext>("database", tags: new[] { "ready" });
if (Mdr.Infrastructure.DependencyInjection.IsClassifierEnabled(builder.Configuration))
{
    healthChecks.AddCheck<ClassifierHealthCheck>("ai-classifier", HealthStatus.Degraded, new[] { "ready" });
}

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnostics, httpContext) =>
    {
        diagnostics.Set("CorrelationId", httpContext.TraceIdentifier);
        diagnostics.Set("UserName", httpContext.User.Identity?.Name ?? "anonymous");
    };
});
app.UseExceptionHandler();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "MDR Document Platform v1"));
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

await DatabaseInitializer.InitializeAsync(app);

app.Run();

/// <summary>Entry point marker for WebApplicationFactory in integration tests.</summary>
public partial class Program
{
}
