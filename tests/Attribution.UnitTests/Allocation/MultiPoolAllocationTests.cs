using System;
using System.Linq;
using System.Threading.Tasks;
using Attribution.Application.Allocation;
using Attribution.Domain.Pools;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using Attribution.UnitTests.TestSupport;
using Xunit;

namespace Attribution.UnitTests.Allocation;

// FR-003, FR-050: a multi-pool-enabled website allocates one Tracking Number per matched
// pool independently — no cross-pool interference, and one pool's exhaustion never blocks
// another pool's allocation on the same request.
public class MultiPoolAllocationTests
{
    private static (Website Website, NumberPool PoolA, NumberPool PoolB, AllocationService Service, FakeAtomicAllocator Allocator, FakeTrackingNumberRepository Numbers) Build()
    {
        var website = Website.Create("multi-pool-site", new[] { "https://example.com" }, "01632 960000", "Europe/London");
        website.EnableMultiPool();

        var poolA = NumberPool.Create("Location A", "website", website.Id, "01632 960001");
        var poolB = NumberPool.Create("Location B", "website", website.Id, "01632 960002");

        var websiteRepository = new FakeWebsiteRepository();
        websiteRepository.Websites.Add(website);

        var poolRepository = new FakeNumberPoolRepository();
        poolRepository.Pools.Add(poolA);
        poolRepository.Pools.Add(poolB);

        var numberRepository = new FakeTrackingNumberRepository();
        var numberA = TrackingNumber.Create(poolA.Id, "+441632900001");
        var numberB = TrackingNumber.Create(poolB.Id, "+441632900002");
        numberRepository.Numbers.Add(numberA);
        numberRepository.Numbers.Add(numberB);

        var sessionRepository = new FakeSessionRepository();
        var allocationRepository = new FakeAllocationRepository();
        var allocator = new FakeAtomicAllocator(sessionRepository, allocationRepository);
        allocator.AvailableNumbers.Add(numberA);
        allocator.AvailableNumbers.Add(numberB);

        var service = new AllocationService(
            websiteRepository, poolRepository, numberRepository,
            sessionRepository, allocationRepository, allocator);

        return (website, poolA, poolB, service, allocator, numberRepository);
    }

    [Fact]
    public async Task AllocateAsync_WithTwoMatchedPools_AllocatesOneDistinctNumberPerPool()
    {
        var (website, poolA, poolB, service, allocator, _) = Build();

        var result = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow,
            matchedPoolIds: new[] { poolA.Id, poolB.Id });

        Assert.NotNull(result.SessionId);
        Assert.Equal(2, result.Allocations!.Count);
        Assert.Contains(result.Allocations, a => a.PoolId == poolA.Id && a.Number == "+441632900001");
        Assert.Contains(result.Allocations, a => a.PoolId == poolB.Id && a.Number == "+441632900002");

        // FR-003 applied per pool: two distinct Tracking Numbers, never the same one twice.
        Assert.Equal(2, allocator.Allocations.Select(a => a.TrackingNumberId).Distinct().Count());
        // Every allocation belongs to a different pool — no double-allocation within a pool.
        Assert.Equal(2, allocator.Allocations.Select(a => a.PoolIdAtAllocation).Distinct().Count());
    }

    [Fact]
    public async Task AllocateAsync_OnePoolExhausted_StillAllocatesTheOtherPool_AndOmitsTheExhaustedOneFromAllocations()
    {
        var (website, poolA, poolB, service, allocator, _) = Build();
        allocator.AvailableNumbers.RemoveAll(n => n.PoolId == poolA.Id); // pool A has nothing left

        var result = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow,
            matchedPoolIds: new[] { poolA.Id, poolB.Id });

        Assert.NotNull(result.SessionId); // pool B's success still creates the session
        var allocation = Assert.Single(result.Allocations!);
        Assert.Equal(poolB.Id, allocation.PoolId);
    }

    [Fact]
    public async Task AllocateAsync_EveryRequestedPoolExhausted_CreatesNoSession()
    {
        var (website, poolA, poolB, service, allocator, _) = Build();
        allocator.AvailableNumbers.Clear();

        var result = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow,
            matchedPoolIds: new[] { poolA.Id, poolB.Id });

        Assert.Null(result.SessionId);
        Assert.Equal("pool_exhausted", result.Reason);
        Assert.Empty(allocator.Sessions);
    }

    [Fact]
    public async Task AllocateAsync_AlwaysReturnsThePoolToNumberMap_RegardlessOfConsentOrMatching()
    {
        var (website, poolA, poolB, service, _, _) = Build();

        var beforeConsent = await service.AllocateAsync(website.Id, consentGranted: false, ArrivalDetails.Empty, DateTimeOffset.UtcNow);
        Assert.Equal(2, beforeConsent.Pools!.Count);
        Assert.Contains(beforeConsent.Pools, p => p.PoolId == poolA.Id && p.DefaultNumber == "01632 960001");
        Assert.Contains(beforeConsent.Pools, p => p.PoolId == poolB.Id && p.DefaultNumber == "01632 960002");
        Assert.Equal("no_consent", beforeConsent.Reason);

        var pendingMatch = await service.AllocateAsync(website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow);
        Assert.Equal(2, pendingMatch.Pools!.Count);
        Assert.Equal("pending_match", pendingMatch.Reason);
        Assert.Null(pendingMatch.SessionId);
    }

    [Fact]
    public async Task AllocateAsync_DropsAMatchedPoolId_NotActuallyScopedToTheRequestingWebsite()
    {
        var (website, poolA, _, service, allocator, numberRepository) = Build();
        var foreignPoolId = Guid.NewGuid();
        var foreignNumber = TrackingNumber.Create(foreignPoolId, "+441632900099");
        numberRepository.Numbers.Add(foreignNumber);
        allocator.AvailableNumbers.Add(foreignNumber);

        var result = await service.AllocateAsync(
            website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow,
            matchedPoolIds: new[] { poolA.Id, foreignPoolId });

        var allocation = Assert.Single(result.Allocations!);
        Assert.Equal(poolA.Id, allocation.PoolId);
        Assert.DoesNotContain(allocator.Allocations, a => a.PoolIdAtAllocation == foreignPoolId);
    }

    [Fact]
    public async Task AllocateAsync_SingleNonMultiPoolWebsite_IsUnaffected_AndCarriesNoPoolsField()
    {
        var website = Website.Create("single-pool-site", new[] { "https://example.com" }, "01632 960000", "Europe/London");
        var pool = NumberPool.Create("Only pool", "website", website.Id);
        var number = TrackingNumber.Create(pool.Id, "+441632900010");

        var websiteRepository = new FakeWebsiteRepository();
        websiteRepository.Websites.Add(website);
        var poolRepository = new FakeNumberPoolRepository();
        poolRepository.Pools.Add(pool);
        var numberRepository = new FakeTrackingNumberRepository();
        numberRepository.Numbers.Add(number);
        var sessionRepository = new FakeSessionRepository();
        var allocationRepository = new FakeAllocationRepository();
        var allocator = new FakeAtomicAllocator(sessionRepository, allocationRepository);
        allocator.AvailableNumbers.Add(number);

        var service = new AllocationService(
            websiteRepository, poolRepository, numberRepository, sessionRepository, allocationRepository, allocator);

        var result = await service.AllocateAsync(website.Id, consentGranted: true, ArrivalDetails.Empty, DateTimeOffset.UtcNow);

        Assert.Equal("+441632900010", result.Number);
        Assert.Null(result.Pools);
        Assert.Null(result.Allocations);
    }
}
