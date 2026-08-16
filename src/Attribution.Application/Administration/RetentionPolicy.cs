namespace Attribution.Application.Administration;

// FR-040's stated defaults, bound from the "Retention" config section — every period is
// independently configurable per deployment, as FR-040 requires ("a configurable retention
// period per data category"). HmacKey is the secret behind the "stable non-reversible
// surrogate" research.md §10 calls for: a keyed HMAC of an identifier (a phone number, an
// ad-platform conversion id) produces the same surrogate every time that same value is
// re-encountered, preserving the joins FR-019's evidence chain and SC-014's report
// reconciliation depend on, without the value being recoverable from the surrogate alone.
public sealed class RetentionPolicy
{
    public int VisitorSessionDeIdentifyAfterMonths { get; set; } = 14;

    public int CallRecordDeIdentifyAfterMonths { get; set; } = 14;

    public int CallRecordPurgeAfterMonths { get; set; } = 25;

    public int AuditLogRetentionYears { get; set; } = 7;

    public string HmacKey { get; set; } = string.Empty;
}
