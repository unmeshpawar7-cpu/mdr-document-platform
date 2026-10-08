using System.Runtime.CompilerServices;
using System.Text;
using FluentAssertions;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Reporting;
using Mdr.Infrastructure.Csv;

namespace Mdr.UnitTests.Infrastructure;

public sealed class CsvTests
{
    private static async Task<List<CsvRecord>> ReadAll(string text, bool bom = false)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (bom)
        {
            bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray();
        }

        var records = new List<CsvRecord>();
        await foreach (var record in new CsvHelperMdrReader().ReadRecordsAsync(new MemoryStream(bytes), default))
        {
            records.Add(record);
        }

        return records;
    }

    [Fact]
    public async Task Reader_streams_records_with_spreadsheet_row_numbers()
    {
        var records = await ReadAll("A,B\n1,2\n3,4\n");
        records.Select(r => r.RowNumber).Should().Equal(1, 2, 3);
        records[2].Fields.Should().Equal("3", "4");
    }

    [Fact]
    public async Task Reader_strips_the_bom_and_handles_quotes_and_embedded_newlines()
    {
        var records = await ReadAll("Title,Description\r\n\"Pump, intake\",\"line one\nline two\"\r\n", bom: true);
        records[0].Fields[0].Should().Be("Title");
        records[1].Fields.Should().Equal("Pump, intake", "line one\nline two");
    }

    [Fact]
    public async Task Reader_tolerates_stray_quotes()
    {
        var records = await ReadAll("A,B\n12\" pipe,x\n");
        records.Should().HaveCount(2);
    }

    private static async IAsyncEnumerable<ValidationReportRow> Rows(IEnumerable<ValidationReportRow> rows, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return row;
        }
    }

    [Fact]
    public async Task Report_writer_emits_header_and_neutralises_formula_injection()
    {
        using var buffer = new MemoryStream();
        await new CsvValidationReportWriter().WriteAsync(
            Rows(new[]
            {
                new ValidationReportRow(2, "Title", "Error", "MDR001_REQUIRED_MISSING", "Title is required.", null),
                new ValidationReportRow(3, "Quantity", "Error", "MDR003_INVALID_INTEGER", "Quantity must be a whole number.", "=HYPERLINK(\"http://x\")"),
            }),
            buffer,
            default);

        var text = Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('﻿');
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be("RowNumber,Column,Severity,Code,Message,OffendingValue");
        lines.Should().HaveCount(3);
        lines[2].Should().Contain("'=HYPERLINK");
        buffer.ToArray().Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-5", "'-5")]
    [InlineData("@SUM", "'@SUM")]
    [InlineData("NWP-001", "NWP-001")]
    [InlineData(null, "")]
    public void Neutralize(string? input, string expected) => CsvValidationReportWriter.Neutralize(input).Should().Be(expected);
}
