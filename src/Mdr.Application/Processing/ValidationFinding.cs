using Mdr.Domain.Validation;

namespace Mdr.Application.Processing;

/// <summary>An in-memory validation result produced by a pipeline stage before it is persisted as a <see cref="ValidationIssue"/>.</summary>
public sealed record ValidationFinding(int RowNumber, string? Column, IssueSeverity Severity, string Code, string Message, string? Value)
{
    public bool IsError => Severity == IssueSeverity.Error;

    /// <summary>Creates a finding using the catalogue severity of <paramref name="code"/>.</summary>
    public static ValidationFinding Create(int rowNumber, string? column, string code, string message, string? value = null) =>
        new(rowNumber, column, ValidationCodes.SeverityOf(code), code, message, value);

    public static ValidationFinding FileLevel(string code, string message, string? value = null) =>
        Create(ValidationIssue.FileLevelRow, null, code, message, value);

    public ValidationIssue ToIssue(Guid uploadId, DateTime createdAtUtc) =>
        ValidationIssue.Create(uploadId, RowNumber, Column, Severity, Code, Message, Value, createdAtUtc);
}
