using System.Security.Cryptography;
using System.Text;

namespace Attribution.Application.Administration;

// FR-040: the scheduled sweep (14-month de-identification, 25-month purge, 7-year audit
// retention) and FR-039's on-demand erasure both live here, sharing the same surrogate
// derivation so a caller's phone number produces the identical surrogate whichever path
// touched it first.
public sealed class RetentionService
{
    private readonly IRetentionRepository _repository;
    private readonly RetentionPolicy _policy;

    public RetentionService(IRetentionRepository repository, RetentionPolicy policy)
    {
        _repository = repository;
        _policy = policy;
    }

    public async Task DeIdentifyExpiredAsync(DateTimeOffset now)
    {
        var visitorCutoff = now.AddMonths(-_policy.VisitorSessionDeIdentifyAfterMonths);
        foreach (var visitorId in await _repository.GetVisitorIdsEligibleForDeIdentificationAsync(visitorCutoff))
        {
            await _repository.DeIdentifyVisitorAsync(visitorId, now);
        }

        var callCutoff = now.AddMonths(-_policy.CallRecordDeIdentifyAfterMonths);
        foreach (var (callId, callerId) in await _repository.GetCallsEligibleForDeIdentificationAsync(callCutoff))
        {
            // FR-040: "except where it is still referenced by an open manual review case" —
            // picked up on a later sweep once the case resolves.
            if (await _repository.HasOpenReviewCaseAsync(callId))
            {
                continue;
            }

            await _repository.DeIdentifyCallAsync(callId, callerId is null ? null : Surrogate(callerId), now);
        }

        foreach (var (publicationId, externalId) in await _repository.GetPublicationsEligibleForDeIdentificationAsync(callCutoff))
        {
            await _repository.DeIdentifyPublicationAsync(publicationId, Surrogate(externalId), now);
        }
    }

    public async Task PurgeExpiredAsync(DateTimeOffset now)
    {
        var purgeCutoff = now.AddMonths(-_policy.CallRecordPurgeAfterMonths);
        foreach (var callId in await _repository.GetCallsEligibleForPurgeAsync(purgeCutoff))
        {
            if (await _repository.HasOpenReviewCaseAsync(callId))
            {
                continue;
            }

            await _repository.PurgeCallAsync(callId);
        }

        var auditCutoff = now.AddYears(-_policy.AuditLogRetentionYears);
        await _repository.PurgeAuditLogOlderThanAsync(auditCutoff);
    }

    // FR-039: "System MUST support erasure of an identified visitor's data on request,
    // completing the erasure within 30 days" — this performs it immediately (synchronously,
    // well inside that bar) rather than queuing a request, since nothing in the spec
    // requires an asynchronous request/tracking workflow, only the completion deadline.
    // The visitor and their sessions are always erased; a call still under open manual
    // review is left for the routine sweep to pick up once resolved, matching FR-040's own
    // carve-out for the same reason — masking the evidence a reviewer is actively relying
    // on could produce a wrong resolution.
    public async Task EraseVisitorAsync(Guid visitorId, DateTimeOffset now)
    {
        await _repository.DeIdentifyVisitorAsync(visitorId, now);

        foreach (var (callId, callerId) in await _repository.GetCallsForVisitorAsync(visitorId))
        {
            if (await _repository.HasOpenReviewCaseAsync(callId))
            {
                continue;
            }

            await _repository.DeIdentifyCallAsync(callId, callerId is null ? null : Surrogate(callerId), now);
        }
    }

    private string Surrogate(string value)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_policy.HmacKey), Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }
}
