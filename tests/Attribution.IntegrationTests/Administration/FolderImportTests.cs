using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Dapper;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Attribution.IntegrationTests.Administration;

// FR-051: importing a pool's numbers from a CSV already sitting in a configured
// server-side folder, as an administrator-triggered alternative to a browser upload —
// applying the identical per-row validation as FR-002's multipart-upload path.
public class FolderImportTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _importFolder = null!;

    public Task InitializeAsync()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        _importFolder = Path.Combine(Path.GetTempPath(), $"number-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_importFolder);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
            builder.UseSetting("NumberImport:FolderPath", _importFolder);
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.SystemAdministrator));
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        Directory.Delete(_importFolder, recursive: true);
    }

    [Fact]
    public async Task ListFiles_ReturnsCsvFilesCurrentlyInTheConfiguredFolder()
    {
        var fileName = $"numbers-{Guid.NewGuid():N}.csv";
        await File.WriteAllTextAsync(Path.Combine(_importFolder, fileName), "did\n+441632960001\n");

        var response = await _client.GetAsync("/v1/admin/numbers/import-folder/files");

        response.EnsureSuccessStatusCode();
        var files = await response.Content.ReadFromJsonAsync<List<ImportFolderFileDto>>();
        Assert.Contains(files!, f => f.FileName == fileName);
    }

    [Fact]
    public async Task ImportFromFolder_ProducesTheSamePerRowResults_AsAMultipartUpload()
    {
        var poolId = await CreatePoolAsync();
        var did = NextTestDid();
        var fileName = $"numbers-{Guid.NewGuid():N}.csv";
        var csvContent = $"did\n{did}\nnot-a-number\n";
        await File.WriteAllTextAsync(Path.Combine(_importFolder, fileName), csvContent);

        var response = await _client.PostAsJsonAsync($"/v1/admin/pools/{poolId}/numbers/import-from-folder", new { file_name = fileName });

        response.EnsureSuccessStatusCode();
        var results = await response.Content.ReadFromJsonAsync<List<ImportRowResultDto>>();
        Assert.Contains(results!, r => r.Did == did && r.Accepted);
        Assert.Contains(results!, r => r.Did == "not-a-number" && !r.Accepted && r.Reason == "malformed");
    }

    [Fact]
    public async Task ImportFromFolder_ReTriggeringTheSameFile_RejectsAlreadyImportedNumbersAsDuplicates_RatherThanReAdding()
    {
        var poolId = await CreatePoolAsync();
        var did = NextTestDid();
        var fileName = $"numbers-{Guid.NewGuid():N}.csv";
        await File.WriteAllTextAsync(Path.Combine(_importFolder, fileName), $"did\n{did}\n");

        var first = await _client.PostAsJsonAsync($"/v1/admin/pools/{poolId}/numbers/import-from-folder", new { file_name = fileName });
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync($"/v1/admin/pools/{poolId}/numbers/import-from-folder", new { file_name = fileName });
        second.EnsureSuccessStatusCode();
        var secondResults = await second.Content.ReadFromJsonAsync<List<ImportRowResultDto>>();

        Assert.Contains(secondResults!, r => r.Did == did && !r.Accepted && r.Reason == "duplicate");
    }

    // tracking_numbers.did is globally unique and this suite runs against the shared
    // dev database (not a per-test container), so a fixed test DID risks colliding with a
    // row left over from a previous run — generate a fresh one instead.
    private static string NextTestDid() =>
        "+44" + string.Concat(Enumerable.Range(0, 10).Select(_ => Random.Shared.Next(0, 10)));

    [Theory]
    [InlineData("../outside.csv")]
    [InlineData("/etc/passwd")]
    public async Task ImportFromFolder_RejectsAFileNameThatIsNotAPlainNameInsideTheFolder(string fileName)
    {
        var poolId = await CreatePoolAsync();

        var response = await _client.PostAsJsonAsync($"/v1/admin/pools/{poolId}/numbers/import-from-folder", new { file_name = fileName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Guid> CreatePoolAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/v1/admin/pools", new { name = $"pool-{Guid.NewGuid():N}", scope_type = "website", scope_ref = Guid.NewGuid().ToString() });
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedPoolResponse>();
        return created!.Id;
    }

    private sealed record CreatedPoolResponse([property: System.Text.Json.Serialization.JsonPropertyName("id")] Guid Id);

    private sealed record ImportFolderFileDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("file_name")] string FileName,
        [property: System.Text.Json.Serialization.JsonPropertyName("size_bytes")] long SizeBytes,
        [property: System.Text.Json.Serialization.JsonPropertyName("modified_at")] DateTimeOffset ModifiedAt);

    private sealed record ImportRowResultDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("row")] int Row,
        [property: System.Text.Json.Serialization.JsonPropertyName("did")] string Did,
        [property: System.Text.Json.Serialization.JsonPropertyName("accepted")] bool Accepted,
        [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string? Reason);
}
