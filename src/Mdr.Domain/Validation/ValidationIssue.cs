using Mdr.Domain.Common;

namespace Mdr.Domain.Validation;

/// <summary>
/// A finding raised while processing an upload. Row number 0 denotes a file-level issue;
/// data rows are numbered as in a spreadsheet (header = row 1, first data row = row 2).
/// </summary>
public sealed class ValidationIssue
{
    public const int FileLevelRow = 0;
    public const int MaxColumnNameLength = 100;
    public const int MaxCodeLength = 64;
    public const int MaxMessageLength = 1000;
    public const int MaxOffendingValueLength = 500;

    private ValidationIssue()
    {
    }

    public long Id { get; private set; }

    public Guid UploadId { get; private set; }

    public int RowNumber { get; private set; }

    public string? ColumnName { get; private set; }

    public IssueSeverity Severity { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public string? OffendingValue { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public static ValidationIssue Create(
        Guid uploadId,
        int rowNumber,
        string? columnName,
        IssueSeverity severity,
        string code,
        string message,
        string? offendingValue,
        DateTime createdAtUtc)
    {
        if (rowNumber < 0)
        {
            throw new DomainException("issue_row_invalid", "Row number cannot be negative.");
        }

        return new ValidationIssue
        {
            UploadId = uploadId,
            RowNumber = rowNumber,
            ColumnName = Text.Truncate(columnName, MaxColumnNameLength),
            Severity = severity,
            Code = Text.Required(code, "issue_code", MaxCodeLength),
            Message = Text.Truncate(message, MaxMessageLength) ?? string.Empty,
            OffendingValue = Text.Truncate(offendingValue, MaxOffendingValueLength),
            CreatedAtUtc = createdAtUtc,
        };
    }
}
