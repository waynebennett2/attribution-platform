using Attribution.Application.Administration;
using Attribution.UnitTests.TestSupport;
using Xunit;

namespace Attribution.UnitTests.Retention;

// FR-040, FR-039: RetentionService's orchestration logic — the sweep's eligibility
// filtering, the open-review-case carve-out, and erasure's unconditional-except-for-review
// behavior — isolated against an in-memory fake so these can be asserted exactly without
// depending on (or risking disturbing) the shared integration database's other fixtures.
// RetentionIntegrityTests/ErasureSlaTests cover the real repository's SQL against a real
// database, scoped narrowly enough to stay safe there.
public class RetentionServiceTests
{
    private static RetentionPolicy Policy(int months = 1) => new()
    {
        VisitorSessionDeIdentifyAfterMonths = months,
        CallRecordDeIdentifyAfterMonths = months,
        CallRecordPurgeAfterMonths = months,
        AuditLogRetentionYears = 1,
        HmacKey = "unit-test-retention-hmac-key-at-least-32-characters",
    };

    [Fact]
    public async Task DeIdentifyExpiredAsync_MasksAnExpiredCallersId_WithAStableSurrogate()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var call = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2), CallerId = "+441632960001" };
        repository.Calls.Add(call);
        var service = new RetentionService(repository, Policy());

        await service.DeIdentifyExpiredAsync(now);

        Assert.NotEqual("+441632960001", call.CallerId);
        Assert.NotNull(call.DeIdentifiedAt);
    }

    [Fact]
    public async Task DeIdentifyExpiredAsync_ProducesTheSameSurrogate_ForTheSameOriginalValue()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var callA = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2), CallerId = "+441632960001" };
        var callB = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2), CallerId = "+441632960001" };
        repository.Calls.Add(callA);
        repository.Calls.Add(callB);
        var service = new RetentionService(repository, Policy());

        await service.DeIdentifyExpiredAsync(now);

        // research.md §10: "a keyed HMAC of the original identifier produces the same
        // surrogate every time" — the same caller, seen on two different calls, must
        // still be recognisable as the same caller after de-identification.
        Assert.Equal(callA.CallerId, callB.CallerId);
    }

    [Fact]
    public async Task DeIdentifyExpiredAsync_SkipsACall_StillReferencedByAnOpenReviewCase()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var call = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2), CallerId = "+441632960001" };
        repository.Calls.Add(call);
        repository.OpenReviewCallIds.Add(call.Id);
        var service = new RetentionService(repository, Policy());

        await service.DeIdentifyExpiredAsync(now);

        Assert.Equal("+441632960001", call.CallerId);
        Assert.Null(call.DeIdentifiedAt);
    }

    [Fact]
    public async Task DeIdentifyExpiredAsync_LeavesANullCallerId_Null_ButStillMarksTheRowProcessed()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var call = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2), CallerId = null };
        repository.Calls.Add(call);
        var service = new RetentionService(repository, Policy());

        await service.DeIdentifyExpiredAsync(now);

        Assert.Null(call.CallerId);
        Assert.NotNull(call.DeIdentifiedAt);
    }

    [Fact]
    public async Task DeIdentifyExpiredAsync_LeavesAFreshCall_Untouched()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var call = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now, CallerId = "+441632960001" };
        repository.Calls.Add(call);
        var service = new RetentionService(repository, Policy());

        await service.DeIdentifyExpiredAsync(now);

        Assert.Equal("+441632960001", call.CallerId);
        Assert.Null(call.DeIdentifiedAt);
    }

    [Fact]
    public async Task PurgeExpiredAsync_PurgesAnExpiredCall_ButSkipsOneUnderOpenReview()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var purgeable = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2) };
        var protectedCall = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now.AddMonths(-2) };
        repository.Calls.Add(purgeable);
        repository.Calls.Add(protectedCall);
        repository.OpenReviewCallIds.Add(protectedCall.Id);
        var service = new RetentionService(repository, Policy());

        await service.PurgeExpiredAsync(now);

        Assert.True(purgeable.Purged);
        Assert.False(protectedCall.Purged);
    }

    [Fact]
    public async Task EraseVisitorAsync_DeIdentifiesTheVisitor_RegardlessOfAge()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var visitor = new FakeVisitor { Id = Guid.NewGuid(), FirstSeenAt = now }; // brand new
        repository.Visitors.Add(visitor);
        var service = new RetentionService(repository, Policy());

        await service.EraseVisitorAsync(visitor.Id, now);

        Assert.NotNull(visitor.DeIdentifiedAt);
    }

    [Fact]
    public async Task EraseVisitorAsync_DeIdentifiesTheirCalls_ButLeavesOneUnderOpenReviewUntouched()
    {
        var repository = new FakeRetentionRepository();
        var now = DateTimeOffset.UtcNow;
        var visitor = new FakeVisitor { Id = Guid.NewGuid(), FirstSeenAt = now };
        var erasableCall = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now, CallerId = "+441632960001", VisitorId = visitor.Id };
        var protectedCall = new FakeRetentionCall { Id = Guid.NewGuid(), StartedAt = now, CallerId = "+441632960002", VisitorId = visitor.Id };
        repository.Visitors.Add(visitor);
        repository.Calls.Add(erasableCall);
        repository.Calls.Add(protectedCall);
        repository.OpenReviewCallIds.Add(protectedCall.Id);
        var service = new RetentionService(repository, Policy());

        await service.EraseVisitorAsync(visitor.Id, now);

        Assert.NotEqual("+441632960001", erasableCall.CallerId);
        Assert.Equal("+441632960002", protectedCall.CallerId);
    }
}
