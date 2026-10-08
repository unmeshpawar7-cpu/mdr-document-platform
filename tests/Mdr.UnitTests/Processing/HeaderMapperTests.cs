using FluentAssertions;
using Mdr.Application.Processing.Csv;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Processing;

public sealed class HeaderMapperTests
{
    private readonly HeaderMapper _mapper = new();

    [Fact]
    public void Maps_canonical_header()
    {
        var mapping = _mapper.Map(TestData.Header);
        mapping.IsUsable.Should().BeTrue();
        mapping.Findings.Should().BeEmpty();
        mapping.ColumnIndexes[MdrColumns.Quantity].Should().Be(8);
    }

    [Fact]
    public void Matching_ignores_case_whitespace_punctuation_bom_and_accepts_aliases()
    {
        var headers = new[] { "﻿ document number ", "TITLE", "doc_type", "Discipline Code", "wbs-code", "Gate Set", "office", "Qty", "Rev." };
        var mapping = _mapper.Map(headers);

        mapping.IsUsable.Should().BeTrue();
        mapping.ColumnIndexes.Should().ContainKeys(MdrColumns.DocumentNumber, MdrColumns.DocumentType, MdrColumns.Wbs, MdrColumns.Revision);
        mapping.ColumnIndexes[MdrColumns.DocumentType].Should().Be(2);
    }

    [Fact]
    public void Missing_required_column_is_an_error()
    {
        var headers = TestData.Header.Where(h => h != "GateSet").ToArray();
        var mapping = _mapper.Map(headers);
        mapping.IsUsable.Should().BeFalse();
        mapping.Findings.Should().ContainSingle(f => f.Code == ValidationCodes.RequiredColumnMissing && f.Value == "GateSet");
    }

    [Fact]
    public void Duplicate_columns_are_errors_and_unknown_columns_are_warnings()
    {
        var headers = TestData.Header.Concat(new[] { "Doc No", "Remarks" }).ToArray();
        var mapping = _mapper.Map(headers);

        mapping.Findings.Should().Contain(f => f.Code == ValidationCodes.DuplicateColumn && f.Severity == IssueSeverity.Error);
        mapping.Findings.Should().Contain(f => f.Code == ValidationCodes.UnknownColumn && f.Severity == IssueSeverity.Warning);
        mapping.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void A_data_row_in_place_of_the_header_reports_header_missing()
    {
        var mapping = _mapper.Map(new[] { "NWP-001-PUN-MEC-DWG-0001", "Pump GA", "MEC" });
        mapping.Findings.Should().ContainSingle().Which.Code.Should().Be(ValidationCodes.HeaderMissing);
    }

    [Fact]
    public void Aliases_do_not_collide()
    {
        var normalized = MdrColumns.All.SelectMany(c => c.Aliases.Append(c.Name).Select(MdrColumns.NormalizeHeader)).ToList();
        normalized.Should().OnlyHaveUniqueItems();
    }
}
