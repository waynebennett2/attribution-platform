namespace Attribution.Domain.Sessions;

// FR-013, FR-014, FR-015: everything captured about how a visitor arrived.
public sealed record ArrivalDetails(
    string? LandingPage,
    string? Referrer,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? UtmTerm,
    string? UtmContent,
    string? Gclid,
    string? Gbraid,
    string? Wbraid,
    string? Ga4ClientId)
{
    public static readonly ArrivalDetails Empty = new(null, null, null, null, null, null, null, null, null, null, null);
}
