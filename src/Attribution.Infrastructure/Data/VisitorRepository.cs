using System.Data;
using Attribution.Domain.Sessions;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class VisitorRepository : RepositoryBase, IVisitorRepository
{
    public VisitorRepository(IDbConnectionFactory connectionFactory) : base(connectionFactory) { }

    public async Task<Visitor?> GetByIdAsync(Guid id)
    {
        using var connection = OpenConnection();
        var row = await connection.QuerySingleOrDefaultAsync<VisitorRow>(
            "SELECT * FROM visitors WHERE id = @Id", new { Id = id.ToString() });
        return row?.ToDomain();
    }

    public async Task AddAsync(Visitor visitor)
    {
        using var connection = OpenConnection();
        await InsertAsync(connection, transaction: null, visitor);
    }

    public static Task InsertAsync(IDbConnection connection, IDbTransaction? transaction, Visitor visitor) =>
        connection.ExecuteAsync(
            "INSERT INTO visitors (id, website_id, first_seen_at, de_identified_at) VALUES (@Id, @WebsiteId, @FirstSeenAt, @DeIdentifiedAt)",
            new
            {
                Id = visitor.Id.ToString(),
                WebsiteId = visitor.WebsiteId.ToString(),
                visitor.FirstSeenAt,
                visitor.DeIdentifiedAt,
            },
            transaction);

    private sealed class VisitorRow
    {
        public string Id { get; set; } = string.Empty;
        public string WebsiteId { get; set; } = string.Empty;
        public DateTimeOffset FirstSeenAt { get; set; }
        public DateTimeOffset? DeIdentifiedAt { get; set; }

        public Visitor ToDomain() => Visitor.Rehydrate(Guid.Parse(Id), Guid.Parse(WebsiteId), FirstSeenAt, DeIdentifiedAt);
    }
}
