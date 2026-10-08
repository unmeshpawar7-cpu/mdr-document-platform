using FluentAssertions;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Domain;

public sealed class ValidationCodeCatalogueTests
{
    [Fact]
    public void Codes_are_unique()
    {
        ValidationCodes.All.Select(c => c.Code).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Ai_codes_are_never_errors()
    {
        ValidationCodes.All.Where(c => c.Code.StartsWith("MDR05", StringComparison.Ordinal))
            .Should().OnlyContain(c => c.Severity == IssueSeverity.Info);
    }

    [Fact]
    public void Every_code_is_documented_in_docs_validation_codes_md()
    {
        var doc = File.ReadAllText(Path.Combine(TestData.RepoRoot(), "docs", "validation-codes.md"));
        var undocumented = ValidationCodes.All.Select(c => c.Code).Where(code => !doc.Contains(code, StringComparison.Ordinal)).ToList();
        undocumented.Should().BeEmpty("docs/validation-codes.md must list every code in ValidationCodes");
    }
}
