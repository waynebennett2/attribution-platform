namespace Attribution.Domain.Sessions;

// FR-014: whether the session's arrival details are the visitor's genuine first-touch
// data (Ordinary), or were no longer recoverable because consent arrived after the
// visitor navigated away from their entry page (Degraded).
public enum SessionProvenance
{
    Ordinary,
    Degraded,
}
