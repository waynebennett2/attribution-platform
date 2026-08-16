namespace Attribution.Domain.Qualification;

// FR-023: a time-of-day condition, evaluated in the website's local timezone rather than
// the platform's canonical storage timezone. StartLocal may be later than EndLocal (e.g.
// 22:00-06:00), meaning the window wraps past midnight.
public sealed record TimeOfDayWindow(TimeOnly StartLocal, TimeOnly EndLocal)
{
    public bool Contains(TimeOnly localTime) =>
        StartLocal <= EndLocal
            ? localTime >= StartLocal && localTime < EndLocal
            : localTime >= StartLocal || localTime < EndLocal;
}
