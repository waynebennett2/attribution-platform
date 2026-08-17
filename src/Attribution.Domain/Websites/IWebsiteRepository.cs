namespace Attribution.Domain.Websites;

public interface IWebsiteRepository
{
    Task<Website?> GetByIdAsync(Guid id);

    Task<Website?> GetByOriginAsync(string origin);

    Task<IReadOnlyList<Website>> GetAllAsync();

    Task AddAsync(Website website);

    Task UpdateAsync(Website website);
}
