using Mdr.Domain.Validation;

namespace Mdr.Application.Uploads;

/// <summary>Pure aggregation logic for <c>GET /api/mdr/{id}/summary</c> (unit-tested without a database).</summary>
public sealed class MdrSummaryBuilder
{
    public const int TopIssueCodeCount = 10;

    public UploadSummaryDto Build(UploadSummaryData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var counters = data.Upload.Counters;
        var codeCounts = data.IssuesByCode.ToDictionary(c => c.Key, c => c.Count, StringComparer.Ordinal);

        int CodeCount(string code) => codeCounts.TryGetValue(code, out var count) ? count : 0;

        var severities = Enum.GetValues<IssueSeverity>()
            .OrderByDescending(s => s)
            .Select(s => new CountByKey(
                s.ToString(),
                data.IssuesBySeverity.Where(c => string.Equals(c.Key, s.ToString(), StringComparison.OrdinalIgnoreCase)).Sum(c => c.Count)))
            .ToList();

        var unavailableBatches = CodeCount(ValidationCodes.AiUnavailable);
        var attempted = data.ClassifiedCount > 0 || unavailableBatches > 0;

        var ai = new AiSummaryDto(
            Attempted: attempted,
            ClassifiedCount: data.ClassifiedCount,
            NotClassifiedCount: Math.Max(0, data.DocumentCount - data.ClassifiedCount),
            NeedsReviewCount: data.NeedsReviewCount,
            NeedsReviewRate: Rate(data.NeedsReviewCount, data.DocumentCount) ?? 0d,
            DisciplineAgreementRate: Rate(data.ClassifiedCount - data.DisciplineMismatchCount, data.ClassifiedCount),
            DocumentTypeAgreementRate: Rate(data.ClassifiedCount - data.DocumentTypeMismatchCount, data.ClassifiedCount),
            AverageConfidence: data.AverageConfidence.HasValue ? Math.Round(data.AverageConfidence.Value, 4) : null,
            LowConfidenceCount: CodeCount(ValidationCodes.AiLowConfidence),
            PotentialDuplicateCount: CodeCount(ValidationCodes.AiPotentialDuplicate),
            UnavailableBatchCount: unavailableBatches);

        return new UploadSummaryDto(
            data.Upload.Id,
            data.Upload.Status,
            counters,
            Rate(counters.ValidRows, counters.TotalRows) ?? 0d,
            Order(data.DocumentsByDiscipline),
            Order(data.DocumentsByDocumentType),
            Order(data.DocumentsByGateSet),
            severities,
            Order(data.IssuesByCode).Take(TopIssueCodeCount).ToList(),
            ai,
            data.Upload.StartedAtUtc,
            data.Upload.FinishedAtUtc,
            data.Upload.ProcessingDurationMs);
    }

    private static double? Rate(int numerator, int denominator) =>
        denominator <= 0 ? null : Math.Round((double)numerator / denominator, 4);

    private static IReadOnlyList<CountByKey> Order(IEnumerable<CountByKey> counts) =>
        counts.Where(c => c.Count > 0)
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Key, StringComparer.Ordinal)
            .ToList();
}
