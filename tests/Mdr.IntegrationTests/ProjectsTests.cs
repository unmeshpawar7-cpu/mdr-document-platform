using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Mdr.IntegrationTests.Infrastructure;

namespace Mdr.IntegrationTests;

public sealed class ProjectsTests : IClassFixture<MdrApiFactory>
{
    private readonly MdrApiFactory _factory;

    public ProjectsTests(MdrApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Create_returns_201_with_location_and_duplicate_returns_409()
    {
        using var client = await _factory.CreateClientForAsync("controller");
        var code = ApiClientExtensions.UniqueProjectCode();

        var created = await client.PostAsJsonAsync("/api/projects", new { code, name = "Southridge Filtration Works", client = "Southridge Water (fictional)" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();

        var fetched = await client.GetAsync(created.Headers.Location);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetched.JsonAsync()).Data().GetProperty("code").GetString().Should().Be(code);

        var duplicate = await client.PostAsJsonAsync("/api/projects", new { code, name = "Again", client = "X" });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.JsonAsync()).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("project_code_exists");
    }

    [Fact]
    public async Task Invalid_project_returns_400_envelope_with_field_errors()
    {
        using var client = await _factory.CreateClientForAsync("controller");
        var response = await client.PostAsJsonAsync("/api/projects", new { code = "bad", name = "", client = "X" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.JsonAsync();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).Should().Contain(new[] { "code", "name" });
    }

    [Fact]
    public async Task Wbs_bulk_add_builds_the_hierarchy_and_rejects_orphans_and_duplicates()
    {
        using var client = await _factory.CreateClientForAsync("controller");
        var id = await client.CreateProjectWithWbsAsync(ApiClientExtensions.UniqueProjectCode());

        var details = (await (await client.GetAsync($"/api/projects/{id}")).JsonAsync()).Data();
        details.GetProperty("wbsElements").GetArrayLength().Should().Be(ApiClientExtensions.WbsCodes.Length);

        var orphan = await client.PostAsJsonAsync($"/api/projects/{id}/wbs", new { elements = new[] { new { code = "8.1", name = "Orphan" } } });
        orphan.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var duplicate = await client.PostAsJsonAsync($"/api/projects/{id}/wbs", new { elements = new[] { new { code = "2.1", name = "Again" } } });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task List_is_paged_and_page_size_is_capped()
    {
        using var client = await _factory.CreateClientForAsync("controller");
        await client.CreateProjectWithWbsAsync(ApiClientExtensions.UniqueProjectCode());

        var data = (await (await client.GetAsync("/api/projects?page=1&pageSize=500")).JsonAsync()).Data();
        data.GetProperty("pageSize").GetInt32().Should().Be(100);
        data.GetProperty("totalCount").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("totalPages").GetInt32().Should().BeGreaterThan(0);
        data.GetProperty("items").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Unknown_project_returns_404()
    {
        using var client = await _factory.CreateClientForAsync("viewer");
        var response = await client.GetAsync($"/api/projects/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.JsonAsync()).GetProperty("success").GetBoolean().Should().BeFalse();
    }
}
