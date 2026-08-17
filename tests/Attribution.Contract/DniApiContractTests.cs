using System.Collections.Generic;
using System.Linq;
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

    // FR-050: matched_pool_ids/session_id are the only new AllocateRequestDto fields; a
    // single-pool client that never sets them must not emit them at all, so the request
    // shape a multi_pool_enabled = false website's client sends stays byte-for-byte what
    // it always has been.
    [Fact]
    public void AllocateRequest_WithoutMultiPoolFields_OmitsThemEntirely()
    {
        var dto = new AllocateRequestDto { WebsiteId = "w1", ClientToken = "c1", ConsentGranted = true };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.TryGetProperty("matched_pool_ids", out var matchedPoolIds));
        Assert.Equal(JsonValueKind.Null, matchedPoolIds.ValueKind);
        Assert.True(doc.TryGetProperty("session_id", out var sessionId));
        Assert.Equal(JsonValueKind.Null, sessionId.ValueKind);
    }

    [Fact]
    public void AllocateRequest_WithMultiPoolFields_SerializesWithDocumentedFieldNames()
    {
        var dto = new AllocateRequestDto
        {
            WebsiteId = "w1",
            ClientToken = "c1",
            ConsentGranted = true,
            MatchedPoolIds = new List<string> { "pool-1", "pool-2" },
            SessionId = "session-1",
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        var poolIds = doc.GetProperty("matched_pool_ids").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(new[] { "pool-1", "pool-2" }, poolIds);
        Assert.Equal("session-1", doc.GetProperty("session_id").GetString());
    }

    // FR-050: pools/allocations must be entirely absent (not even null) for a
    // multi_pool_enabled = false website's response — dni-api.md's "no shape change at
    // all" guarantee.
    [Fact]
    public void AllocateResponse_SinglePoolShape_CarriesNoPoolsOrAllocationsField()
    {
        var dto = new AllocateResponseDto { SessionId = "s1", Number = "+15550001111", ExpiresAt = DateTimeOffset.UtcNow };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.False(doc.TryGetProperty("pools", out _));
        Assert.False(doc.TryGetProperty("allocations", out _));
    }

    [Fact]
    public void AllocateResponse_MultiPoolPreMatchShape_CarriesPoolsMap_AndNoNumberField()
    {
        var dto = new AllocateResponseDto
        {
            SessionId = null,
            Reason = "pending_match",
            Pools = new List<PoolNumberDto> { new() { PoolId = "pool-1", DefaultNumber = "01632 960001" } },
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal(JsonValueKind.Null, doc.GetProperty("session_id").ValueKind);
        Assert.Equal("pending_match", doc.GetProperty("reason").GetString());
        Assert.False(doc.TryGetProperty("number", out _));
        var pool = Assert.Single(doc.GetProperty("pools").EnumerateArray());
        Assert.Equal("pool-1", pool.GetProperty("pool_id").GetString());
        Assert.Equal("01632 960001", pool.GetProperty("default_number").GetString());
    }

    [Fact]
    public void AllocateResponse_MultiPoolSuccessShape_CarriesOneAllocationPerPool()
    {
        var dto = new AllocateResponseDto
        {
            SessionId = "s1",
            Allocations = new List<PoolAllocationDto>
            {
                new() { PoolId = "pool-1", Number = "+441632900001", ExpiresAt = DateTimeOffset.UtcNow },
                new() { PoolId = "pool-2", Number = "+441632900002", ExpiresAt = DateTimeOffset.UtcNow },
            },
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.Equal("s1", doc.GetProperty("session_id").GetString());
        Assert.False(doc.TryGetProperty("number", out _));
        var allocations = doc.GetProperty("allocations").EnumerateArray().ToList();
        Assert.Equal(2, allocations.Count);
        Assert.Contains(allocations, a => a.GetProperty("pool_id").GetString() == "pool-1" && a.GetProperty("number").GetString() == "+441632900001");
    }

    [Fact]
    public void HeartbeatResponse_SinglePoolShape_CarriesNoAllocationsField()
    {
        var dto = new HeartbeatResponseDto { StillValid = true, Number = "+15550001111" };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.False(doc.TryGetProperty("allocations", out _));
    }

    [Fact]
    public void HeartbeatResponse_MultiPoolShape_CarriesPerPoolValidityAndNumber_AndNoTopLevelNumberField()
    {
        var dto = new HeartbeatResponseDto
        {
            StillValid = true,
            Allocations = new List<PoolHeartbeatDto>
            {
                new() { PoolId = "pool-1", StillValid = true, Number = "+441632900001" },
            },
        };

        var json = JsonSerializer.Serialize(dto);
        var doc = JsonDocument.Parse(json).RootElement;

        Assert.True(doc.GetProperty("still_valid").GetBoolean());
        Assert.False(doc.TryGetProperty("number", out _));
        var allocation = Assert.Single(doc.GetProperty("allocations").EnumerateArray());
        Assert.Equal("pool-1", allocation.GetProperty("pool_id").GetString());
        Assert.True(allocation.GetProperty("still_valid").GetBoolean());
        Assert.Equal("+441632900001", allocation.GetProperty("number").GetString());
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
