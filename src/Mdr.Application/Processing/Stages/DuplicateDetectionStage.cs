using Mdr.Application.Common.Persistence;
using Mdr.Application.Processing.Csv;
using Mdr.Domain.Validation;

namespace Mdr.Application.Processing.Stages;

public sealed record DocumentNumberOccurrence(int RowNumber, string DocumentNumber);

public sealed record DuplicateDetectionResult(IReadOnlyList<ValidationFinding> Findings, IReadOnlySet<int> DuplicateRowNumbers);

/// <summary>
/// Stage 4: duplicate document numbers. In-file: the first occurrence (lowest row number) owns the number and every
/// later occurrence gets MDR020. Existing: one set-based repository query for all distinct numbers; every row whose
/// number already exists in the project gets MDR021.
/// </summary>
public interface IDuplicateDetectionStage
{
    Task<DuplicateDetectionResult> DetectAsync(Guid projectId, IReadOnlyList<DocumentNumberOccurrence> occurrences, CancellationToken cancellationToken);
}

public sealed class DuplicateDetectionStage : IDuplicateDetectionStage
{
    private readonly IDocumentRepository _documents;

    public DuplicateDetectionStage(IDocumentRepository documents)
    {
        _documents = documents;
    }

    public async Task<DuplicateDetectionResult> DetectAsync(
        Guid projectId,
        IReadOnlyList<DocumentNumberOccurrence> occurrences,
        CancellationToken cancellationToken)
    {
        var findings = new List<ValidationFinding>();
        var duplicateRows = new HashSet<int>();
        var firstSeen = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = occurrences.OrderBy(o => o.RowNumber).ToList();

        foreach (var occurrence in ordered)
        {
            if (firstSeen.TryGetValue(occurrence.DocumentNumber, out var firstRow))
            {
                duplicateRows.Add(occurrence.RowNumber);
                findings.Add(ValidationFinding.Create(
                    occurrence.RowNumber,
                    MdrColumns.DocumentNumber,
                    ValidationCodes.DuplicateInFile,
                    $"Document number {occurrence.DocumentNumber} already appears on row {firstRow}.",
                    occurrence.DocumentNumber));
            }
            else
            {
                firstSeen.Add(occurrence.DocumentNumber, occurrence.RowNumber);
            }
        }

        if (firstSeen.Count > 0)
        {
            var existing = await _documents.GetExistingDocumentNumbersAsync(projectId, firstSeen.Keys.ToList(), cancellationToken);
            if (existing.Count > 0)
            {
                foreach (var occurrence in ordered.Where(o => existing.Contains(o.DocumentNumber)))
                {
                    duplicateRows.Add(occurrence.RowNumber);
                    findings.Add(ValidationFinding.Create(
                        occurrence.RowNumber,
                        MdrColumns.DocumentNumber,
                        ValidationCodes.DuplicateExisting,
                        $"Document number {occurrence.DocumentNumber} already exists in the project register.",
                        occurrence.DocumentNumber));
                }
            }
        }

        return new DuplicateDetectionResult(findings, duplicateRows);
    }
}
