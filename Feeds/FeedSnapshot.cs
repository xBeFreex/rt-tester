using rt_tester.Configuration;

namespace rt_tester.Feeds;

public sealed class FeedSnapshot(AggregatorConfig config)
{
    public Dictionary<string, FeedFetchResult> VehiclePositionsByAgency { get; } = new();
    public Dictionary<string, FeedFetchResult> TripUpdatesByAgency { get; } = new();
    public Dictionary<string, FeedFetchResult> AlertsByAgency { get; } = new();

    public FeedFetchResult? MergedVehiclePositions { get; set; }
    public FeedFetchResult? MergedTripUpdates { get; set; }
    public FeedFetchResult? MergedAlerts { get; set; }

    public AggregatorConfig Config { get; } = config;

    public async Task LoadAsync(FeedClient client, TimeSpan timeout, CancellationToken ct)
    {
        var tasks = new List<Task>();

        foreach (var agency in Config.Agencies)
        {
            tasks.Add(FetchInto(client, agency.VehiclePositionsUrl, timeout, ct, r => VehiclePositionsByAgency[agency.AgencyId] = r));
            tasks.Add(FetchInto(client, agency.TripUpdatesUrl, timeout, ct, r => TripUpdatesByAgency[agency.AgencyId] = r));
            tasks.Add(FetchInto(client, agency.AlertsUrl, timeout, ct, r => AlertsByAgency[agency.AgencyId] = r));
        }

        tasks.Add(FetchInto(client, Config.Merged.VehiclePositionsUrl, timeout, ct, r => MergedVehiclePositions = r));
        tasks.Add(FetchInto(client, Config.Merged.TripUpdatesUrl, timeout, ct, r => MergedTripUpdates = r));
        tasks.Add(FetchInto(client, Config.Merged.AlertsUrl, timeout, ct, r => MergedAlerts = r));

        await Task.WhenAll(tasks);
    }

    private static async Task FetchInto(FeedClient client, string? url, TimeSpan timeout, CancellationToken ct, Action<FeedFetchResult> assign)
    {
        var result = await client.FetchAsync(url, timeout, ct);
        assign(result);
    }
}
