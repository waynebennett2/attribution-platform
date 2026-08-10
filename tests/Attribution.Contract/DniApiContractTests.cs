using System.Text.Json;
using Attribution.Api.Contracts;
using Xunit;

namespace Attribution.Contract;

// Validates that the Api's DTOs serialize with exactly the field names documented in
// contracts/dni-api.md — a pure shape check (JSON serialization), not a live HTTP round
// trip, so it needs neither a running server nor a database.
public class DniApiContractTests
{
    [Fact]
    public void AllocateRequest_SerializesWithDocumentedFieldNames()
    {
        var dto = new AllocateRequestDto
        {
            WebsiteId = "w1",
            ClientToken = "c1",
            ConsentGranted = true,
            LandingPage = "https://example.com/",
            Referrer = "https://google.com/",
            Utm = new UtmDto { Source = "google", Medium = "cpc", Campaign = "spring", Term = "t", Content = "c" },
            Gclid = "g1",
            Gbraid = "gb1",
            Wbraid = "wb1",
            Ga4ClientId = "ga1",
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        foreach (var expected in new[]
        {
            "website_id", "client_token", "consent_granted", "landing_page", "referrer",
            "utm", "gclid", "gbraid", "wbraid", "ga4_client_id",
        })
        {
            Assert.True(doc.TryGetProperty(expected, out _), $"missing field '{expected}'");
        }

        var utm = doc.GetProperty("utm");
        foreach (var expected in new[] { "source", "medium", "campaign", "term", "content" })
        {
            Assert.True(utm.TryGetProperty(expected, out _), $"missing utm field '{expected}'");
        }
    }

    [Fact]
    public void AllocateResponse_SuccessShape_MatchesContract()
    {
        var dto = new AllocateResponseDto { SessionId = "s1", Number = "+15550001111", ExpiresAt = DateTimeOffset.UtcNow };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.TryGetProperty("session_id", out var sessionId));
        Assert.Equal("s1", sessionId.GetString());
        Assert.True(doc.TryGetProperty("number", out _));
        Assert.True(doc.TryGetProperty("expires_at", out _));
    }

    [Fact]
    public void AllocateResponse_FailureShape_CarriesReasonAndNullSessionId()
    {
        var dto = new AllocateResponseDto { SessionId = null, Number = "+15550009999", Reason = "no_consent" };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal(JsonValueKind.Null, doc.GetProperty("session_id").ValueKind);
        Assert.Equal("no_consent", doc.GetProperty("reason").GetString());
    }

    [Fact]
    public void HeartbeatRequestResponse_SerializeWithDocumentedFieldNames()
    {
        var requestJson = JsonSerializer.Serialize(new HeartbeatRequestDto { SessionId = "s1" });
        Assert.Contains("\"session_id\":\"s1\"", requestJson);

        var responseJson = JsonSerializer.Serialize(new HeartbeatResponseDto { StillValid = true, Number = "+15550001111" });
        var doc = JsonDocument.Parse(responseJson).RootElement;
        Assert.True(doc.TryGetProperty("still_valid", out var stillValid));
        Assert.True(stillValid.GetBoolean());
        Assert.True(doc.TryGetProperty("number", out _));
    }

    [Fact]
    public void ConsentRequest_SerializesWithDocumentedFieldNames()
    {
        var dto = new ConsentRequestDto
        {
            SessionId = "s1",
            ClientToken = "c1",
            WebsiteId = "w1",
            Consent = "withdrawn",
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        foreach (var expected in new[] { "session_id", "client_token", "website_id", "consent" })
        {
            Assert.True(doc.TryGetProperty(expected, out _), $"missing field '{expected}'");
        }
    }

    [Fact]
    public void ShadowObserveRequest_SerializesWithDocumentedFieldNames()
    {
        var dto = new ShadowObserveRequestDto
        {
            WebsiteId = "w1",
            SessionId = "s1",
            ObservedNumber = "+15550001111",
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        foreach (var expected in new[] { "website_id", "session_id", "observed_number" })
        {
            Assert.True(doc.TryGetProperty(expected, out _), $"missing field '{expected}'");
        }
    }
}
