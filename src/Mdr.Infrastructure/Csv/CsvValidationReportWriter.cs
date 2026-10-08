using System.Globalization;
using System.Text;
using CsvHelper;
using Mdr.Application.Processing.Reporting;

namespace Mdr.Infrastructure.Csv;

/// <summary>
/// Writes the validation report as UTF-8 (with BOM, so Excel detects the encoding) CSV. Cell values that start with
/// a formula trigger (= + - @ tab CR) are prefixed with an apostrophe to prevent CSV/formula injection.
/// </summary>
public sealed class CsvValidationReportWriter : IValidationReportWriter
{
    public static readonly IReadOnlyList<string> Header = new[] { "RowNumber", "Column", "Severity", "Code", "Message", "OffendingValue" };

    public async Task WriteAsync(IAsyncEnumerable<ValidationReportRow> rows, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(destination);

        await using var writer = new StreamWriter(destination, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 65536, leaveOpen: true);
        using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture, leaveOpen: true);

        foreach (var column in Header)
        {
            csv.WriteField(column);
        }

        await csv.NextRecordAsync();

        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            csv.WriteField(row.RowNumber.ToString(CultureInfo.InvariantCulture));
            csv.WriteField(Neutralize(row.Column));
            csv.WriteField(row.Severity);
            csv.WriteField(row.Code);
            csv.WriteField(Neutralize(row.Message));
            csv.WriteField(Neutralize(row.Value));
            await csv.NextRecordAsync();
        }

        await csv.FlushAsync();
        await writer.FlushAsync();
    }

    /// <summary>Prefixes values that a spreadsheet would interpret as a formula.</summary>
    public static string Neutralize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }
}
