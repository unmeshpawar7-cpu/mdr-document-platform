using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;

namespace Mdr.IntegrationTests.Infrastructure;

public static class ApiClientExtensions
{
    public static readonly string[] WbsCodes = { "1", "1.1", "2", "2.1", "2.2", "3", "3.1", "3.2", "3.3", "4", "4.1", "4.2", "5", "5.1", "5.2" };

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    public static JsonElement Data(this JsonElement body) => body.GetProperty("data");

    /// <summary>Creates a project with the standard WBS used by the samples and returns its id.</summary>
    public static async Task<Guid> CreateProjectWithWbsAsync(this HttpClient client, string code, string name = "Integration test project")
    {
        var created = await client.PostAsJsonAsync("/api/projects", new { code, name, client = "Fictional Utilities Ltd" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.JsonAsync()).Data().GetProperty("id").GetGuid();

        var wbs = await client.PostAsJsonAsync($"/api/projects/{id}/wbs", new
        {
            elements = WbsCodes.Select(c => new { code = c, name = $"WBS {c}" }).ToArray(),
        });
        wbs.StatusCode.Should().Be(HttpStatusCode.Created);
        return id;
    }

    public static Task<HttpResponseMessage> UploadAsync(this HttpClient client, Guid projectId, byte[] content, string fileName = "mdr.csv")
    {
        var form = new MultipartFormDataContent();
        form.Add(new StringContent(projectId.ToString()), "projectId");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", fileName);
        return client.PostAsync("/api/mdr/upload", form);
    }

    /// <summary>Polls the status resource until processing reaches a terminal state.</summary>
    public static async Task<JsonElement> WaitForCompletionAsync(this HttpClient client, Uri location)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var response = await client.GetAsync(location);
            response.EnsureSuccessStatusCode();
            var data = (await response.JsonAsync()).Data();
            var status = data.GetProperty("status").GetString();
            if (status is "Completed" or "CompletedWithErrors" or "Failed")
            {
                return data;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Upload still {status} after 60 seconds.");
            }

            await Task.Delay(100);
        }
    }

    /// <summary>Builds a valid CSV for any project code (sequence numbers start at <paramref name="firstSequence"/>).</summary>
    public static byte[] GenerateCsv(string projectCode, int rows, int firstSequence = 1)
    {
        var builder = new StringBuilder("DocumentNumber,Title,Description,Discipline,DocumentType,WBS,GateSet,Office,Quantity,Revision,PlannedDate,Status\n");
        for (var i = 0; i < rows; i++)
        {
            var sequence = firstSequence + i;
            builder.Append(System.Globalization.CultureInfo.InvariantCulture, $"{projectCode}-PUN-MEC-DWG-{sequence:D4},Pump arrangement {sequence},General arrangement of pump {sequence},MEC,DWG,2.1,STD,PUN,1,A,2027-06-01,Planned\n");
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    public static string UniqueProjectCode()
    {
        const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var random = Random.Shared;
        var prefix = new string(Enumerable.Range(0, 4).Select(_ => letters[random.Next(letters.Length)]).ToArray());
        return $"{prefix}-{random.Next(100, 999)}";
    }
}
