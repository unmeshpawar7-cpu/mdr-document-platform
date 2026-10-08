using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Mdr.Application.Classification;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Mdr.Infrastructure.Classification;

/// <summary>
/// Typed HttpClient for <c>POST /v1/classify</c> on the Python sidecar. Retries, timeouts and circuit breaking come
/// from <c>AddStandardResilienceHandler()</c>; whatever still fails is converted into
/// <see cref="ClassificationResponse.Unavailable"/> so the upload degrades gracefully. Only caller cancellation propagates.
/// </summary>
public sealed class HttpDocumentClassifier : IDocumentClassifier
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ClassifyPath = "v1/classify";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptions<ClassifierOptions> _options;
    private readonly ILogger<HttpDocumentClassifier> _logger;

    public HttpDocumentClassifier(HttpClient httpClient, IOptions<ClassifierOptions> options, ILogger<HttpDocumentClassifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public bool IsEnabled => true;

    public async Task<ClassificationResponse> ClassifyAsync(ClassificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var started = Stopwatch.GetTimestamp();
        try
        {
            var payload = new ClassifyRequestWire(
                request.Items.Select(i => new ClassifyItemWire(i.Id, i.Title, i.Description, i.DocumentNumber)).ToList(),
                request.ExistingDocuments.Select(e => new ExistingDocumentWire(e.Id, e.Title, e.DocumentNumber)).ToList());

            using var message = new HttpRequestMessage(HttpMethod.Post, ClassifyPath)
            {
                Content = JsonContent.Create(payload, options: Json),
            };
            var apiKey = _options.Value.ApiKey;
            if (!string.IsNullOrEmpty(apiKey))
            {
                message.Headers.Add(ApiKeyHeader, apiKey);
            }

            using var response = await _httpClient.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Classifier returned HTTP {StatusCode} for {Items} items", (int)response.StatusCode, request.Items.Count);
                return ClassificationResponse.Unavailable($"classifier returned HTTP {(int)response.StatusCode}");
            }

            var body = await response.Content.ReadFromJsonAsync<ClassifyResponseWire>(Json, cancellationToken);
            if (body?.Results is null)
            {
                return ClassificationResponse.Unavailable("classifier returned an empty body");
            }

            var suggestions = body.Results
                .Where(r => !string.IsNullOrWhiteSpace(r.Id))
                .Select(r => new ClassificationSuggestion(
                    r.Id!,
                    r.Discipline,
                    r.DocumentType,
                    r.Category,
                    r.Priority,
                    r.Confidence,
                    r.MissingMetadata ?? new List<string>(),
                    r.PotentialDuplicateOf,
                    r.Rationale,
                    r.ClassifierVersion ?? body.ClassifierVersion ?? "unknown"))
                .ToList();

            _logger.LogInformation(
                "Classifier answered {Results} results for {Items} items in {ElapsedMs} ms (version {Version})",
                suggestions.Count,
                request.Items.Count,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                body.ClassifierVersion);
            return ClassificationResponse.Success(suggestions);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // HttpRequestException, timeouts (TimeoutRejectedException / TaskCanceledException), open circuit, bad JSON...
            _logger.LogWarning(
                ex,
                "Classifier call failed after {ElapsedMs} ms; continuing without AI suggestions",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return ClassificationResponse.Unavailable($"classifier call failed ({ex.GetType().Name})");
        }
    }

    internal sealed record ClassifyItemWire(string Id, string Title, string? Description, string? DocumentNumber);

    internal sealed record ExistingDocumentWire(string Id, string Title, string? DocumentNumber);

    internal sealed record ClassifyRequestWire(IReadOnlyList<ClassifyItemWire> Items, IReadOnlyList<ExistingDocumentWire> ExistingDocuments);

    internal sealed class ClassifyResultWire
    {
        public string? Id { get; set; }

        public string? Discipline { get; set; }

        public string? DocumentType { get; set; }

        public string? Category { get; set; }

        public string? Priority { get; set; }

        public double Confidence { get; set; }

        public List<string>? MissingMetadata { get; set; }

        public string? PotentialDuplicateOf { get; set; }

        public string? Rationale { get; set; }

        public string? ClassifierVersion { get; set; }
    }

    internal sealed class ClassifyResponseWire
    {
        public string? ClassifierVersion { get; set; }

        public List<ClassifyResultWire>? Results { get; set; }
    }
}
