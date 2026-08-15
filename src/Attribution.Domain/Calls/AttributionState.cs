namespace Attribution.Domain.Calls;

// FR-018, FR-020, FR-021: the outcome of matching a call to a session — always one of
// these three, never a probability or a best guess.
public enum AttributionState
{
    Attributed,
    Unattributed,
    Ambiguous,
}
