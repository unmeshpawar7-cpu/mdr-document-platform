using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Mdr.Infrastructure.Classification;

/// <summary>Reports the sidecar as Degraded (never Unhealthy) when unreachable: AI enrichment is advisory.</summary>
public sealed class ClassifierHealthCheck : IHealthCheck
{
    public const string HttpClientName = "classifier-health";

    private readonly IHttpClientFactory _httpClientFactory;

    public ClassifierHealthCheck(IHttpClientFactory httpClientFactory) => _httpClientFactory = httpClientFactory;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("Classifier sidecar reachable.")
                : HealthCheckResult.Degraded($"Classifier sidecar returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Degraded("Classifier sidecar unreachable; uploads continue without AI suggestions.", ex);
        }
    }
}
