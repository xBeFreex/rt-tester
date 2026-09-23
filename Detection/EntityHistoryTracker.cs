using System.Collections.Concurrent;

namespace rt_tester.Detection;

/// <summary>
/// Cross-cycle state for entities tracked by the comparers. The BackgroundService runs one process
/// for the app's lifetime, so an in-memory singleton is enough to remember what happened in previous
/// poll cycles without needing DB persistence.
/// </summary>
public sealed class EntityHistoryTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _pendingSince = new();
    private readonly ConcurrentDictionary<string, long> _lastMergedTimestamp = new();

    private static string Key(FeedTypeKind kind, string agencyId, string tripId) => $"{kind}|{agencyId}|{tripId}";

    /// <summary>Records the first cycle a source entity was seen without a merged counterpart.
    /// Returns the timestamp it was first seen pending (stable across repeated calls until cleared).</summary>
    public DateTime MarkPendingIfNew(FeedTypeKind kind, string agencyId, string tripId, DateTime nowUtc)
        => _pendingSince.GetOrAdd(Key(kind, agencyId, tripId), _ => nowUtc);

    public void ClearPending(FeedTypeKind kind, string agencyId, string tripId)
        => _pendingSince.TryRemove(Key(kind, agencyId, tripId), out _);

    /// <summary>Compares a newly observed merged-feed timestamp against the highest one seen so far for
    /// this entity. Returns the previous (higher) watermark if this call represents a regression
    /// (older timestamp republished after a newer one), otherwise null. Always advances the watermark
    /// forward, never backward, so repeated regressions keep comparing against the true high point.</summary>
    public long? CheckAndUpdateWatermark(FeedTypeKind kind, string agencyId, string tripId, long observedTimestamp)
    {
        var key = Key(kind, agencyId, tripId);
        long? regressionAgainst = null;

        _lastMergedTimestamp.AddOrUpdate(key, observedTimestamp, (_, existing) =>
        {
            if (observedTimestamp < existing) regressionAgainst = existing;
            return Math.Max(existing, observedTimestamp);
        });

        return regressionAgainst;
    }
}
