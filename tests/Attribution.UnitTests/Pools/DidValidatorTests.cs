using Attribution.Domain.Pools;
using Xunit;

namespace Attribution.UnitTests.Pools;

// FR-002: malformed CSV entries are rejected — not a syntactically valid E.164 number.
public class DidValidatorTests
{
    [Theory]
    [InlineData("+15550001234")]
    [InlineData("15550001234")]
    [InlineData("442071838750")]
    public void ValidE164Numbers_AreAccepted(string did)
    {
        Assert.True(DidValidator.IsValidE164(did));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-number")]
    [InlineData("555-000-1234")]
    [InlineData("1234")] // too short
    [InlineData("1234567890123456")] // too long (16 digits)
    [InlineData("+1 555 000 1234")] // spaces not allowed
    public void MalformedEntries_AreRejected(string? did)
    {
        Assert.False(DidValidator.IsValidE164(did));
    }
}
