namespace Attribution.Application.Administration;

// FR-047: every threshold here is independently configurable per condition (bound from the
// host's "Alerting" config section); these are only the shipped defaults. ReviewCaseAge's
// 48h default is the one FR-036 states explicitly — the others have no spec-mandated
// number, so these are chosen to warn with enough lead time to act (FR-034's "warn before
// a pool is exhausted") without flagging normal transient noise as an outage.
public sealed class AlertingThresholds
{
    public TimeSpan IngestionLag { get; set; } = TimeSpan.FromHours(2);

    public double PublicationFailureRate { get; set; } = 0.1;

    public double PoolUtilisation { get; set; } = 0.9;

    public TimeSpan ReviewCaseAge { get; set; } = TimeSpan.FromHours(48);

    // How often an already-open alert re-notifies while the condition persists (FR-047:
    // "repeat at a configurable interval until the condition clears or an administrator
    // acknowledges it").
    public TimeSpan RepeatNotificationInterval { get; set; } = TimeSpan.FromHours(1);
}
