namespace Attribution.Domain.Audit;

// FR-047: the operational conditions the platform actively monitors.
public enum AlertConditionType
{
    IngestionLag,
    PublicationFailureRate,
    AllocationFailureRate,
    PoolUtilisation,
    ReviewCaseAge,
}
