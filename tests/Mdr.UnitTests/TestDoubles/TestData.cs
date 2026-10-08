using Mdr.Application.Processing.Csv;
using Mdr.Application.Processing.Stages;
using Mdr.Application.ReferenceData;
using Mdr.Domain.Projects;
using Mdr.Infrastructure.Persistence.Seed;

namespace Mdr.UnitTests.TestDoubles;

public static class TestData
{
    public static readonly DateTime Now = new(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

    public static readonly string[] WbsCodes = { "1", "1.1", "2", "2.1", "2.2", "3", "3.1", "3.2", "3.3", "4", "4.1", "4.2", "5", "5.1", "5.2" };

    public static readonly string[] Header =
    {
        "DocumentNumber", "Title", "Description", "Discipline", "DocumentType", "WBS",
        "GateSet", "Office", "Quantity", "Revision", "PlannedDate", "Status",
    };

    public static ReferenceDataSnapshot Snapshot { get; } = new(
        ReferenceDataSeed.Disciplines.Select(d => new ReferenceItem(d.Id, d.Code, d.Name)),
        ReferenceDataSeed.DocumentTypes.Select(d => new ReferenceItem(d.Id, d.Code, d.Name)),
        ReferenceDataSeed.Offices.Select(o => new OfficeItem(o.Id, o.Code, o.Name, o.City, o.Country)),
        ReferenceDataSeed.GateSets.Select(g => new GateSetItem(g.Id, g.Code, g.Name, g.GetStages(), g.Description)),
        ReferenceDataSeed.DocumentTypeGateSets.Select(r => new GateSetRule(r.DocumentTypeId, r.GateSetId)));

    public static Project CreateProject(string code = "NWP-001")
    {
        var project = Project.Create(code, "Northwind Water Plant", "Northwind Utilities (fictional)", "tester", Now);
        foreach (var wbs in WbsCodes)
        {
            project.AddWbsElement(wbs, $"WBS {wbs}");
        }

        return project;
    }

    public static BusinessValidationContext Context(Project project) =>
        new(project.Code, Snapshot, project.WbsElements.ToDictionary(w => w.Code, w => w.Id), DateOnly.FromDateTime(Now));

    public static ParsedMdrRow ValidRow(int rowNumber = 2) => new()
    {
        RowNumber = rowNumber,
        DocumentNumber = "NWP-001-PUN-MEC-DWG-0012",
        Title = "Intake pump general arrangement",
        Description = "General arrangement of intake pumps",
        DisciplineCode = "MEC",
        DocumentTypeCode = "DWG",
        WbsCode = "2.1",
        GateSetCode = "STD",
        OfficeCode = "PUN",
        Quantity = 2,
        Revision = "A",
        PlannedDate = new DateOnly(2027, 3, 1),
        Status = "Planned",
    };

    public static HeaderMapping DefaultMapping() => new HeaderMapper().Map(Header);

    /// <summary>Builds a CSV line for the default header, overriding selected columns.</summary>
    public static string[] Fields(params (string Column, string Value)[] overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DocumentNumber"] = "NWP-001-PUN-MEC-DWG-0012",
            ["Title"] = "Intake pump general arrangement",
            ["Description"] = "General arrangement of intake pumps",
            ["Discipline"] = "MEC",
            ["DocumentType"] = "DWG",
            ["WBS"] = "2.1",
            ["GateSet"] = "STD",
            ["Office"] = "PUN",
            ["Quantity"] = "2",
            ["Revision"] = "A",
            ["PlannedDate"] = "2027-03-01",
            ["Status"] = "Planned",
        };
        foreach (var (column, value) in overrides)
        {
            values[column] = value;
        }

        return Header.Select(h => values[h]).ToArray();
    }

    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Mdr.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Mdr.sln) not found.");
    }

    public static string SamplePath(string fileName) => Path.Combine(RepoRoot(), "samples", fileName);
}
