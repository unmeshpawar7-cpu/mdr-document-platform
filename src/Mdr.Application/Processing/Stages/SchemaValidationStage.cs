using System.Globalization;
using Mdr.Application.Processing.Csv;
using Mdr.Domain.Common;
using Mdr.Domain.Documents;
using Mdr.Domain.Validation;

namespace Mdr.Application.Processing.Stages;

public sealed record SchemaValidationResult(ParsedMdrRow Row, IReadOnlyList<ValidationFinding> Findings)
{
    public bool HasErrors => Findings.Any(f => f.IsError);
}

/// <summary>Stage 2 (row part): required values, max lengths and type parsing.</summary>
public interface ISchemaValidationStage
{
    SchemaValidationResult Validate(CsvRecord record, HeaderMapping mapping);
}

public sealed class SchemaValidationStage : ISchemaValidationStage
{
    public const string DateFormat = "yyyy-MM-dd";

    public SchemaValidationResult Validate(CsvRecord record, HeaderMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(mapping);

        var findings = new List<ValidationFinding>();
        var row = record.RowNumber;

        if (record.Fields.Count > mapping.HeaderFieldCount)
        {
            findings.Add(ValidationFinding.Create(
                row,
                null,
                ValidationCodes.MalformedRow,
                $"Row has {record.Fields.Count} fields but the header has {mapping.HeaderFieldCount}; values may be shifted (check for unquoted commas)."));
        }

        string? Read(string columnName)
        {
            var column = MdrColumns.Get(columnName);
            string? raw = null;
            if (mapping.ColumnIndexes.TryGetValue(column.Name, out var index) && index < record.Fields.Count)
            {
                raw = record.Fields[index];
            }

            var value = Text.NullIfBlank(raw);
            if (value is null)
            {
                if (column.Required)
                {
                    findings.Add(ValidationFinding.Create(row, column.Name, ValidationCodes.RequiredMissing, $"{column.Name} is required."));
                }

                return null;
            }

            if (value.Length > column.MaxLength)
            {
                findings.Add(ValidationFinding.Create(
                    row,
                    column.Name,
                    ValidationCodes.MaxLengthExceeded,
                    $"{column.Name} is {value.Length} characters; the maximum is {column.MaxLength}.",
                    value));
                return null;
            }

            return value;
        }

        string? Upper(string? value) => value?.ToUpperInvariant();

        var documentNumber = Read(MdrColumns.DocumentNumber);
        var title = Read(MdrColumns.Title);
        var description = Read(MdrColumns.Description);
        var discipline = Upper(Read(MdrColumns.Discipline));
        var documentType = Upper(Read(MdrColumns.DocumentType));
        var wbs = Read(MdrColumns.Wbs);
        var gateSet = Upper(Read(MdrColumns.GateSet));
        var office = Upper(Read(MdrColumns.Office));
        var quantityText = Read(MdrColumns.Quantity);
        var revision = Upper(Read(MdrColumns.Revision));
        var plannedDateText = Read(MdrColumns.PlannedDate);
        var status = Read(MdrColumns.Status);

        int? quantity = null;
        if (quantityText is not null)
        {
            if (int.TryParse(quantityText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed))
            {
                quantity = parsed;
            }
            else
            {
                findings.Add(ValidationFinding.Create(
                    row,
                    MdrColumns.Quantity,
                    ValidationCodes.InvalidInteger,
                    "Quantity must be a whole number.",
                    quantityText));
            }
        }

        DateOnly? plannedDate = null;
        if (plannedDateText is not null)
        {
            if (DateOnly.TryParseExact(plannedDateText, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                plannedDate = date;
            }
            else
            {
                findings.Add(ValidationFinding.Create(
                    row,
                    MdrColumns.PlannedDate,
                    ValidationCodes.InvalidDate,
                    $"PlannedDate must be an ISO date ({DateFormat}).",
                    plannedDateText));
            }
        }

        var parsedRow = new ParsedMdrRow
        {
            RowNumber = row,
            DocumentNumber = documentNumber is null ? null : DocumentNumber.Normalize(documentNumber),
            Title = title,
            Description = description,
            DisciplineCode = discipline,
            DocumentTypeCode = documentType,
            WbsCode = wbs,
            GateSetCode = gateSet,
            OfficeCode = office,
            Quantity = quantity,
            Revision = revision,
            PlannedDate = plannedDate,
            Status = status,
        };

        return new SchemaValidationResult(parsedRow, findings);
    }
}
