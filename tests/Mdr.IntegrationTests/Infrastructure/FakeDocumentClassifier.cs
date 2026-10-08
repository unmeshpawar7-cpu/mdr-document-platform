using Mdr.Application.Classification;
using Mdr.Domain.Documents;

namespace Mdr.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic classifier used instead of the Python sidecar: it echoes the discipline/type encoded in the
/// document number with high confidence, or reports itself unavailable when <see cref="Available"/> is false.
/// </summary>
public sealed class FakeDocumentClassifier : IDocumentClassifier
{
    private volatile bool _available = true;

    public bool Available
    {
        get => _available;
        set => _available = value;
    }

    public bool IsEnabled => true;

    public Task<ClassificationResponse> ClassifyAsync(ClassificationRequest request, CancellationToken cancellationToken)
    {
        if (!Available)
        {
            return Task.FromResult(ClassificationResponse.Unavailable("fake classifier offline"));
        }

        var suggestions = request.Items
            .Select(item =>
            {
                DocumentNumber.TryParse(item.DocumentNumber, out var number);
                IReadOnlyList<string> missing = string.IsNullOrWhiteSpace(item.Description) ? new[] { "description" } : Array.Empty<string>();
                return new ClassificationSuggestion(
                    item.Id,
                    number?.DisciplineCode,
                    number?.DocumentTypeCode,
                    "Engineering",
                    "Medium",
                    0.9,
                    missing,
                    null,
                    "integration-test fake",
                    "fake-classifier-1");
            })
            .ToList();
        return Task.FromResult(ClassificationResponse.Success(suggestions));
    }
}
