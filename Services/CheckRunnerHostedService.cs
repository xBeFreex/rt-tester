using System.Text.Json;
using Microsoft.Extensions.Options;
using rt_tester.Configuration;
using rt_tester.Detection;
using rt_tester.Feeds;

namespace rt_tester.Services;

public sealed class CheckRunnerHostedService(
    IOptions<AggregatorConfig> config,
    FeedClient feedClient,
    MergeDiagnosticsEngine engine,
    FindingStore findingStore,
    ILogger<CheckRunnerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var cfg = config.Value;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(cfg.PollIntervalSeconds));

        do
        {
            try
            {
                await RunOnceAsync(cfg, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error during merge-diagnostics check cycle");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(AggregatorConfig cfg, CancellationToken ct)
    {
        var startedAt = DateTime.UtcNow;
        var runId = await findingStore.StartRunAsync(startedAt, ct);

        var snapshot = new FeedSnapshot(cfg);
        await snapshot.LoadAsync(feedClient, TimeSpan.FromSeconds(cfg.HttpTimeoutSeconds), ct);

        var findings = engine.Run(snapshot, runId, DateTime.UtcNow);

        var fetchStatus = BuildFetchStatusJson(snapshot);
        await findingStore.SaveFindingsAndCompleteRunAsync(runId, findings, fetchStatus, DateTime.UtcNow, ct);

        logger.LogInformation("Check cycle complete: {FindingCount} findings", findings.Count);
    }

    private static string BuildFetchStatusJson(FeedSnapshot snapshot)
    {
        var entries = new List<object>();

        void Add(string label, FeedFetchResult? r)
        {
            if (r is null) return;
            entries.Add(new { label, url = r.Url, ok = r.Ok, statusCode = r.StatusCode, error = r.Error, entityCount = r.Feed?.Entity.Count });
        }

        foreach (var (agency, r) in snapshot.VehiclePositionsByAgency) Add($"{agency}/vehicle-positions", r);
        foreach (var (agency, r) in snapshot.TripUpdatesByAgency) Add($"{agency}/trip-updates", r);
        foreach (var (agency, r) in snapshot.AlertsByAgency) Add($"{agency}/alerts", r);
        Add("merged/vehicle-positions", snapshot.MergedVehiclePositions);
        Add("merged/trip-updates", snapshot.MergedTripUpdates);
        Add("merged/alerts", snapshot.MergedAlerts);

        return JsonSerializer.Serialize(entries);
    }
}
