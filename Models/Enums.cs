namespace rt_tester.Models;

public enum FeedType
{
    VehiclePositions,
    TripUpdates,
    Alerts
}

public enum MismatchCategory
{
    InconsistentIdPrefix,
    PrefixCollision,
    DuplicatedInMerged,
    MissingInMerged,
    FieldMutated,
    MergeIntroducedStaleTimestamp,
    BadStartTimeIntroduced,
    FetchError
}

public enum Severity
{
    Info,
    Warning,
    Error
}
