namespace Attribution.Domain.Identity;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);

    Task<User?> GetByUsernameAsync(string username);

    Task<User?> GetByRefreshTokenHashAsync(string refreshTokenHash);

    Task<IReadOnlyList<User>> GetAllAsync();

    // FR-046: backs the guard against deactivating or demoting the last active System
    // Administrator account.
    Task<int> CountActiveSystemAdministratorsAsync();

    Task AddAsync(User user);

    Task UpdateAsync(User user);
}
