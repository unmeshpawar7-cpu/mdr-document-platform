using FluentAssertions;
using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Processing;

public sealed class SchemaValidationStageTests
{
    private readonly SchemaValidationStage _stage = new();
    private readonly HeaderMapping _mapping = TestData.DefaultMapping();

    private SchemaValidationResult Validate(params (string Column, string Value)[] overrides) =>
        _stage.Validate(new CsvRecord(5, TestData.Fields(overrides)), _mapping);

    [Fact]
    public void Valid_row_is_parsed_trimmed_and_upper_cased()
    {
        var result = Validate(("Discipline", " mec "), ("DocumentNumber", "nwp-001-pun-mec-dwg-0012"), ("Revision", "b"));

        result.HasErrors.Should().BeFalse();
        result.Row.RowNumber.Should().Be(5);
        result.Row.DisciplineCode.Should().Be("MEC");
        result.Row.DocumentNumber.Should().Be("NWP-001-PUN-MEC-DWG-0012");
        result.Row.Revision.Should().Be("B");
        result.Row.Quantity.Should().Be(2);
        result.Row.PlannedDate.Should().Be(new DateOnly(2027, 3, 1));
    }

    [Fact]
    public void Missing_required_value_is_reported_and_passed_on_as_null()
    {
        var result = Validate(("Title", "   "));
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.RequiredMissing && f.Column == MdrColumns.Title);
        result.Row.Title.Should().BeNull();
    }

    [Fact]
    public void Optional_values_may_be_empty()
    {
        var result = Validate(("Description", ""), ("PlannedDate", ""), ("Status", ""));
        result.Findings.Should().BeEmpty();
        result.Row.PlannedDate.Should().BeNull();
    }

    [Fact]
    public void Over_long_value_is_reported()
    {
        var result = Validate(("Title", new string('x', 251)));
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.MaxLengthExceeded);
        result.Row.Title.Should().BeNull();
    }

    [Theory]
    [InlineData("two")]
    [InlineData("1.5")]
    [InlineData("1,000")]
    public void Non_integer_quantity_is_reported(string quantity)
    {
        Validate(("Quantity", quantity)).Findings.Should().ContainSingle(f => f.Code == ValidationCodes.InvalidInteger);
    }

    [Fact]
    public void Negative_quantity_parses_so_business_rules_can_reject_it()
    {
        var result = Validate(("Quantity", "-3"));
        result.HasErrors.Should().BeFalse();
        result.Row.Quantity.Should().Be(-3);
    }

    [Theory]
    [InlineData("15/03/2027")]
    [InlineData("2027-13-01")]
    [InlineData("March 2027")]
    public void Non_iso_date_is_reported(string date)
    {
        Validate(("PlannedDate", date)).Findings.Should().ContainSingle(f => f.Code == ValidationCodes.InvalidDate);
    }

    [Fact]
    public void Extra_fields_are_reported_as_malformed()
    {
        var fields = TestData.Fields().Append("extra").ToArray();
        var result = _stage.Validate(new CsvRecord(3, fields), _mapping);
        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.MalformedRow);
    }

    [Fact]
    public void Short_rows_treat_missing_trailing_fields_as_empty()
    {
        var fields = TestData.Fields().Take(10).ToArray();
        var result = _stage.Validate(new CsvRecord(3, fields), _mapping);
        result.HasErrors.Should().BeFalse();
        result.Row.Status.Should().BeNull();
    }
}
