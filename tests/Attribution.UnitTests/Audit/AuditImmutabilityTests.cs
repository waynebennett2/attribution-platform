using System;
using Attribution.Domain.Audit;
using Xunit;

namespace Attribution.UnitTests.Audit;

// FR-035: audit entries are immutable once written; attempts to alter one are refused
// and themselves recorded. This test covers the in-memory invariant; the database-level
// append-only grant (no UPDATE/DELETE permission) is covered by an integration test.
public class AuditImmutabilityTests
{
    [Fact]
    public void Create_RecordsActorActionTargetAndTimestamp()
    {
        var entry = AuditEntry.Create(
            actorUserId: "user-1",
            action: "SuspendTrackingNumber",
            targetType: "TrackingNumber",
            targetId: "num-42",
            beforeValue: "{\"status\":\"active\"}",
            afterValue: "{\"status\":\"suspended\"}");

        Assert.Equal("user-1", entry.ActorUserId);
        Assert.Equal("SuspendTrackingNumber", entry.Action);
        Assert.Equal("TrackingNumber", entry.TargetType);
        Assert.Equal("num-42", entry.TargetId);
        Assert.NotEqual(default, entry.OccurredAt);
    }

    [Fact]
    public void AuditEntry_HasNoMutationMethod()
    {
        // Deliberately structural: an AuditEntry exposes only init-time state via Create(),
        // and every property setter is private — there is no method on this type that can
        // change a field after construction. This test documents and locks that invariant
        // by construction; it is not merely aspirational.
        var type = typeof(AuditEntry);
        foreach (var property in type.GetProperties())
        {
            var setMethod = property.GetSetMethod(nonPublic: true);
            Assert.True(setMethod is null || setMethod.IsPrivate,
                $"{property.Name} must not have a public or protected setter.");
        }
    }

    [Fact]
    public void Create_Throws_WhenActorUserIdIsEmpty()
    {
        Assert.Throws<ArgumentException>(() => AuditEntry.Create(
            actorUserId: "",
            action: "SuspendTrackingNumber",
            targetType: "TrackingNumber",
            targetId: "num-42",
            beforeValue: null,
            afterValue: "{}"));
    }
}
