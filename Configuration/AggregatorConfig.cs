namespace rt_tester.Configuration;

public sealed class AggregatorConfig
{
    public int PollIntervalSeconds { get; set; } = 30;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public int StaleThresholdSeconds { get; set; } = 90;
    public int HistoryRetentionHours { get; set; } = 72;

    /// <summary>Timestamp/position drift up to this many seconds between a source entity and its
    /// merged counterpart is treated as normal poll-cycle lag (the merged feed's own aggregator polls
    /// sources on its own schedule), not a merge-introduced mutation.</summary>
    public int MergeLagToleranceSeconds { get; set; } = 60;

    /// <summary>A source entity not yet matched in the merged feed is only reported as MissingInMerged
    /// once it has been absent for longer than this - avoids false positives from the merged feed simply
    /// not having caught up yet.</summary>
    public int MissingInMergedGraceSeconds { get; set; } = 90;

    /// <summary>Any entity in the merged feed whose own timestamp is older than this is flagged as a
    /// stuck/ghost entity - the merge step never expired it, regardless of whether it still matches a
    /// source entity (the source may have long since stopped serving that trip).</summary>
    public int GhostEntityThresholdHours { get; set; } = 6;
    public string PrefixSeparator { get; set; } = "_";
    public List<AgencyConfig> Agencies { get; set; } = new();
    public MergedConfig Merged { get; set; } = new();
}

public sealed class AgencyConfig
{
    public string AgencyId { get; set; } = "";
    public string? AgencyName { get; set; }
    public string? VehiclePositionsUrl { get; set; }
    public string? TripUpdatesUrl { get; set; }
    public string? AlertsUrl { get; set; }
}

public sealed class MergedConfig
{
    public string? VehiclePositionsUrl { get; set; }
    public string? TripUpdatesUrl { get; set; }
    public string? AlertsUrl { get; set; }
}
