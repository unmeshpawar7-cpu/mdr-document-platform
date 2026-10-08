using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mdr.Api.Infrastructure;

/// <summary>Compact JSON health report (status per check, no exception details).</summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = (long)e.Value.Duration.TotalMilliseconds,
            }),
        };
        return context.Response.WriteAsJsonAsync(payload);
    }
}
