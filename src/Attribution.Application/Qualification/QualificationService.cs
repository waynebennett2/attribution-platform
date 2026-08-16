using Attribution.Domain.Calls;
using Attribution.Domain.Qualification;
using Attribution.Domain.Sessions;
using Attribution.Domain.Websites;
using DomainAttribution = Attribution.Domain.Calls.Attribution;

namespace Attribution.Application.Qualification;

// FR-022-FR-024: judges an attributed call against whichever qualification rule is in
// force for its website/campaign at the call's start time — campaign scope wins over
// website scope, which wins over the platform default — and records which rule version
// and scope actually judged it.
public sealed class QualificationService
{
    private readonly IQualificationRuleRepository _ruleRepository;
    private readonly IQualificationResultRepository _resultRepository;
    private readonly ISessionRepository _sessionRepository;
    private readonly IWebsiteRepository _websiteRepository;

    public QualificationService(
        IQualificationRuleRepository ruleRepository,
        IQualificationResultRepository resultRepository,
        ISessionRepository sessionRepository,
        IWebsiteRepository websiteRepository)
    {
        _ruleRepository = ruleRepository;
        _resultRepository = resultRepository;
        _sessionRepository = sessionRepository;
        _websiteRepository = websiteRepository;
    }

    public async Task<QualificationResult> QualifyAsync(Call call, DomainAttribution attribution, DateTimeOffset now)
    {
        if (attribution.State != AttributionState.Attributed || attribution.SessionId is null)
        {
            // data-model.md: qualification only ever runs against an attributed call.
            throw new InvalidOperationException("Only an attributed call can be qualified.");
        }

        var session = await _sessionRepository.GetByIdAsync(attribution.SessionId.Value);
        var website = session is not null ? await _websiteRepository.GetByIdAsync(session.WebsiteId) : null;

        var rule = await ResolveActiveRuleAsync(session, website, call.StartedAt);
        var timeZone = ResolveTimeZone(website);

        var isQualified = RuleEvaluator.Evaluate(rule.Conditions, call, timeZone);
        var result = QualificationResult.Decide(call.Id, attribution.Id, rule.Id, isQualified, now);
        await _resultRepository.AddAsync(result);
        return result;
    }

    // FR-024: campaign scope is more specific than website scope, which is more specific
    // than the platform default; "most specific in-force wins".
    private async Task<QualificationRule> ResolveActiveRuleAsync(Session? session, Website? website, DateTimeOffset instant)
    {
        if (session?.Arrival.UtmCampaign is { Length: > 0 } campaign)
        {
            var campaignRule = await _ruleRepository.GetInForceAsync(QualificationScopeType.Campaign, campaign, instant);
            if (campaignRule is not null)
            {
                return campaignRule;
            }
        }

        if (website is not null)
        {
            var websiteRule = await _ruleRepository.GetInForceAsync(QualificationScopeType.Website, website.Id.ToString(), instant);
            if (websiteRule is not null)
            {
                return websiteRule;
            }
        }

        return await _ruleRepository.GetInForceAsync(QualificationScopeType.Default, null, instant)
            ?? throw new InvalidOperationException("No platform default qualification rule is in force.");
    }

    private static TimeZoneInfo ResolveTimeZone(Website? website) =>
        website is null ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(website.LocalTimezone);
}
