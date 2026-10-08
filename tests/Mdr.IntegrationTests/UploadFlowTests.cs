using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Mdr.Application.Common.Persistence;
using Mdr.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Mdr.IntegrationTests;

public sealed class UploadFlowTests : IClassFixture<MdrApiFactory>
{
    private readonly MdrApiFactory _factory;

    public UploadFlowTests(MdrApiFactory factory) => _factory = factory;

    private static byte[] Sample(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    private static async Task<Uri> AcceptedLocationAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        response.Headers.Location.Should().NotBeNull();
        var data = (await response.JsonAsync()).Data();
        data.GetProperty("status").GetString().Should().Be("Queued");
        return new Uri(response.Headers.Location!.OriginalString, UriKind.RelativeOrAbsolute);
    }

    private static JsonElement Counters(JsonElement status) => status.GetProperty("counters");

    [Fact]
    public async Task Sample_files_flow_from_upload_to_summary_and_report()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        using var admin = await _factory.CreateClientForAsync("admin");
        var projectId = await controller.CreateProjectWithWbsAsync("NWP-001", "Northwind Water Plant");

        // 1. Valid sample: 202 + Location, background processing, poll to completion.
        var location = await AcceptedLocationAsync(await controller.UploadAsync(projectId, Sample("mdr-valid.csv"), "mdr-valid.csv"));
        var status = await controller.WaitForCompletionAsync(location);
        status.GetProperty("status").GetString().Should().Be("Completed");
        Counters(status).GetProperty("totalRows").GetInt32().Should().Be(40);
        Counters(status).GetProperty("insertedRows").GetInt32().Should().Be(40);
        status.GetProperty("uploadedBy").GetString().Should().Be("controller");
        var validUploadId = status.GetProperty("id").GetGuid();

        var documents = (await (await controller.GetAsync($"/api/mdr/{validUploadId}/documents?pageSize=100")).JsonAsync()).Data();
        documents.GetProperty("totalCount").GetInt32().Should().Be(40);
        documents.GetProperty("items")[0].GetProperty("ai").GetProperty("classifierVersion").GetString().Should().Be("fake-classifier-1");

        var mechanical = (await (await controller.GetAsync($"/api/mdr/{validUploadId}/documents?discipline=mec")).JsonAsync()).Data();
        mechanical.GetProperty("totalCount").GetInt32().Should().Be(7);
        var needsReview = (await (await controller.GetAsync($"/api/mdr/{validUploadId}/documents?needsReview=true")).JsonAsync()).Data();
        needsReview.GetProperty("totalCount").GetInt32().Should().Be(0);

