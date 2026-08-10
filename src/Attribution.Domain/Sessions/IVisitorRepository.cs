namespace Attribution.Domain.Sessions;

public interface IVisitorRepository
{
    Task<Visitor?> GetByIdAsync(Guid id);

    Task AddAsync(Visitor visitor);
}
