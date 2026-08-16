namespace Attribution.Domain.Qualification;

public interface IQualificationResultRepository
{
    Task<QualificationResult?> GetCurrentByCallIdAsync(Guid callId);

    Task AddAsync(QualificationResult result);

    Task UpdateAsync(QualificationResult result);
}