        var summary = (await (await controller.GetAsync($"/api/mdr/{validUploadId}/summary")).JsonAsync()).Data();
        summary.GetProperty("documentsByDiscipline").EnumerateArray().Sum(c => c.GetProperty("count").GetInt32()).Should().Be(40);
        summary.GetProperty("documentsByGateSet").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "STD").GetProperty("count").GetInt32().Should().Be(28);
        summary.GetProperty("ai").GetProperty("classifiedCount").GetInt32().Should().Be(40);
        summary.GetProperty("ai").GetProperty("disciplineAgreementRate").GetDouble().Should().Be(1.0);
        summary.GetProperty("processingDurationMs").ValueKind.Should().Be(JsonValueKind.Number);

        // 2. Error sample after the valid one: every rule fires, valid rows still persisted.
        var errorLocation = await AcceptedLocationAsync(await controller.UploadAsync(projectId, Sample("mdr-with-errors.csv"), "mdr-with-errors.csv"));
        var errorStatus = await controller.WaitForCompletionAsync(errorLocation);
        errorStatus.GetProperty("status").GetString().Should().Be("CompletedWithErrors");
        var counters = Counters(errorStatus);
        counters.GetProperty("totalRows").GetInt32().Should().Be(27);
        counters.GetProperty("validRows").GetInt32().Should().Be(6);
        counters.GetProperty("invalidRows").GetInt32().Should().Be(21);
        counters.GetProperty("duplicateRows").GetInt32().Should().Be(2);
        counters.GetProperty("insertedRows").GetInt32().Should().Be(6);
        var errorUploadId = errorStatus.GetProperty("id").GetGuid();

        var inFile = (await (await controller.GetAsync($"/api/mdr/{errorUploadId}/validation?code=MDR020_DUPLICATE_IN_FILE")).JsonAsync()).Data();
        inFile.GetProperty("totalCount").GetInt32().Should().Be(1);
        inFile.GetProperty("items")[0].GetProperty("rowNumber").GetInt32().Should().Be(14);

        var row15 = (await (await controller.GetAsync($"/api/mdr/{errorUploadId}/validation?row=15")).JsonAsync()).Data();
        row15.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()).Should().Contain("MDR021_DUPLICATE_EXISTING");

        var warnings = (await (await controller.GetAsync($"/api/mdr/{errorUploadId}/validation?severity=warning")).JsonAsync()).Data();
        warnings.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("code").GetString() == "MDR042_PLANNED_DATE_IN_PAST");

        var paged = (await (await controller.GetAsync($"/api/mdr/{errorUploadId}/validation?severity=error&page=2&pageSize=5")).JsonAsync()).Data();
        paged.GetProperty("page").GetInt32().Should().Be(2);
        paged.GetProperty("items").GetArrayLength().Should().Be(5);

        var errorSummary = (await (await controller.GetAsync($"/api/mdr/{errorUploadId}/summary")).JsonAsync()).Data();
        errorSummary.GetProperty("issuesBySeverity")[0].GetProperty("key").GetString().Should().Be("Error");
        errorSummary.GetProperty("issuesBySeverity")[0].GetProperty("count").GetInt32().Should().BeGreaterThan(20);

        // 3. CSV validation report download.
        var report = await controller.GetAsync($"/api/mdr/{errorUploadId}/report");
        report.StatusCode.Should().Be(HttpStatusCode.OK);
        report.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        var reportText = Encoding.UTF8.GetString(await report.Content.ReadAsByteArrayAsync()).TrimStart('﻿');
        reportText.Should().StartWith("RowNumber,Column,Severity,Code,Message,OffendingValue");
        reportText.Should().Contain("MDR021_DUPLICATE_EXISTING").And.Contain("MDR034_GATESET_NOT_ALLOWED");

        // 4. Same bytes again for the same project: 409.
        var duplicate = await controller.UploadAsync(projectId, Sample("mdr-valid.csv"), "renamed-copy.csv");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.JsonAsync()).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("upload_duplicate_file");

        // 5. History and audit trail.
        var history = (await (await controller.GetAsync($"/api/projects/{projectId}/uploads")).JsonAsync()).Data();
        history.GetProperty("totalCount").GetInt32().Should().Be(2);
        history.GetProperty("items")[0].GetProperty("id").GetGuid().Should().Be(errorUploadId, "history is newest first");

        var audit = (await (await admin.GetAsync($"/api/audit-logs?entityType=MdrUpload&entityId={errorUploadId}")).JsonAsync()).Data();
        audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString())
            .Should().Contain(new[] { "Upload.Queued", "Upload.Completed" });

        // 6. The duplicate lookup used by the pipeline translates to a single SQLite query (json_each).
        await using var scope = _factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var existing = await repository.GetExistingDocumentNumbersAsync(
            projectId,
            new[] { "NWP-001-PUN-MEC-DWG-0001", "NWP-001-PUN-MEC-DWG-0101", "NWP-001-PUN-MEC-DWG-9999" },
            CancellationToken.None);
        existing.Should().BeEquivalentTo(new[] { "NWP-001-PUN-MEC-DWG-0001", "NWP-001-PUN-MEC-DWG-0101" });
    }

    [Theory]
    [InlineData("mdr.txt", "DocumentNumber,Title\n", "MDR101_FILE_EXTENSION")]
    [InlineData("mdr.csv", "\n \n", "MDR104_HEADER_MISSING")]
    public async Task Bad_files_are_rejected_synchronously_with_400_envelope(string fileName, string content, string code)
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var projectId = await controller.CreateProjectWithWbsAsync(ApiClientExtensions.UniqueProjectCode());

        var response = await controller.UploadAsync(projectId, Encoding.UTF8.GetBytes(content), fileName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.JsonAsync();
        body.GetProperty("success").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("code").GetString()).Should().Contain(code);
    }

    [Fact]
    public async Task Non_utf8_file_is_rejected()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var projectId = await controller.CreateProjectWithWbsAsync(ApiClientExtensions.UniqueProjectCode());

        var response = await controller.UploadAsync(projectId, new byte[] { 0x44, 0x6F, 0x63, 0xE9, 0x0A });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.JsonAsync()).GetProperty("errors")[0].GetProperty("code").GetString().Should().Be("MDR103_FILE_ENCODING");
    }

    [Fact]
    public async Task Missing_columns_are_detected_by_the_background_processor()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var projectId = await controller.CreateProjectWithWbsAsync(ApiClientExtensions.UniqueProjectCode());

        var location = await AcceptedLocationAsync(await controller.UploadAsync(projectId, Encoding.UTF8.GetBytes("DocumentNumber,Title\nA,B\n")));
        var status = await controller.WaitForCompletionAsync(location);

        status.GetProperty("status").GetString().Should().Be("Failed");
        status.GetProperty("failureReason").GetString().Should().Contain("Header");
        var issues = (await (await controller.GetAsync($"/api/mdr/{status.GetProperty("id").GetGuid()}/validation?code=MDR105_REQUIRED_COLUMN_MISSING&pageSize=50")).JsonAsync()).Data();
        issues.GetProperty("totalCount").GetInt32().Should().Be(7);
    }

    [Fact]
    public async Task Classifier_outage_degrades_to_info_issues_and_still_completes()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var code = ApiClientExtensions.UniqueProjectCode();
        var projectId = await controller.CreateProjectWithWbsAsync(code);
        _factory.Classifier.Available = false;
        try
        {
            var location = await AcceptedLocationAsync(await controller.UploadAsync(projectId, ApiClientExtensions.GenerateCsv(code, 12)));
            var status = await controller.WaitForCompletionAsync(location);

            status.GetProperty("status").GetString().Should().Be("Completed");
            var uploadId = status.GetProperty("id").GetGuid();
            var info = (await (await controller.GetAsync($"/api/mdr/{uploadId}/validation?code=MDR050_AI_UNAVAILABLE")).JsonAsync()).Data();
            info.GetProperty("totalCount").GetInt32().Should().Be(1);
            info.GetProperty("items")[0].GetProperty("severity").GetString().Should().Be("Info");

            var summary = (await (await controller.GetAsync($"/api/mdr/{uploadId}/summary")).JsonAsync()).Data();
            summary.GetProperty("ai").GetProperty("classifiedCount").GetInt32().Should().Be(0);
            summary.GetProperty("ai").GetProperty("unavailableBatchCount").GetInt32().Should().Be(1);
        }
        finally
        {
            _factory.Classifier.Available = true;
        }
    }

    [Fact]
    public async Task Viewer_cannot_upload_and_unknown_resources_return_404()
    {
        using var viewer = await _factory.CreateClientForAsync("viewer");
        using var controller = await _factory.CreateClientForAsync("controller");

        (await viewer.UploadAsync(Guid.NewGuid(), Encoding.UTF8.GetBytes("a\n"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await controller.UploadAsync(Guid.NewGuid(), ApiClientExtensions.GenerateCsv("ZZZ-999", 1))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/mdr/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await viewer.GetAsync($"/api/mdr/{Guid.NewGuid()}/summary")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invalid_severity_filter_returns_400()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var code = ApiClientExtensions.UniqueProjectCode();
        var projectId = await controller.CreateProjectWithWbsAsync(code);
        var location = await AcceptedLocationAsync(await controller.UploadAsync(projectId, ApiClientExtensions.GenerateCsv(code, 2)));
        var status = await controller.WaitForCompletionAsync(location);

        var response = await controller.GetAsync($"/api/mdr/{status.GetProperty("id").GetGuid()}/validation?severity=fatal");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public sealed class UploadRateLimitTests : IClassFixture<LowRateLimitFactory>
{
    private readonly LowRateLimitFactory _factory;

    public UploadRateLimitTests(LowRateLimitFactory factory) => _factory = factory;

    [Fact]
    public async Task Uploads_are_rate_limited_per_user()
    {
        using var controller = await _factory.CreateClientForAsync("controller");
        var code = ApiClientExtensions.UniqueProjectCode();
        var projectId = await controller.CreateProjectWithWbsAsync(code);

        (await controller.UploadAsync(projectId, ApiClientExtensions.GenerateCsv(code, 1, 1))).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await controller.UploadAsync(projectId, ApiClientExtensions.GenerateCsv(code, 1, 2))).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var limited = await controller.UploadAsync(projectId, ApiClientExtensions.GenerateCsv(code, 1, 3));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await limited.JsonAsync()).GetProperty("success").GetBoolean().Should().BeFalse();
    }
}
