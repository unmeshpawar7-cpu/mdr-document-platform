using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Mdr.IntegrationTests.Infrastructure;

namespace Mdr.IntegrationTests;

public sealed class AuthAndPlatformTests : IClassFixture<MdrApiFactory>
{
    private readonly MdrApiFactory _factory;

    public AuthAndPlatformTests(MdrApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Valid_credentials_return_a_bearer_token_with_roles()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userName = "Controller", password = MdrApiFactory.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = (await response.JsonAsync()).Data();
        data.GetProperty("tokenType").GetString().Should().Be("Bearer");
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        data.GetProperty("roles")[0].GetString().Should().Be("DocumentController");
    }

    [Fact]
    public async Task Wrong_password_returns_401_envelope()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userName = "controller", password = "nope" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.JsonAsync();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Empty_credentials_return_400_with_field_errors()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/token", new { userName = "", password = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Anonymous_requests_get_401_envelope()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/projects");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.JsonAsync()).GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Viewer_can_read_but_not_write()
    {
        using var viewer = await _factory.CreateClientForAsync("viewer");

        (await viewer.GetAsync("/api/projects")).StatusCode.Should().Be(HttpStatusCode.OK);
        var create = await viewer.PostAsJsonAsync("/api/projects", new { code = "VWR-001", name = "Viewer project", client = "X" });
        create.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await create.JsonAsync()).GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Audit_logs_are_admin_only()
    {
        using var viewer = await _factory.CreateClientForAsync("viewer");
        using var admin = await _factory.CreateClientForAsync("admin");

        (await viewer.GetAsync("/api/audit-logs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/audit-logs")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reference_data_lists_seeded_vocabulary()
    {
        using var viewer = await _factory.CreateClientForAsync("viewer");
        var data = (await (await viewer.GetAsync("/api/reference-data")).JsonAsync()).Data();

        data.GetProperty("disciplines").GetArrayLength().Should().Be(8);
        data.GetProperty("documentTypes").GetArrayLength().Should().Be(8);
        data.GetProperty("offices").GetArrayLength().Should().Be(3);
        var gateSets = data.GetProperty("gateSets").EnumerateArray().ToList();
        gateSets.Select(g => g.GetProperty("code").GetString()).Should().BeEquivalentTo(new[] { "INFO", "PROC", "REG", "STD" });
        gateSets.Single(g => g.GetProperty("code").GetString() == "STD").GetProperty("stages").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Health_endpoints_respond_and_correlation_id_is_echoed()
    {
        using var client = _factory.CreateClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "it-correlation-123");
        var response = await client.SendAsync(request);
        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle().Which.Should().Be("it-correlation-123");
    }
}
