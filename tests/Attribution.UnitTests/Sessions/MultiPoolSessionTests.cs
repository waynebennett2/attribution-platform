using System;
using System.Linq;
using System.Threading.Tasks;
using Attribution.Application.Allocation;
using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.UnitTests.TestSupport;
using Xunit;

namespace Attribution.UnitTests.Sessions;

// FR-050, research.md §15: a multi-pool session holds more than one concurrently active
// Allocation (one per matched pool, distinct pool_id_at_allocation), and a later page view
// that matches a pool the session doesn't yet hold grows that same session rather than
// starting a second one.
public class MultiPoolSessionTests
{
    private static (Website Website, NumberPool PoolA, NumberPool PoolB, NumberPool PoolC, AllocationService Service, FakeAtomicAllocator Allocator, FakeAllocationRepository Allocations, FakeSessionRepository Sessions) Build()
    {
        var website = Website.Create("directory-site", new[] { "https://example.com" }, "01632 960000", "Europe/London");
        website.EnableMultiPool();

        var poolA = NumberPool.Create("Location A", "website", website.Id, "01632 960001");
        var poolB = NumberPool.Create("Location B", "website", website.Id, "01632 960002");
        var poolC = NumberPool.Create("Location C", "website", website.Id, "01632 960003");

        var websiteRepository = new FakeWebsiteRepository();
        websiteRepository.Websites.Add(website);
        var poolRepository = new FakeNumberPoolRepository();
        poolRepository.Pools.Add(poolA);
        poolRepository.Pools.Add(poolB);
        poolRepository.Pools.Add(poolC);

        var numberRepository = new FakeTrackingNumberRepository();
        var numberA = TrackingNumber.Create(poolA.Id, "+441632900001");
        var numberB = TrackingNumber.Create(poolB.Id, "+441632900002");
        var numberC = TrackingNumber.Create(poolC.Id, "+441632900003");
        numberRepository.Numbers.Add(numberA);
        numberRepository.Numbers.Add(numberB);
        numberRepository.Numbers.Add(numberC);

        var sessionRepository = new FakeSessionRepository();
        var allocationRepository = new FakeAllocationRepository();
        var allocator = new FakeAtomicAllocator(sessionRepository, allocationRepository);
        allocator.AvailableNumbers.Add(numberA);
        allocator.AvailableNumbers.Add(numberB);
        allocator.AvailableNumbers.Add(numberC);

        var service = new AllocationService(
            websiteRepository, poolRepository, numberRepository, sessionRepository, allocationRepository, allocator);

        return (website, poolA, poolB, poolC, service, allocator, allocationRepository, sessionRepository);
    }

    [Fact]
    public async Task ASessionHoldingTwoPools_HasDistinctPoolIdAtAllocation_OnEachRow()
    {
        var (website, poolA, poolB, _, service, _, allocations, _) = Build();

        var result = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow,
            matchedPoolIds: new[] { poolA.Id, poolB.Id });

        var sessionAllocations = allocations.Allocations.Where(a => a.SessionId == result.SessionId).ToList();
        Assert.Equal(2, sessionAllocations.Count);
        Assert.Equal(sessionAllocations.Count, sessionAllocations.Select(a => a.PoolIdAtAllocation).Distinct().Count());
    }

    [Fact]
    public async Task ALaterPageView_MatchingANewPool_GrowsTheExistingSession_RatherThanStartingANewOne()
    {
        var (website, poolA, poolB, _, service, _, allocations, sessions) = Build();
        var now = DateTimeOffset.UtcNow;

        var first = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now, matchedPoolIds: new[] { poolA.Id });
        Assert.NotNull(first.SessionId);
        Assert.Single(sessions.Sessions);

        // A second page view matches a pool the session doesn't yet hold; the client
        // supplies the existing session_id (research.md §15).
        var second = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now.AddMinutes(1),
            matchedPoolIds: new[] { poolB.Id }, existingSessionId: first.SessionId);

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Single(sessions.Sessions); // still one session, not two
        var grown = Assert.Single(second.Allocations!);
        Assert.Equal(poolB.Id, grown.PoolId);

        var sessionAllocations = allocations.Allocations.Where(a => a.SessionId == first.SessionId).ToList();
        Assert.Equal(2, sessionAllocations.Count);
        Assert.Contains(sessionAllocations, a => a.PoolIdAtAllocation == poolA.Id);
        Assert.Contains(sessionAllocations, a => a.PoolIdAtAllocation == poolB.Id);
    }

    [Fact]
    public async Task ALaterPageView_MatchingAPoolTheSessionAlreadyHolds_DoesNotDuplicateTheAllocation()
    {
        var (website, poolA, _, _, service, _, allocations, sessions) = Build();
        var now = DateTimeOffset.UtcNow;

        var first = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now, matchedPoolIds: new[] { poolA.Id });

        var second = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now.AddMinutes(1),
            matchedPoolIds: new[] { poolA.Id }, existingSessionId: first.SessionId);

        Assert.Equal(first.SessionId, second.SessionId);
        Assert.Empty(second.Allocations!); // nothing new — pool A was already held
        Assert.Single(sessions.Sessions);
        Assert.Single(allocations.Allocations, a => a.SessionId == first.SessionId);
    }

    [Fact]
    public async Task AnExpiredSessionId_IsTreatedAsAbsent_AndStartsAFreshSession()
    {
        var (website, poolA, poolB, _, service, _, _, sessions) = Build();
        var now = DateTimeOffset.UtcNow;

        var first = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now, matchedPoolIds: new[] { poolA.Id });
        var originalSession = sessions.Sessions.Single();
        originalSession.EndByTimeout(now.AddMinutes(5));

        var second = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, now.AddMinutes(10),
            matchedPoolIds: new[] { poolB.Id }, existingSessionId: first.SessionId);

        Assert.NotEqual(first.SessionId, second.SessionId);
        Assert.Equal(2, sessions.Sessions.Count);
    }
}
