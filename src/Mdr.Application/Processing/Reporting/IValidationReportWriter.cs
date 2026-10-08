namespace Mdr.Application.Processing.Reporting;

/// <summary>One line of the downloadable CSV validation report.</summary>
public sealed record ValidationReportRow(int RowNumber, string? Column, string Severity, string Code, string Message, string? Value);

/// <summary>Writes validation report rows as CSV (implemented with CsvHelper in Infrastructure).</summary>
public interface IValidationReportWriter
{
    Task WriteAsync(IAsyncEnumerable<ValidationReportRow> rows, Stream destination, CancellationToken cancellationToken);
}
