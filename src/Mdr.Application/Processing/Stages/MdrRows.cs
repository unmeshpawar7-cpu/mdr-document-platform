using Mdr.Application.ReferenceData;
using Mdr.Domain.Documents;

namespace Mdr.Application.Processing.Stages;

/// <summary>
/// A row after schema validation. Values are trimmed; codes and document numbers are upper-cased.
/// A value that failed schema validation is passed on as <c>null</c> so later stages do not double-report it.
/// </summary>
public sealed record ParsedMdrRow
{
    public required int RowNumber { get; init; }

    public string? DocumentNumber { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? DisciplineCode { get; init; }

    public string? DocumentTypeCode { get; init; }

    public string? WbsCode { get; init; }

    public string? GateSetCode { get; init; }

    public string? OfficeCode { get; init; }

    public int? Quantity { get; init; }

    public string? Revision { get; init; }

    public DateOnly? PlannedDate { get; init; }

    public string? Status { get; init; }
}

/// <summary>A fully validated row with reference data resolved to ids, ready to persist.</summary>
public sealed record ResolvedMdrRow(
    int RowNumber,
    string DocumentNumber,
    string Title,
    string? Description,
    ReferenceItem Discipline,
    ReferenceItem DocumentType,
    OfficeItem Office,
    GateSetItem GateSet,
    Guid WbsElementId,
    string WbsCode,
    int Quantity,
    string Revision,
    DateOnly? PlannedDate,
    DocumentStatus Status);
