using FluentAssertions;
using FluentValidation;
using Mdr.Application.Common.Exceptions;
using Mdr.Application.Common.Paging;
using Mdr.Application.Projects;
using Mdr.Domain.Common;
using Mdr.UnitTests.TestDoubles;
using NSubstitute;

namespace Mdr.UnitTests.Uploads;

public sealed class ProjectServiceTests
{
    private readonly FakeProjectRepository _projects = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingAuditTrail _audit = new();

    private ProjectService CreateService() => new(
        _projects,
        Substitute.For<IProjectQueries>(),
        _unitOfWork,
        new CreateProjectRequestValidator(),
        new AddWbsElementsRequestValidator(),
        new FixedCurrentUser(),
        _audit,
        new TestClock(TestData.Now));

    [Fact]
    public async Task Creates_project_with_normalised_code()
    {
        var project = await CreateService().CreateAsync(new CreateProjectRequest("nwp-001", "Northwind Water Plant", "Northwind Utilities"), default);
        project.Code.Should().Be("NWP-001");
        project.CreatedBy.Should().Be("controller");
        _audit.Entries.Should().ContainSingle(e => e.Action == "Project.Created");
    }

    [Fact]
    public async Task Invalid_request_throws_validation_exception()
    {
        var act = () => CreateService().CreateAsync(new CreateProjectRequest("NWP1", "", "Client"), default);
        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.Select(e => e.PropertyName);
        errors.Should().Contain(new[] { "Code", "Name" });
    }

    [Fact]
    public async Task Duplicate_code_is_a_conflict()
    {
        _projects.Projects.Add(TestData.CreateProject());
        var act = () => CreateService().CreateAsync(new CreateProjectRequest("NWP-001", "Another", "Client"), default);
        await act.Should().ThrowAsync<ConflictException>();
    }

    [Fact]
    public async Task Bulk_wbs_creates_parents_before_children_regardless_of_order()
    {
        var created = await CreateService().CreateAsync(new CreateProjectRequest("SRF-002", "Southridge Filtration", "Client"), default);
        var request = new AddWbsElementsRequest(new[]
        {
            new WbsElementInput("1.1.1", "Pumps"),
            new WbsElementInput("1", "Intake"),
            new WbsElementInput("1.1", "Pump station"),
        });

        var elements = await CreateService().AddWbsElementsAsync(created.Id, request, default);

        elements.Select(e => e.Code).Should().Equal("1", "1.1", "1.1.1");
        _projects.Projects.Single().WbsElements.Should().HaveCount(3);
    }

    [Fact]
    public async Task Existing_wbs_codes_conflict_and_missing_parents_are_domain_errors()
    {
        var project = TestData.CreateProject();
        _projects.Projects.Add(project);

        var conflict = () => CreateService().AddWbsElementsAsync(project.Id, new AddWbsElementsRequest(new[] { new WbsElementInput("1", "Again") }), default);
        await conflict.Should().ThrowAsync<ConflictException>();

        var orphan = () => CreateService().AddWbsElementsAsync(project.Id, new AddWbsElementsRequest(new[] { new WbsElementInput("7.1", "Orphan") }), default);
        (await orphan.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be("wbs_parent_missing");
    }

    [Fact]
    public void Wbs_validator_rejects_duplicates_in_request()
    {
        var result = new AddWbsElementsRequestValidator().Validate(new AddWbsElementsRequest(new[]
        {
            new WbsElementInput("1", "A"),
            new WbsElementInput("1", "B"),
        }));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Page_request_is_clamped()
    {
        var page = new PageRequest(0, 1000);
        page.Page.Should().Be(1);
        page.PageSize.Should().Be(PageRequest.MaxPageSize);
        new PagedResult<int>(new[] { 1 }, 1, 20, 41).TotalPages.Should().Be(3);
    }
}
