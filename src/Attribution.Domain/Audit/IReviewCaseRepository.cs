namespace Attribution.Domain.Audit;

public interface IReviewCaseRepository
{
    Task<ReviewCase?> GetByIdAsync(Guid id);

    Task<IReadOnlyList<ReviewCase>> GetOpenAsync();

    Task AddAsync(ReviewCase reviewCase);

    Task UpdateAsync(ReviewCase reviewCase);
}
