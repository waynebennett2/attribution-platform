namespace Attribution.Domain.Audit;

public interface IReviewCaseRepository
{
    Task AddAsync(ReviewCase reviewCase);
}
