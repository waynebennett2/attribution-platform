using System.Data;
using Attribution.Domain.Sessions;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class SessionRepository : RepositoryBase, ISessionRepository
{
    public SessionRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Session?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<SessionRow>(
            "SELECT * FROM sessions WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task AddAsync(Session session)
    {
        using var connection = OpenConnection();
        await InsertAsync(connection, transaction: null, session);
    }

    public async Task UpdateAsync(Session session)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE sessions SET consent_state = @ConsentState, expires_at = @ExpiresAt, ended_at = @EndedAt
            WHERE id = @Id
            """,
            SessionRow.FromDomain(session));
    }

    public static Task InsertAsync(IDbConnection connection, IDbTransaction? transaction, Session session) =>
        connection.ExecuteAsync(
            """
            INSERT INTO sessions
                (id, visitor_id, website_id, landing_page, referrer, utm_source, utm_medium, utm_campaign,
                 utm_term, utm_content, gclid, gbraid, wbraid, ga4_client_id, consent_state, provenance,
                 started_at, expires_at, ended_at)
            VALUES
                (@Id, @VisitorId, @WebsiteId, @LandingPage, @Referrer, @UtmSource, @UtmMedium, @UtmCampaign,
                 @UtmTerm, @UtmContent, @Gclid, @Gbraid, @Wbraid, @Ga4ClientId, @ConsentState, @Provenance,
                 @StartedAt, @ExpiresAt, @EndedAt)
            """,
            SessionRow.FromDomain(session),
            transaction);

    private sealed class SessionRow
    {
        public string Id { get; set; } = string.Empty;
        public string VisitorId { get; set; } = string.Empty;
        public string WebsiteId { get; set; } = string.Empty;
        public string? LandingPage { get; set; }
        public string? Referrer { get; set; }
        public string? UtmSource { get; set; }
        public string? UtmMedium { get; set; }
        public string? UtmCampaign { get; set; }
        public string? UtmTerm { get; set; }
        public string? UtmContent { get; set; }
        public string? Gclid { get; set; }
        public string? Gbraid { get; set; }
        public string? Wbraid { get; set; }
        public string? Ga4ClientId { get; set; }
        public string ConsentState { get; set; } = string.Empty;
        public string Provenance { get; set; } = string.Empty;
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }

        public Session ToDomain() => Session.Rehydrate(
            Guid.Parse(Id), Guid.Parse(VisitorId), Guid.Parse(WebsiteId),
            new ArrivalDetails(LandingPage, Referrer, UtmSource, UtmMedium, UtmCampaign, UtmTerm, UtmContent,
                Gclid, Gbraid, Wbraid, Ga4ClientId),
            Enum.Parse<ConsentState>(ConsentState),
            Enum.Parse<SessionProvenance>(Provenance),
            StartedAt, ExpiresAt, EndedAt);

        public static object FromDomain(Session session) => new
        {
            Id = session.Id.ToString(),
            VisitorId = session.VisitorId.ToString(),
            WebsiteId = session.WebsiteId.ToString(),
            session.Arrival.LandingPage,
            session.Arrival.Referrer,
            session.Arrival.UtmSource,
            session.Arrival.UtmMedium,
            session.Arrival.UtmCampaign,
            session.Arrival.UtmTerm,
            session.Arrival.UtmContent,
            session.Arrival.Gclid,
            session.Arrival.Gbraid,
            session.Arrival.Wbraid,
            session.Arrival.Ga4ClientId,
            ConsentState = session.ConsentState.ToString(),
            Provenance = session.Provenance.ToString(),
            session.StartedAt,
            session.ExpiresAt,
            session.EndedAt,
        };
    }
}
