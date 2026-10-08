using FluentAssertions;
using Mdr.Domain.Common;
using Mdr.Domain.Documents;
using Mdr.Domain.Projects;
using Mdr.Domain.Validation;
using Mdr.UnitTests.TestDoubles;

namespace Mdr.UnitTests.Domain;

public sealed class ProjectAndDocumentTests
{
    [Theory]
    [InlineData("NWP-001", true)]
    [InlineData("nwp-001", false)]
    [InlineData("N-001", false)]
    [InlineData("NWP-01", false)]
    public void Project_code_format(string code, bool expected) => Project.IsValidCode(code).Should().Be(expected);

    [Fact]
    public void Project_create_normalises_the_code()
    {
        Project.Create(" nwp-001 ", "Northwind Water Plant", "Client", "u", TestData.Now).Code.Should().Be("NWP-001");
    }

    [Fact]
    public void Wbs_requires_parent_and_unique_codes()
    {
        var project = Project.Create("NWP-001", "Northwind Water Plant", "Client", "u", TestData.Now);
        project.Invoking(p => p.AddWbsElement("1.2", "Child")).Should().Throw<DomainException>().Which.Code.Should().Be("wbs_parent_missing");

        project.AddWbsElement("1", "Root");
        var child = project.AddWbsElement("1.2", "Child");
        child.Level.Should().Be(2);
        child.ParentCode.Should().Be("1");

        project.Invoking(p => p.AddWbsElement("1.2", "Again")).Should().Throw<DomainException>().Which.Code.Should().Be("wbs_code_duplicate");
        project.Invoking(p => p.AddWbsElement("1.x", "Bad")).Should().Throw<DomainException>().Which.Code.Should().Be("wbs_code_invalid");
    }

    private static MdrDocument NewDocument() => MdrDocument.Create(
        Guid.NewGuid(), Guid.NewGuid(), 2, "nwp-001-pun-mec-dwg-0012", "Pump GA", null, 6, 3, 3, 1, Guid.NewGuid(), 1, "a", null, DocumentStatus.Planned, TestData.Now);

    [Fact]
    public void Document_create_normalises_number_and_revision_and_rejects_zero_quantity()
    {
        var document = NewDocument();
        document.DocumentNumber.Should().Be("NWP-001-PUN-MEC-DWG-0012");
        document.Revision.Should().Be("A");

        var act = () => MdrDocument.Create(Guid.NewGuid(), Guid.NewGuid(), 2, "X", "T", null, 1, 1, 1, 1, Guid.NewGuid(), 0, "A", null, DocumentStatus.Planned, TestData.Now);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("document_quantity_invalid");
    }

    [Fact]
    public void Agreeing_confident_classification_does_not_need_review()
    {
        var document = NewDocument();
        var reasons = document.ApplyClassification(
            new AiClassification("MEC", "DWG", "Equipment", AiPriority.Medium, 0.92, Array.Empty<string>(), null, "keywords", "v1"),
            "MEC",
            "DWG",
            0.6,
            TestData.Now);

        reasons.Should().BeEmpty();
        document.NeedsReview.Should().BeFalse();
        document.AiConfidence.Should().Be(0.92);
        document.AiClassifiedAtUtc.Should().Be(TestData.Now);
    }

    [Fact]
    public void Disagreement_low_confidence_and_duplicates_flag_review()
    {
        var document = NewDocument();
        var reasons = document.ApplyClassification(
            new AiClassification("ELE", "SPE", null, AiPriority.High, 0.3, new[] { "description" }, Guid.NewGuid().ToString(), null, "v1"),
            "MEC",
            "DWG",
            0.6,
            TestData.Now);

        reasons.Should().BeEquivalentTo(new[]
        {
            ValidationCodes.AiDisciplineMismatch,
            ValidationCodes.AiDocumentTypeMismatch,
            ValidationCodes.AiLowConfidence,
            ValidationCodes.AiPotentialDuplicate,
        });
        document.NeedsReview.Should().BeTrue();
        document.AiDisciplineMismatch.Should().BeTrue();
        document.AiMissingMetadata.Should().Be("description");
    }

    [Fact]
    public void Missing_suggestion_is_not_a_mismatch()
    {
        var document = NewDocument();
        document.ApplyClassification(new AiClassification(null, null, null, null, 0.9, Array.Empty<string>(), null, null, "v1"), "MEC", "DWG", 0.6, TestData.Now);
        document.AiDisciplineMismatch.Should().BeFalse();
        document.NeedsReview.Should().BeFalse();
    }
}
