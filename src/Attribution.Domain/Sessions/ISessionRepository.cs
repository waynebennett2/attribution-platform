namespace Attribution.Domain.Sessions;

public interface ISessionRepository
{
    Task<Session?> GetByIdAsync(Guid id);

    Task AddAsync(Session session);

    Task UpdateAsync(Session session);
}
