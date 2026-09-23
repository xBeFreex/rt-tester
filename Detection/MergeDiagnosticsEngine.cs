using System.Security.Cryptography;
using System.Text;
using rt_tester.Feeds;
using rt_tester.Models;
using TransitRealtime;

namespace rt_tester.Detection;

public sealed class MergeDiagnosticsEngine(
    VehiclePositionComparer vpComparer,
    TripUpdateComparer tuComparer,
    AlertComparer alertComparer)
{
    public List<Finding> Run(FeedSnapshot snapshot, Guid checkRunId, DateTime nowUtc)
    {
        var drafts = new List<FindingDraft>();

        void AddFetchErrors(FeedType feedType, IEnumerable<FeedFetchResult> results)
        {
            foreach (var result in results.Where(r => !r.Ok))
            {
                drafts.Add(new FindingDraft(
                    feedType, MismatchCategory.FetchError, Severity.Info,
                    null, result.Url, null, null, null,
                    $"Failed to fetch/decode feed at '{result.Url}': {result.Error}", null, null, null));
            }
        }

        AddFetchErrors(FeedType.VehiclePositions, snapshot.VehiclePositionsByAgency.Values);
        AddFetchErrors(FeedType.TripUpdates, snapshot.TripUpdatesByAgency.Values);
        AddFetchErrors(FeedType.Alerts, snapshot.AlertsByAgency.Values);
        if (snapshot.MergedVehiclePositions is { Ok: false }) AddFetchErrors(FeedType.VehiclePositions, [snapshot.MergedVehiclePositions]);
        if (snapshot.MergedTripUpdates is { Ok: false }) AddFetchErrors(FeedType.TripUpdates, [snapshot.MergedTripUpdates]);
        if (snapshot.MergedAlerts is { Ok: false }) AddFetchErrors(FeedType.Alerts, [snapshot.MergedAlerts]);

        if (snapshot.MergedVehiclePositions is { Ok: true, Feed: not null } mvp)
        {
            var sources = CollectOkSources(snapshot.VehiclePositionsByAgency);
            if (sources.Count > 0)
                drafts.AddRange(vpComparer.Compare(mvp.Feed, sources));
        }

        if (snapshot.MergedTripUpdates is { Ok: true, Feed: not null } mtu)
        {
            var sources = CollectOkSources(snapshot.TripUpdatesByAgency);
            if (sources.Count > 0)
                drafts.AddRange(tuComparer.Compare(mtu.Feed, sources));
        }

        if (snapshot.MergedAlerts is { Ok: true, Feed: not null } malerts)
        {
            var sources = CollectOkSources(snapshot.AlertsByAgency);
            if (sources.Count > 0)
                drafts.AddRange(alertComparer.Compare(malerts.Feed, sources));
        }

        return drafts.Select(d => ToFinding(d, checkRunId, nowUtc)).ToList();
    }

    private static Dictionary<string, FeedMessage> CollectOkSources(Dictionary<string, FeedFetchResult> byAgency)
        => byAgency.Where(kv => kv.Value is { Ok: true, Feed: not null })
                   .ToDictionary(kv => kv.Key, kv => kv.Value.Feed!);

    private static Finding ToFinding(FindingDraft d, Guid checkRunId, DateTime nowUtc)
    {
        var fingerprint = ComputeFingerprint(d);
        return new Finding
        {
            CheckRunId = checkRunId,
            FirstSeenUtc = nowUtc,
            LastSeenUtc = nowUtc,
            FeedType = d.FeedType,
            Category = d.Category,
            Severity = d.Severity,
            AgencyId = d.AgencyId,
            EntityKey = d.EntityKey,
            FieldName = d.FieldName,
            SourceValue = d.SourceValue,
            MergedValue = d.MergedValue,
            Explanation = d.Explanation,
            LikelyValidatorCode = d.LikelyValidatorCode,
            SourceEntityJson = d.SourceEntityJson,
            MergedEntityJson = d.MergedEntityJson,
            Fingerprint = fingerprint
        };
    }

    private static string ComputeFingerprint(FindingDraft d)
    {
        var raw = $"{d.FeedType}|{d.Category}|{d.EntityKey}|{d.FieldName}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
