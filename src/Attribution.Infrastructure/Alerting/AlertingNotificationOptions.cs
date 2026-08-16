namespace Attribution.Infrastructure.Alerting;

// FR-047: "Notifications MUST be delivered by email and by an outbound webhook, both
// configurable per condition." Bound from the "Alerting:Notifications" config section;
// real recipients/webhook belong in appsettings.{Environment}.local.json — see spec.md's
// Dependencies section ("the customer nominates the recipients and, where they want one, a
// webhook endpoint"). A condition type with no entry in PerCondition falls back to Default,
// so a deployment that wants uniform routing need only set Default.
public sealed class AlertingNotificationOptions
{
    public AlertingDestination Default { get; set; } = new();

    public Dictionary<string, AlertingDestination> PerCondition { get; set; } = new();

    public AlertingDestination For(string conditionType) =>
        PerCondition.TryGetValue(conditionType, out var overridden) ? overridden : Default;
}

public sealed class AlertingDestination
{
    public string[] EmailRecipients { get; set; } = Array.Empty<string>();

    public string? WebhookUrl { get; set; }
}
