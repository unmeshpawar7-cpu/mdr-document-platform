using System.Net;
using System.Text;
using FluentAssertions;
using Mdr.Application.Classification;
using Mdr.Infrastructure.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Infrastructure;

public sealed class HttpDocumentClassifierTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return await _respond(request, cancellationToken);
        }
    }

    private static readonly ClassificationRequest Request = new(
        new[] { new ClassificationItem("doc-1", "Raw water intake pump datasheet", "Pump duty", "NWP-001-PUN-MEC-DS-0002") },
        new[] { new ExistingDocumentHint("old-1", "Intake pump datasheet", null) });

    private static (HttpDocumentClassifier Classifier, StubHandler Handler) Create(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var handler = new StubHandler(respond);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://classifier.test/") };
        var options = Options.Create(new ClassifierOptions { Enabled = true, ApiKey = "test-key" });
        return (new HttpDocumentClassifier(client, options, NullLogger<HttpDocumentClassifier>.Instance), handler);
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Posts_camel_case_batch_with_api_key_and_maps_results()
    {
        const string body = """
        {
          "classifierVersion": "taxonomy-2026.10+tfidf",
          "results": [
            { "id": "doc-1", "discipline": "MEC", "documentType": "DS", "category": "Rotating equipment", "priority": "High",
              "confidence": 0.87, "missingMetadata": [], "potentialDuplicateOf": "old-1", "rationale": "pump, datasheet" }
          ]
        }
        """;
        var (classifier, handler) = Create((_, _) => Task.FromResult(Json(body)));

        var response = await classifier.ClassifyAsync(Request, default);

        response.Succeeded.Should().BeTrue();
        var suggestion = response.Suggestions.Should().ContainSingle().Subject;
        suggestion.Discipline.Should().Be("MEC");
        suggestion.Confidence.Should().Be(0.87);
        suggestion.PotentialDuplicateOf.Should().Be("old-1");
        suggestion.ClassifierVersion.Should().Be("taxonomy-2026.10+tfidf", "the batch version is used when an item omits it");

        var request = handler.Requests.Single();
        request.RequestUri!.AbsoluteUri.Should().Be("http://classifier.test/v1/classify");
        request.Headers.GetValues(HttpDocumentClassifier.ApiKeyHeader).Should().Equal("test-key");
        handler.Bodies.Single().Should().Contain("\"documentNumber\":\"NWP-001-PUN-MEC-DS-0002\"").And.Contain("\"existingDocuments\"");
    }

    [Fact]
    public async Task Server_error_returns_unavailable()
    {
        var (classifier, _) = Create((_, _) => Task.FromResult(Json("{}", HttpStatusCode.ServiceUnavailable)));
        var response = await classifier.ClassifyAsync(Request, default);
        response.Succeeded.Should().BeFalse();
        response.FailureReason.Should().Contain("503");
    }

    [Fact]
    public async Task Connection_failure_returns_unavailable()
    {
        var (classifier, _) = Create((_, _) => throw new HttpRequestException("connection refused"));
        (await classifier.ClassifyAsync(Request, default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Timeout_returns_unavailable()
    {
        var (classifier, _) = Create((_, _) => throw new TaskCanceledException("timeout"));
        var response = await classifier.ClassifyAsync(Request, default);
        response.Succeeded.Should().BeFalse("a timeout that was not requested by the caller degrades gracefully");
    }

    [Fact]
    public async Task Malformed_json_returns_unavailable()
    {
        var (classifier, _) = Create((_, _) => Task.FromResult(Json("not json")));
        (await classifier.ClassifyAsync(Request, default)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (classifier, _) = Create((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        var act = () => classifier.ClassifyAsync(Request, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Null_classifier_is_disabled()
    {
        var classifier = new NullDocumentClassifier();
        classifier.IsEnabled.Should().BeFalse();
        (await classifier.ClassifyAsync(Request, default)).Succeeded.Should().BeFalse();
    }
}
