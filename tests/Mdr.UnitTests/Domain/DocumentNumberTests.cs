using FluentAssertions;
using Mdr.Domain.Documents;

namespace Mdr.UnitTests.Domain;

public sealed class DocumentNumberTests
{
    [Theory]
    [InlineData("NWP-001-PUN-MEC-DWG-0012", "NWP-001", "PUN", "MEC", "DWG", 12)]
    [InlineData("  nwp-001-hou-ins-ds-0001 ", "NWP-001", "HOU", "INS", "DS", 1)]
    [InlineData("ABCDE-999-LDN-STR-CAL-9999", "ABCDE-999", "LDN", "STR", "CAL", 9999)]
    public void TryParse_accepts_valid_numbers_and_normalises_case(string raw, string project, string office, string discipline, string type, int sequence)
    {
        DocumentNumber.TryParse(raw, out var parsed).Should().BeTrue();
        parsed!.ProjectCode.Should().Be(project);
        parsed.OfficeCode.Should().Be(office);
        parsed.DisciplineCode.Should().Be(discipline);
        parsed.DocumentTypeCode.Should().Be(type);
        parsed.Sequence.Should().Be(sequence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NWP001-PUN-MEC-DWG-0012")]
    [InlineData("NWP-001-PUN-MEC-DWG-12")]
    [InlineData("NWP-001-PUN-MEC-DWG-0000")]
    [InlineData("NWP-001-PUNE-MEC-DWG-0012")]
    [InlineData("NWP-001-PUN-MEC-DRAW-0012")]
    [InlineData("NWP-001-PUN-MEC-DWG-0012-X")]
    public void TryParse_rejects_malformed_numbers(string? raw)
    {
        DocumentNumber.TryParse(raw, out var parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Fact]
    public void Value_round_trips_with_zero_padded_sequence()
    {
        var number = new DocumentNumber("NWP-001", "PUN", "MEC", "DWG", 7);
        number.Value.Should().Be("NWP-001-PUN-MEC-DWG-0007");
        number.ToString().Should().Be(number.Value);
    }
}
