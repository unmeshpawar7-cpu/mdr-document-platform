using Mdr.Application.Classification;

namespace Mdr.Infrastructure.Classification;

/// <summary>Used when <c>Classifier:Enabled</c> is false: AI enrichment is skipped entirely.</summary>
public sealed class NullDocumentClassifier : IDocumentClassifier
{
    public bool IsEnabled => false;

    public Task<ClassificationResponse> ClassifyAsync(ClassificationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(ClassificationResponse.Unavailable("AI classification is disabled."));
}
