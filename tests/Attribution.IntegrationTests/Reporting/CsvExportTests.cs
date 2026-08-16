using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Attribution.Domain.Identity;
using Attribution.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Attribution.IntegrationTests.Reporting;

// FR-030: a report's CSV export must contain the same rows and values, with the same
// filters and period, as the JSON report it was generated from — verified here by
// generating both from the identical query and comparing them directly, rather than
// trusting that the two code paths agree.
public class CsvExportTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private DateOnly _day;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:SigningSecret", TestAuth.SigningSecret);
            builder.UseSetting("Jwt:Issuer", TestAuth.Issuer);
            builder.UseSetting("Jwt:Audience", TestAuth.Audience);
        });
        _client = _factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestAuth.IssueToken(Role.MarketingAdministrator));

        _day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-Random.Shared.Next(2000, 200000)));
        var dayStart = new DateTimeOffset(_day.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);

        var seed = await ReportingSeedHelper.CreateAsync(campaign: "csv-export-test");
        await seed.SeedAttributedCallAsync(dayStart, answeredAt: dayStart, connectedDurationSeconds: 65, isQualified: true);
        await seed.SeedAttributedCallAsync(dayStart.AddHours(1), answeredAt: dayStart.AddHours(1), connectedDurationSeconds: 12, isQualified: false);
        await seed.SeedUnattributedCallAsync(dayStart.AddHours(2), "number_never_allocated");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("dashboard")]
    [InlineData("campaigns")]
    [InlineData("calls")]
    [InlineData("missed")]
    [InlineData("qualified")]
    [InlineData("unattributed")]
    [InlineData("coverage")]
    public async Task CsvExport_ContainsTheSameRowsAndValues_AsTheJsonReport(string report)
    {
        var query = $"from={_day:yyyy-MM-dd}&to={_day:yyyy-MM-dd}";

        var jsonResponse = await _client.GetAsync($"/v1/reports/{report}?{query}");
        jsonResponse.EnsureSuccessStatusCode();
        var json = await jsonResponse.Content.ReadFromJsonAsync<JsonElement>();
        var jsonRows = json.GetProperty("rows").EnumerateArray().ToList();

        var csvResponse = await _client.GetAsync($"/v1/reports/{report}/export.csv?{query}");
        csvResponse.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", csvResponse.Content.Headers.ContentType?.MediaType);
        var csvRows = ParseCsv(await csvResponse.Content.ReadAsStringAsync());

        Assert.Equal(jsonRows.Count, csvRows.Count);
        for (var i = 0; i < jsonRows.Count; i++)
        {
            var jsonRow = jsonRows[i];
            var csvRow = csvRows[i];
            foreach (var property in jsonRow.EnumerateObject())
            {
                Assert.True(csvRow.TryGetValue(property.Name, out var csvValue), $"CSV row missing column '{property.Name}'.");
                AssertSameValue(property.Value, csvValue);
            }
        }
    }

    // Compares by value, not by raw string — JSON's default DateTime formatting trims
    // trailing zero fractional digits, while the CSV export always writes a fixed-width
    // "O" timestamp; both represent the identical instant, just with different formatting
    // precision, which FR-030 ("same values") doesn't forbid.
    private static void AssertSameValue(JsonElement jsonValue, string csvValue)
    {
        switch (jsonValue.ValueKind)
        {
            case JsonValueKind.Null:
                Assert.Equal(string.Empty, csvValue);
                return;
            case JsonValueKind.True:
                Assert.Equal("true", csvValue);
                return;
            case JsonValueKind.False:
                Assert.Equal("false", csvValue);
                return;
            case JsonValueKind.String:
                var jsonString = jsonValue.GetString() ?? string.Empty;
                if (DateTimeOffset.TryParse(jsonString, out var jsonDate) && DateTimeOffset.TryParse(csvValue, out var csvDate))
                {
                    Assert.Equal(jsonDate, csvDate);
                }
                else
                {
                    Assert.Equal(jsonString, csvValue);
                }

                return;
            default:
                Assert.Equal(jsonValue.GetRawText(), csvValue);
                return;
        }
    }

    // Minimal RFC4180-style parser: handles double-quoted fields with embedded commas and
    // escaped quotes ("") — sufficient for what ReportsController's CsvField ever produces.
    private static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (lines.Length == 0 || (lines.Length == 1 && lines[0].Length == 0))
        {
            return new List<Dictionary<string, string>>();
        }

        var header = ParseLine(lines[0]);
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Length; i++)
        {
            var fields = ParseLine(lines[i]);
            var row = new Dictionary<string, string>();
            for (var c = 0; c < header.Count; c++)
            {
                row[header[c]] = c < fields.Count ? fields[c] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<string> ParseLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
