using rt_tester.Models;

namespace rt_tester.Detection;

/// <summary>A candidate finding produced by a comparer, before the engine assigns
/// CheckRunId/Fingerprint/FirstSeen-LastSeen bookkeeping.</summary>
public sealed record FindingDraft(
    FeedType FeedType,
    MismatchCategory Category,
    Severity Severity,
    string? AgencyId,
    string EntityKey,
    string? FieldName,
    string? SourceValue,
    string? MergedValue,
    string Explanation,
    string? LikelyValidatorCode,
    string? SourceEntityJson,
    string? MergedEntityJson);
