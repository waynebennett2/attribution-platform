using System.Data;
using Attribution.Application.Allocation;
using Attribution.Domain.Sessions;
using Dapper;

namespace Attribution.Infrastructure.Data;

public sealed class AtomicAllocator : IAtomicAllocator
{
    private readonly IDbConnectionFactory _connectionFactory;

    public AtomicAllocator(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<AllocationAttemptResult> TryAllocateAsync(
        Visitor visitor,
        Session session,
        Guid poolId,
        TimeSpan cooldown,
        DateTimeOffset windowStart,
        TimeSpan allocationWindowExtension,
        DateTimeOffset now)
    {
        using var connection = _connectionFactory.CreateOpenConnection();
        // READ COMMITTED avoids REPEATABLE READ's extra gap-locking on the NOT EXISTS
        // subquery, which would otherwise serialize unrelated candidates unnecessarily
        // and defeat the point of SKIP LOCKED (research.md §2).
        using var transaction = connection.BeginTransaction(IsolationLevel.ReadCommitted);

        try
        {
            // FR-003, FR-006: pick one eligible, currently-unallocated number in the pool.
            // SKIP LOCKED means a concurrent request picking a different candidate never
            // blocks on this one; authority for "is it available" is the allocations table
            // itself (a covering-or-cooldown row means unavailable), not the denormalized
            // TrackingNumber.LastReleasedAt hint used only to order candidates.
            //
            // ORDER BY is a plain column (NULLs — never-released numbers — sort first in
            // MySQL's ASC order, so this needs no CASE/boolean expression to get the same
            // "prefer never-released, then oldest-released" preference) specifically so this
            // resolves via IX_tracking_numbers_pool_status_released without a filesort.
            // FOR UPDATE SKIP LOCKED's skip-and-keep-scanning behavior is only reliable
            // against a straightforward index-ordered scan — under a filesort/materialized
            // intermediate result (which the previous `ORDER BY (x IS NULL) DESC, x ASC`
            // computed-expression form forced MySQL into), a transaction whose "top" sorted
            // candidate happened to be locked would return zero rows instead of continuing
            // on to the next unlocked candidate, which is genuinely observable under real
            // concurrency: T120/T121's own load/failover tests reproduced it directly
            // (roughly a third of concurrent requests reporting pool_exhausted despite
            // plenty of genuinely free numbers) before this fix.
            var trackingNumberId = await connection.QuerySingleOrDefaultAsync<string>(
                """
                SELECT tn.id FROM tracking_numbers tn
                WHERE tn.pool_id = @PoolId
                  AND tn.status = 'Active'
                  AND NOT EXISTS (
                    SELECT 1 FROM allocations a
                    WHERE a.tracking_number_id = tn.id
                      AND @Now < DATE_ADD(a.window_end, INTERVAL @CooldownSeconds SECOND)
                  )
                ORDER BY tn.last_released_at ASC
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                new { PoolId = poolId.ToString(), Now = now, CooldownSeconds = (int)cooldown.TotalSeconds },
                transaction);

            if (trackingNumberId is null)
            {
                // FR-011: pool exhausted — roll back so no Visitor/Session is left orphaned
                // with no Allocation; the caller falls back to the website's default number.
                transaction.Rollback();
                return new AllocationAttemptResult(false, null);
            }

            await VisitorRepository.InsertAsync(connection, transaction, visitor);
            await SessionRepository.InsertAsync(connection, transaction, session);

            var allocation = Allocation.Create(
                Guid.Parse(trackingNumberId),
                session.Id,
                poolId,
                windowStart,
                session.ExpiresAt,
                allocationWindowExtension);
            await AllocationRepository.InsertAsync(connection, transaction, allocation);

            transaction.Commit();
            return new AllocationAttemptResult(true, allocation);
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }
}
