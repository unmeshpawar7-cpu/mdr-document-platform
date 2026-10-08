using Mdr.Application.Processing.Csv;
using Mdr.Application.ReferenceData;
using Mdr.Domain.Documents;
using Mdr.Domain.Validation;

namespace Mdr.Application.Processing.Stages;

/// <summary>Everything business validation needs that is constant for one upload.</summary>
public sealed record BusinessValidationContext(
    string ProjectCode,
    ReferenceDataSnapshot ReferenceData,
    IReadOnlyDictionary<string, Guid> WbsElements,
    DateOnly Today);

public sealed record BusinessValidationResult(IReadOnlyList<ValidationFinding> Findings, ResolvedMdrRow? Resolved)
{
    public bool HasErrors => Findings.Any(f => f.IsError);
}

/// <summary>
/// Stage 3: reference data lookups, WBS existence, gate set allow-list, quantity/revision/status rules and
/// document-number format + segment consistency. <see cref="BusinessValidationResult.Resolved"/> is only set
/// when every required value is present and no business rule failed.
/// </summary>
public interface IBusinessValidationStage
{
    BusinessValidationResult Validate(ParsedMdrRow row, BusinessValidationContext context);
}

public sealed class BusinessValidationStage : IBusinessValidationStage
{
    public BusinessValidationResult Validate(ParsedMdrRow row, BusinessValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(context);

        var findings = new List<ValidationFinding>();
        var n = row.RowNumber;
        var reference = context.ReferenceData;

        var discipline = Lookup<ReferenceItem>(row.DisciplineCode, reference.FindDiscipline, n, MdrColumns.Discipline, ValidationCodes.UnknownDiscipline, "discipline", findings);
        var documentType = Lookup<ReferenceItem>(row.DocumentTypeCode, reference.FindDocumentType, n, MdrColumns.DocumentType, ValidationCodes.UnknownDocumentType, "document type", findings);
        var office = Lookup<OfficeItem>(row.OfficeCode, reference.FindOffice, n, MdrColumns.Office, ValidationCodes.UnknownOffice, "office", findings);
        var gateSet = Lookup<GateSetItem>(row.GateSetCode, reference.FindGateSet, n, MdrColumns.GateSet, ValidationCodes.UnknownGateSet, "gate set", findings);

        if (documentType is not null && gateSet is not null && !reference.IsGateSetAllowed(documentType.Id, gateSet.Id))
        {
            var allowed = string.Join(", ", reference.AllowedGateSetCodes(documentType.Id));
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.GateSet,
                ValidationCodes.GateSetNotAllowed,
                $"Gate set '{gateSet.Code}' is not allowed for document type '{documentType.Code}'. Allowed: {allowed}.",
                row.GateSetCode));
        }

        Guid? wbsElementId = null;
        if (row.WbsCode is not null)
        {
            if (context.WbsElements.TryGetValue(row.WbsCode, out var id))
            {
                wbsElementId = id;
            }
            else
            {
                findings.Add(ValidationFinding.Create(
                    n,
                    MdrColumns.Wbs,
                    ValidationCodes.UnknownWbs,
                    $"WBS '{row.WbsCode}' does not exist in project {context.ProjectCode}.",
                    row.WbsCode));
            }
        }

        ValidateDocumentNumber(row, context, findings);

        if (row.Quantity is <= 0)
        {
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.Quantity,
                ValidationCodes.QuantityNotPositive,
                "Quantity must be greater than zero.",
                row.Quantity.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (row.Revision is not null && !MdrDocument.IsValidRevision(row.Revision))
        {
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.Revision,
                ValidationCodes.InvalidRevision,
                "Revision must be 1-3 upper-case letters or digits (e.g. A, 0, P01).",
                row.Revision));
        }

        if (row.PlannedDate is { } planned && planned < context.Today)
        {
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.PlannedDate,
                ValidationCodes.PlannedDateInPast,
                $"Planned date {planned:yyyy-MM-dd} is in the past.",
                planned.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        }

        var status = DocumentStatus.Planned;
        if (row.Status is not null && !TryParseStatus(row.Status, out status))
        {
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.Status,
                ValidationCodes.InvalidStatus,
                $"Status must be one of: {string.Join(", ", Enum.GetNames<DocumentStatus>())}.",
                row.Status));
        }

        var hasErrors = findings.Any(f => f.IsError);
        ResolvedMdrRow? resolved = null;
        if (!hasErrors
            && discipline is not null && documentType is not null && office is not null && gateSet is not null
            && wbsElementId.HasValue && row.DocumentNumber is not null && row.Title is not null
            && row.Quantity.HasValue && row.Revision is not null && row.WbsCode is not null)
        {
            resolved = new ResolvedMdrRow(
                n,
                row.DocumentNumber,
                row.Title,
                row.Description,
                discipline,
                documentType,
                office,
                gateSet,
                wbsElementId.Value,
                row.WbsCode,
                row.Quantity.Value,
                row.Revision,
                row.PlannedDate,
                status);
        }

        return new BusinessValidationResult(findings, resolved);
    }

    /// <summary>Accepts enum names case-insensitively, ignoring spaces, hyphens and underscores ("In Progress").</summary>
    public static bool TryParseStatus(string value, out DocumentStatus status)
    {
        var normalized = MdrColumns.NormalizeHeader(value);
        foreach (var candidate in Enum.GetValues<DocumentStatus>())
        {
            if (string.Equals(candidate.ToString(), normalized, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }

        status = DocumentStatus.Planned;
        return false;
    }

    private static T? Lookup<T>(
        string? code,
        Func<string?, T?> find,
        int row,
        string column,
        string errorCode,
        string label,
        List<ValidationFinding> findings)
        where T : class
    {
        if (code is null)
        {
            return null;
        }

        var item = find(code);
        if (item is null)
        {
            findings.Add(ValidationFinding.Create(row, column, errorCode, $"'{code}' is not a known {label} code.", code));
        }

        return item;
    }

    private static void ValidateDocumentNumber(ParsedMdrRow row, BusinessValidationContext context, List<ValidationFinding> findings)
    {
        if (row.DocumentNumber is null)
        {
            return;
        }

        var n = row.RowNumber;
        if (!DocumentNumber.TryParse(row.DocumentNumber, out var parsed))
        {
            findings.Add(ValidationFinding.Create(
                n,
                MdrColumns.DocumentNumber,
                ValidationCodes.DocumentNumberFormat,
                "Document number must follow {Project}-{Office}-{Discipline}-{Type}-{Seq} (e.g. NWP-001-PUN-MEC-DWG-0012); Seq is 0001-9999.",
                row.DocumentNumber));
            return;
        }

        void Check(string segment, string? expected, string code, string column, string label)
        {
            if (expected is not null && !string.Equals(segment, expected, StringComparison.Ordinal))
            {
                findings.Add(ValidationFinding.Create(
                    n,
                    column,
                    code,
                    $"Document number {label} segment '{segment}' does not match {label} '{expected}'.",
                    row.DocumentNumber));
            }
        }

        Check(parsed.ProjectCode, context.ProjectCode, ValidationCodes.DocumentNumberProjectMismatch, MdrColumns.DocumentNumber, "project");
        Check(parsed.OfficeCode, row.OfficeCode, ValidationCodes.DocumentNumberOfficeMismatch, MdrColumns.Office, "office");
        Check(parsed.DisciplineCode, row.DisciplineCode, ValidationCodes.DocumentNumberDisciplineMismatch, MdrColumns.Discipline, "discipline");
        Check(parsed.DocumentTypeCode, row.DocumentTypeCode, ValidationCodes.DocumentNumberTypeMismatch, MdrColumns.DocumentType, "document type");
    }
}
