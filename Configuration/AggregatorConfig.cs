namespace rt_tester.Configuration;

public sealed class AggregatorConfig
{
    public int PollIntervalSeconds { get; set; } = 30;
    public int HttpTimeoutSeconds { get; set; } = 20;
    public int StaleThresholdSeconds { get; set; } = 90;
    public int HistoryRetentionHours { get; set; } = 72;
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
