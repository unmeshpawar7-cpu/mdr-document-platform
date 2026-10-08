using Mdr.Domain.Users;
using Microsoft.AspNetCore.Authorization;

namespace Mdr.Api.Infrastructure;

/// <summary>Role-based policies: Viewer &lt; DocumentController &lt; Admin.</summary>
public static class Policies
{
    public const string CanRead = "CanRead";
    public const string CanManage = "CanManage";
    public const string AdminOnly = "AdminOnly";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(CanRead, p => p.RequireRole(AppRoles.Viewer, AppRoles.DocumentController, AppRoles.Admin));
        options.AddPolicy(CanManage, p => p.RequireRole(AppRoles.DocumentController, AppRoles.Admin));
        options.AddPolicy(AdminOnly, p => p.RequireRole(AppRoles.Admin));
    }
}

public static class RateLimitPolicies
{
    public const string Uploads = "uploads";
    public const string Auth = "auth";
}

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    [System.ComponentModel.DataAnnotations.Range(1, 10_000)]
    public int UploadsPerMinute { get; set; } = 10;

    [System.ComponentModel.DataAnnotations.Range(1, 10_000)]
    public int AuthAttemptsPerMinute { get; set; } = 20;
}
