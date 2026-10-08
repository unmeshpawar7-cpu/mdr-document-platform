using System.Text;
using FluentAssertions;
using Mdr.Application.Options;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Validation;
using Microsoft.Extensions.Options;

namespace Mdr.UnitTests.Processing;

public sealed class FileValidationStageTests
{
    private readonly FileValidationStage _stage = new(Options.Create(new MdrProcessingOptions { MaxFileSizeBytes = 1024 }));

    private static Stream Bytes(params byte[] bytes) => new MemoryStream(bytes);

    private static Stream Text(string text, bool bom = false)
    {
        var body = Encoding.UTF8.GetBytes(text);
        return new MemoryStream(bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray() : body);
    }

    [Theory]
    [InlineData("mdr.xlsx", 10, ValidationCodes.FileExtension)]
    [InlineData("mdr", 10, ValidationCodes.FileExtension)]
    [InlineData("mdr.csv", 0, ValidationCodes.FileEmpty)]
    [InlineData("mdr.csv", 2048, ValidationCodes.FileTooLarge)]
    public void Metadata_checks(string name, long length, string expectedCode)
    {
        _stage.ValidateMetadata(name, length).Should().ContainSingle(f => f.Code == expectedCode);
    }

    [Fact]
    public void Metadata_accepts_csv_case_insensitively()
    {
        _stage.ValidateMetadata("MDR.CSV", 100).Should().BeEmpty();
    }

    [Fact]
    public async Task Utf8_with_and_without_bom_is_accepted()
    {
        (await _stage.ValidateContentAsync(Text("DocumentNumber,Title\nA,B\n"), default)).Should().BeEmpty();
        (await _stage.ValidateContentAsync(Text("DocumentNumber,Title\r\nA,B\r\n", bom: true), default)).Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_utf8_is_rejected()
    {
        // 0xE9 is 'e acute' in Windows-1252 and an invalid lone byte in UTF-8.
        var findings = await _stage.ValidateContentAsync(Bytes(0x41, 0x2C, 0xE9, 0x0A), default);
        findings.Should().ContainSingle(f => f.Code == ValidationCodes.FileEncoding);
    }

    [Fact]
    public async Task Utf16_bom_is_rejected()
    {
        var findings = await _stage.ValidateContentAsync(Bytes(0xFF, 0xFE, 0x41, 0x00), default);
        findings.Should().ContainSingle(f => f.Code == ValidationCodes.FileEncoding);
    }

    [Fact]
    public async Task Binary_content_is_rejected()
    {
        var findings = await _stage.ValidateContentAsync(Bytes(0x41, 0x00, 0x42), default);
        findings.Should().ContainSingle(f => f.Code == ValidationCodes.FileEncoding);
    }

    [Fact]
    public async Task Empty_and_whitespace_only_content_is_rejected()
    {
        (await _stage.ValidateContentAsync(Bytes(), default)).Should().ContainSingle(f => f.Code == ValidationCodes.FileEmpty);
        (await _stage.ValidateContentAsync(Text("\n  \r\n\n"), default)).Should().ContainSingle(f => f.Code == ValidationCodes.HeaderMissing);
    }

    [Fact]
    public async Task Header_after_leading_blank_lines_is_found()
    {
        (await _stage.ValidateContentAsync(Text("\n\nDocumentNumber,Title"), default)).Should().BeEmpty();
    }
}
