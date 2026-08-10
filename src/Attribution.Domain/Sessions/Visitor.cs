namespace Attribution.Domain.Sessions;

// spec.md Key Entities §Visitor: an anonymous returning individual, identified across
// sessions on one website.
public class Visitor
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid WebsiteId { get; private set; }
    public DateTimeOffset FirstSeenAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeIdentifiedAt { get; private set; }

    private Visitor() { }

    public static Visitor Create(Guid websiteId) => new() { WebsiteId = websiteId };

    internal static Visitor Rehydrate(Guid id, Guid websiteId, DateTimeOffset firstSeenAt, DateTimeOffset? deIdentifiedAt) =>
        new() { Id = id, WebsiteId = websiteId, FirstSeenAt = firstSeenAt, DeIdentifiedAt = deIdentifiedAt };
}
