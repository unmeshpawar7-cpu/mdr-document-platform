using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Mdr.Application.Processing.Csv;

namespace Mdr.Infrastructure.Csv;

/// <summary>
/// Streams raw CSV records with CsvHelper's <see cref="CsvParser"/> (RFC 4180 quoting, multi-line fields).
/// Header handling is done by the application's header mapper, so the parser runs without a header record.
/// </summary>
public sealed class CsvHelperMdrReader : IMdrCsvReader
{
    private static readonly CsvConfiguration Configuration = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = false,
        BadDataFound = null,
        MissingFieldFound = null,
        IgnoreBlankLines = true,
        DetectColumnCountChanges = false,
    };

    public async IAsyncEnumerable<CsvRecord> ReadRecordsAsync(Stream content, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        // detectEncodingFromByteOrderMarks strips a UTF-8 BOM so it never leaks into the first header cell.
        using var reader = new StreamReader(content, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true, bufferSize: 65536, leaveOpen: true);
        using var parser = new CsvParser(reader, Configuration, leaveOpen: true);

        while (await parser.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = parser.Record ?? Array.Empty<string>();
            yield return new CsvRecord(parser.Row, fields);
        }
    }
}
