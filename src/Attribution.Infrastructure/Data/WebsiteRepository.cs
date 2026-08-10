using Attribution.Domain.Websites;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class WebsiteRepository : RepositoryBase, IWebsiteRepository
{
    public WebsiteRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Website?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<WebsiteRow>(
            "SELECT * FROM websites WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<Website?> GetByOriginAsync(string origin)
    {
        using var connection = OpenConnection();
        // permitted_origins is stored newline-delimited; a LIKE scan is adequate at this
        // scale (spec Assumptions: monthly website count not stated but expected to be small).
        var rows = await connection.QueryAsync<WebsiteRow>("SELECT * FROM websites");
        return rows.Select(r => r.ToDomain())
            .FirstOrDefault(w => w.PermittedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));
    }

    public async Task AddAsync(Website website)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO websites
                (id, name, permitted_origins, default_number, session_timeout_seconds,
                 heartbeat_interval_seconds, allocation_window_extension_seconds, cooldown_seconds,
                 consent_required, shadow_mode_enabled, business_unit, local_timezone, created_at, updated_at)
            VALUES
                (@Id, @Name, @PermittedOrigins, @DefaultNumber, @SessionTimeoutSeconds,
                 @HeartbeatIntervalSeconds, @AllocationWindowExtensionSeconds, @CooldownSeconds,
                 @ConsentRequired, @ShadowModeEnabled, @BusinessUnit, @LocalTimezone, @CreatedAt, @UpdatedAt)
            """,
            WebsiteRow.FromDomain(website));
    }

    public async Task UpdateAsync(Website website)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE websites SET
                name = @Name, permitted_origins = @PermittedOrigins, default_number = @DefaultNumber,
                session_timeout_seconds = @SessionTimeoutSeconds, heartbeat_interval_seconds = @HeartbeatIntervalSeconds,
                allocation_window_extension_seconds = @AllocationWindowExtensionSeconds, cooldown_seconds = @CooldownSeconds,
                consent_required = @ConsentRequired, shadow_mode_enabled = @ShadowModeEnabled,
                business_unit = @BusinessUnit, local_timezone = @LocalTimezone, updated_at = @UpdatedAt
            WHERE id = @Id
            """,
            WebsiteRow.FromDomain(website));
    }

    // Dapper materializes into this flat row type (public settable properties); the
    // Website aggregate itself keeps its setters private (see Website.Rehydrate).
    private sealed class WebsiteRow
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string PermittedOrigins { get; set; } = string.Empty;
        public string DefaultNumber { get; set; } = string.Empty;
        public int SessionTimeoutSeconds { get; set; }
        public int HeartbeatIntervalSeconds { get; set; }
        public int AllocationWindowExtensionSeconds { get; set; }
        public int CooldownSeconds { get; set; }
        public bool ConsentRequired { get; set; }
        public bool ShadowModeEnabled { get; set; }
        public string? BusinessUnit { get; set; }
        public string LocalTimezone { get; set; } = "UTC";
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }

        public Website ToDomain() => Website.Rehydrate(
            Guid.Parse(Id), Name, PermittedOrigins.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            DefaultNumber, SessionTimeoutSeconds, HeartbeatIntervalSeconds, AllocationWindowExtensionSeconds,
            CooldownSeconds, ConsentRequired, ShadowModeEnabled, BusinessUnit, LocalTimezone, CreatedAt, UpdatedAt);

        public static object FromDomain(Website website) => new
        {
            Id = website.Id.ToString(),
            website.Name,
            PermittedOrigins = string.Join('\n', website.PermittedOrigins),
            website.DefaultNumber,
            website.SessionTimeoutSeconds,
            website.HeartbeatIntervalSeconds,
            website.AllocationWindowExtensionSeconds,
            website.CooldownSeconds,
            website.ConsentRequired,
            website.ShadowModeEnabled,
            website.BusinessUnit,
            website.LocalTimezone,
            website.CreatedAt,
            website.UpdatedAt,
        };
    }
}
