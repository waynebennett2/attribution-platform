using Attribution.Domain.Identity;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class UserRepository : RepositoryBase, IUserRepository
{
    public UserRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM users WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM users WHERE username = @Username", new { Username = username });
        return row?.ToDomain();
    }

    public async Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(
            "SELECT * FROM users WHERE refresh_token_hash = @RefreshTokenHash", new { RefreshTokenHash = refreshTokenHash });
        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<User>> GetAllAsync()
    {
        using var connection = OpenConnection();
        var rows = await connection.QueryAsync<UserRow>("SELECT * FROM users ORDER BY created_at");
        return rows.Select(r => r.ToDomain()).ToList();
    }

    public async Task<int> CountActiveSystemAdministratorsAsync()
    {
        using var connection = OpenConnection();
        return await connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*) FROM users
            WHERE is_active = 1
              AND COALESCE(role_override, mapped_role) = @Role
            """,
            new { Role = nameof(Role.SystemAdministrator) });
    }

    public async Task AddAsync(User user)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            INSERT INTO users
                (id, username, client_id, identity_type, mapped_role, role_override,
                 role_overridden_by, password_hash, totp_secret, mfa_required,
                 refresh_token_hash, refresh_token_expires_at, is_active, created_at, last_seen_at)
            VALUES
                (@Id, @Username, @ClientId, @IdentityType, @MappedRole, @RoleOverride,
                 @RoleOverriddenBy, @PasswordHash, @TotpSecret, @MfaRequired,
                 @RefreshTokenHash, @RefreshTokenExpiresAt, @IsActive, @CreatedAt, @LastSeenAt)
            """,
            UserRow.FromDomain(user));
    }

    public async Task UpdateAsync(User user)
    {
        using var connection = OpenConnection();
        await connection.ExecuteAsync(
            """
            UPDATE users SET
                role_override = @RoleOverride, role_overridden_by = @RoleOverriddenBy,
                refresh_token_hash = @RefreshTokenHash, refresh_token_expires_at = @RefreshTokenExpiresAt,
                is_active = @IsActive, last_seen_at = @LastSeenAt
            WHERE id = @Id
            """,
            UserRow.FromDomain(user));
    }

    private sealed class UserRow
    {
        public string Id { get; set; } = string.Empty;
        public string? Username { get; set; }
        public string? ClientId { get; set; }
        public string IdentityType { get; set; } = string.Empty;
        public string MappedRole { get; set; } = string.Empty;
        public string? RoleOverride { get; set; }
        public string? RoleOverriddenBy { get; set; }
        public string? PasswordHash { get; set; }
        public string? TotpSecret { get; set; }
        public bool MfaRequired { get; set; }
        public string? RefreshTokenHash { get; set; }
        public DateTimeOffset? RefreshTokenExpiresAt { get; set; }
        public bool IsActive { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastSeenAt { get; set; }

        public User ToDomain() => User.Rehydrate(
            Guid.Parse(Id), Username, ClientId,
            Enum.Parse<Domain.Identity.IdentityType>(IdentityType),
            Enum.Parse<Role>(MappedRole),
            RoleOverride is null ? null : Enum.Parse<Role>(RoleOverride),
            RoleOverriddenBy, PasswordHash, TotpSecret, MfaRequired,
            RefreshTokenHash, RefreshTokenExpiresAt, IsActive, CreatedAt, LastSeenAt);

        public static object FromDomain(User user) => new
        {
            Id = user.Id.ToString(),
            user.Username,
            user.ClientId,
            IdentityType = user.IdentityType.ToString(),
            MappedRole = user.MappedRole.ToString(),
            RoleOverride = user.RoleOverride?.ToString(),
            user.RoleOverriddenBy,
            user.PasswordHash,
            user.TotpSecret,
            user.MfaRequired,
            user.RefreshTokenHash,
            user.RefreshTokenExpiresAt,
            user.IsActive,
            user.CreatedAt,
            user.LastSeenAt,
        };
    }
}
