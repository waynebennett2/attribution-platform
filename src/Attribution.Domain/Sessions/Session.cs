namespace Attribution.Domain.Sessions;

// FR-010, FR-012, FR-013-FR-015, FR-039: one visit by a visitor.
public class Session
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid VisitorId { get; private set; }
    public Guid WebsiteId { get; private set; }
    public ArrivalDetails Arrival { get; private set; } = ArrivalDetails.Empty;
    public ConsentState ConsentState { get; private set; } = ConsentState.Pending;
    public SessionProvenance Provenance { get; private set; } = SessionProvenance.Ordinary;
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }

    private Session() { }

    // FR-039: a session only comes into being once consent is granted.
    public static Session Create(
        Guid visitorId,
        Guid websiteId,
        ArrivalDetails arrival,
        SessionProvenance provenance,
        DateTimeOffset startedAt,
        TimeSpan timeout)
    {
        return new Session
        {
            VisitorId = visitorId,
            WebsiteId = websiteId,
            Arrival = arrival,
            ConsentState = ConsentState.Granted,
            Provenance = provenance,
            StartedAt = startedAt,
            ExpiresAt = startedAt.Add(timeout),
        };
    }

    // FR-012: a genuinely active visitor's session is refreshed on each heartbeat, well
    // inside the timeout (default heartbeat 5 min vs. timeout 30 min).
    public void RefreshActivity(DateTimeOffset at, TimeSpan timeout)
    {
        if (IsExpired(at))
        {
            throw new InvalidOperationException("Cannot refresh an already-expired or ended session.");
        }

        ExpiresAt = at.Add(timeout);
    }

    public bool IsExpired(DateTimeOffset now) => EndedAt is not null || now >= ExpiresAt;

    public void EndByTimeout(DateTimeOffset endedAt) => EndedAt = endedAt;

    // FR-039: consent withdrawal ends the session immediately — a deliberate act, not a
    // timeout — which is what lets FR-018 treat it as a distinct, extension-free release path.
    public void EndByConsentWithdrawal(DateTimeOffset endedAt)
    {
        ConsentState = ConsentState.Withdrawn;
        EndedAt = endedAt;
    }

    internal static Session Rehydrate(
        Guid id, Guid visitorId, Guid websiteId, ArrivalDetails arrival, ConsentState consentState,
        SessionProvenance provenance, DateTimeOffset startedAt, DateTimeOffset expiresAt, DateTimeOffset? endedAt) => new()
        {
            Id = id,
            VisitorId = visitorId,
            WebsiteId = websiteId,
            Arrival = arrival,
            ConsentState = consentState,
            Provenance = provenance,
            StartedAt = startedAt,
            ExpiresAt = expiresAt,
            EndedAt = endedAt,
        };
}
