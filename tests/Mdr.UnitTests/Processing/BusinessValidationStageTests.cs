using FluentAssertions;
using Mdr.Application.Processing.Stages;
using Mdr.Domain.Documents;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Processing;

public sealed class BusinessValidationStageTests
{
    private readonly BusinessValidationStage _stage = new();
    private readonly BusinessValidationContext _context = TestData.Context(TestData.CreateProject());

    [Fact]
    public void Valid_row_resolves_reference_data_and_wbs()
    {
        var result = _stage.Validate(TestData.ValidRow(), _context);

        result.Findings.Should().BeEmpty();
        result.Resolved.Should().NotBeNull();
        result.Resolved!.Discipline.Code.Should().Be("MEC");
        result.Resolved.WbsElementId.Should().Be(_context.WbsElements["2.1"]);
        result.Resolved.Status.Should().Be(DocumentStatus.Planned);
    }

    public static IEnumerable<object[]> InvalidRows()
    {
        var valid = TestData.ValidRow();
        yield return new object[] { valid with { DisciplineCode = "XYZ", DocumentNumber = "NWP-001-PUN-XYZ-DWG-0012" }, ValidationCodes.UnknownDiscipline };
        yield return new object[] { valid with { DocumentTypeCode = "MAN", DocumentNumber = "NWP-001-PUN-MEC-MAN-0012" }, ValidationCodes.UnknownDocumentType };
        yield return new object[] { valid with { OfficeCode = "SYD", DocumentNumber = "NWP-001-SYD-MEC-DWG-0012" }, ValidationCodes.UnknownOffice };
        yield return new object[] { valid with { GateSetCode = "G9" }, ValidationCodes.UnknownGateSet };
        yield return new object[] { valid with { GateSetCode = "REG" }, ValidationCodes.GateSetNotAllowed };
        yield return new object[] { valid with { WbsCode = "9.9" }, ValidationCodes.UnknownWbs };
        yield return new object[] { valid with { Quantity = 0 }, ValidationCodes.QuantityNotPositive };
        yield return new object[] { valid with { Revision = "A-1" }, ValidationCodes.InvalidRevision };
        yield return new object[] { valid with { Status = "Approved" }, ValidationCodes.InvalidStatus };
        yield return new object[] { valid with { DocumentNumber = "NWP-001-PUN-MEC-DWG-12" }, ValidationCodes.DocumentNumberFormat };
        yield return new object[] { valid with { DocumentNumber = "SRF-002-PUN-MEC-DWG-0012" }, ValidationCodes.DocumentNumberProjectMismatch };
        yield return new object[] { valid with { DocumentNumber = "NWP-001-HOU-MEC-DWG-0012" }, ValidationCodes.DocumentNumberOfficeMismatch };
        yield return new object[] { valid with { DocumentNumber = "NWP-001-PUN-ELE-DWG-0012" }, ValidationCodes.DocumentNumberDisciplineMismatch };
        yield return new object[] { valid with { DocumentNumber = "NWP-001-PUN-MEC-SPE-0012" }, ValidationCodes.DocumentNumberTypeMismatch };
    }

    [Theory]
    [MemberData(nameof(InvalidRows))]
    public void Each_rule_reports_exactly_its_code(ParsedMdrRow row, string expectedCode)
    {
        var result = _stage.Validate(row, _context);

        result.Findings.Should().ContainSingle().Which.Code.Should().Be(expectedCode);
        result.Resolved.Should().BeNull();
    }

    [Fact]
    public void Past_planned_date_is_only_a_warning()
    {
        var result = _stage.Validate(TestData.ValidRow() with { PlannedDate = new DateOnly(2020, 1, 15) }, _context);

        result.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.PlannedDateInPast && f.Severity == IssueSeverity.Warning);
        result.HasErrors.Should().BeFalse();
        result.Resolved.Should().NotBeNull();
    }

    [Theory]
    [InlineData("In Progress", DocumentStatus.InProgress)]
    [InlineData("on_hold", DocumentStatus.OnHold)]
    [InlineData("ISSUED", DocumentStatus.Issued)]
    public void Status_names_are_matched_leniently(string text, DocumentStatus expected)
    {
        BusinessValidationStage.TryParseStatus(text, out var status).Should().BeTrue();
        status.Should().Be(expected);
    }

    [Fact]
    public void Numeric_status_is_rejected()
    {
        BusinessValidationStage.TryParseStatus("2", out _).Should().BeFalse();
    }

    [Fact]
    public void Rows_missing_required_values_are_never_resolved()
    {
        var result = _stage.Validate(TestData.ValidRow() with { Title = null }, _context);
        result.Findings.Should().BeEmpty("schema validation already reported the missing title");
        result.Resolved.Should().BeNull();
    }
}
