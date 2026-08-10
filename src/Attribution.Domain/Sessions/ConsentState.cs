namespace Attribution.Domain.Sessions;

// FR-039: a visitor's consent state.
public enum ConsentState
{
    Pending,
    Granted,
    Withdrawn,
}
