namespace Mdr.Application.Processing.Csv;

/// <summary>One raw CSV record. <see cref="RowNumber"/> is 1-based with the header as row 1.</summary>
public sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Fields)
{
    public bool IsBlank => Fields.All(string.IsNullOrWhiteSpace);
}

/// <summary>Streams CSV records (header first) without loading the file into memory.</summary>
public interface IMdrCsvReader
{
    IAsyncEnumerable<CsvRecord> ReadRecordsAsync(Stream content, CancellationToken cancellationToken);
}
