namespace Attribution.Domain.Qualification;

// FR-024: a qualification rule's scope — the platform-wide default, or an override for a
// specific website or campaign.
public enum QualificationScopeType
{
    Default,
    Website,
    Campaign,
}
