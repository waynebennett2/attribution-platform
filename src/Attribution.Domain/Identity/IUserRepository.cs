namespace Attribution.Domain.Identity;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id);

    Task<User?> GetBySubjectRefAsync(string subjectRef);

    Task<User?> GetByUsernameAsync(string username);

    Task<IReadOnlyList<User>> GetBreakGlassUsersAsync();

    Task AddAsync(User user);

    Task UpdateAsync(User user);
}
