using Google.Protobuf;
using rt_tester.Models;
using TransitRealtime;

namespace rt_tester.Detection;

public sealed class TripUpdateComparer(PrefixResolver resolver)
{
    public List<FindingDraft> Compare(FeedMessage merged, Dictionary<string, FeedMessage> sourcesByAgency)
    {
        var findings = new List<FindingDraft>();
        var sourceIndexByAgency = sourcesByAgency.ToDictionary(kv => kv.Key, kv => EntityIndex.BuildTripUpdates(kv.Value));
        var mergedEntities = merged.Entity.Where(e => e.TripUpdate is not null).ToList();
        var covered = new HashSet<(string Agency, string TripId)>();

        DetectDuplicatesAndCollisions(mergedEntities, sourceIndexByAgency, findings);

        foreach (var entity in mergedEntities)
        {
            var tu = entity.TripUpdate;
            var tripId = tu.Trip?.TripId;

            var firstStopId = tu.StopTimeUpdate.FirstOrDefault(s => !string.IsNullOrEmpty(s.StopId))?.StopId;
            var tripResolved = resolver.Resolve(tripId);
            var stopResolved = resolver.Resolve(firstStopId);

            string? expectedAgency = null;
            string? originalTripId = tripId;

            if (tripResolved is not null)
            {
                expectedAgency = tripResolved.AgencyId;
                originalTripId = tripResolved.OriginalId;

                foreach (var stu in tu.StopTimeUpdate)
                {
                    if (string.IsNullOrEmpty(stu.StopId)) continue;
                    var stuResolved = resolver.Resolve(stu.StopId);
                    if (stuResolved is null || stuResolved.AgencyId != expectedAgency)
                    {
                        findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.InconsistentIdPrefix, Severity.Error,
                            expectedAgency, entity.Id, "stop_time_update.stop_id", stu.StopId, stu.StopId,
                            $"Merged TripUpdate '{entity.Id}' has trip_id prefixed for agency '{expectedAgency}' but " +
                            $"stop_time_update.stop_id '{stu.StopId}' was not prefixed consistently. Explains UNKNOWN_TRIP_ID.",
                            "UNKNOWN_TRIP_ID", null, JsonFormatter.Default.Format(entity)));
                        break;
                    }
                }
            }
            else if (stopResolved is not null && !string.IsNullOrEmpty(tripId))
            {
                expectedAgency = stopResolved.AgencyId;
                findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.InconsistentIdPrefix, Severity.Error,
                    expectedAgency, entity.Id, "trip_id", tripId, tripId,
                    $"Merged TripUpdate '{entity.Id}' has stop_time_update ids prefixed for agency '{expectedAgency}' but " +
                    $"trip_id '{tripId}' was left unprefixed.",
                    "UNKNOWN_TRIP_ID", null, JsonFormatter.Default.Format(entity)));
            }

            if (expectedAgency is null) continue;
            if (!sourceIndexByAgency.TryGetValue(expectedAgency, out var sourceIndex)) continue;

            var sourceEntity = sourceIndex.FindTripUpdate(originalTripId, entity.Id);
            if (sourceEntity is null) continue;

            if (!string.IsNullOrEmpty(originalTripId))
                covered.Add((expectedAgency, originalTripId));

            CompareFields(expectedAgency, entity, sourceEntity, findings);
        }

        DetectMissingInMerged(sourcesByAgency, covered, findings);
        return findings;
    }

    private void CompareFields(string agencyId, FeedEntity mergedEntity, FeedEntity sourceEntity, List<FindingDraft> findings)
    {
        var merged = mergedEntity.TripUpdate;
        var source = sourceEntity.TripUpdate;
        var mergedJson = JsonFormatter.Default.Format(mergedEntity);
        var sourceJson = JsonFormatter.Default.Format(sourceEntity);

        var sourceStart = $"{source.Trip?.StartDate}T{source.Trip?.StartTime}";
        var mergedStart = $"{merged.Trip?.StartDate}T{merged.Trip?.StartTime}";
        var sourceHasStart = !string.IsNullOrEmpty(source.Trip?.StartDate) || !string.IsNullOrEmpty(source.Trip?.StartTime);
        var mergedHasStart = !string.IsNullOrEmpty(merged.Trip?.StartDate) || !string.IsNullOrEmpty(merged.Trip?.StartTime);

        if (sourceHasStart && !mergedHasStart)
        {
            findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.BadStartTimeIntroduced, Severity.Warning,
                agencyId, mergedEntity.Id, "trip.start_date/start_time", sourceStart, mergedStart,
                $"Source trip descriptor for TripUpdate '{mergedEntity.Id}' had a valid start_date/start_time but the " +
                "merged entity's is empty/malformed - likely explains VEHICLE_POSITION_BAD_START_TIME.",
                "VEHICLE_POSITION_BAD_START_TIME", sourceJson, mergedJson));
        }
        else if (sourceHasStart && mergedHasStart && sourceStart != mergedStart)
        {
            findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.FieldMutated, Severity.Warning,
                agencyId, mergedEntity.Id, "trip.start_date/start_time", sourceStart, mergedStart,
                $"Merge altered trip start_date/start_time for TripUpdate '{mergedEntity.Id}' (agency '{agencyId}').",
                "VEHICLE_POSITION_BAD_START_TIME", sourceJson, mergedJson));
        }

        var sourceStops = source.StopTimeUpdate.ToList();
        var mergedStops = merged.StopTimeUpdate.ToList();
        if (sourceStops.Count != mergedStops.Count)
        {
            findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.FieldMutated, Severity.Error,
                agencyId, mergedEntity.Id, "stop_time_update.count", sourceStops.Count.ToString(), mergedStops.Count.ToString(),
                $"Merge changed the number of stop_time_update entries for TripUpdate '{mergedEntity.Id}' " +
                $"(source had {sourceStops.Count}, merged has {mergedStops.Count}) - stops were dropped or duplicated.",
                null, sourceJson, mergedJson));
            return;
        }

        for (var i = 0; i < sourceStops.Count; i++)
        {
            var s = sourceStops[i];
            var m = mergedStops[i];
            if (s.StopSequence != m.StopSequence)
            {
                findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.FieldMutated, Severity.Warning,
                    agencyId, mergedEntity.Id, $"stop_time_update[{i}].stop_sequence", s.StopSequence.ToString(), m.StopSequence.ToString(),
                    $"Merge altered stop_sequence at index {i} for TripUpdate '{mergedEntity.Id}'.",
                    null, sourceJson, mergedJson));
            }

            if (s.Arrival?.Time != m.Arrival?.Time || s.Departure?.Time != m.Departure?.Time)
            {
                findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.FieldMutated, Severity.Warning,
                    agencyId, mergedEntity.Id, $"stop_time_update[{i}].arrival/departure", $"{s.Arrival?.Time}/{s.Departure?.Time}",
                    $"{m.Arrival?.Time}/{m.Departure?.Time}",
                    $"Merge altered arrival/departure time at stop index {i} for TripUpdate '{mergedEntity.Id}'.",
                    null, sourceJson, mergedJson));
            }
        }
    }

    private void DetectDuplicatesAndCollisions(
        List<FeedEntity> mergedEntities, Dictionary<string, EntityIndex> sourceIndexByAgency, List<FindingDraft> findings)
    {
        foreach (var group in mergedEntities.GroupBy(e => e.Id).Where(g => g.Count() > 1))
            ClassifyGroup(group.ToList(), "entity_id", group.Key, sourceIndexByAgency, findings);

        foreach (var group in mergedEntities
                     .Where(e => !string.IsNullOrEmpty(e.TripUpdate.Trip?.TripId))
                     .GroupBy(e => e.TripUpdate.Trip.TripId)
                     .Where(g => g.Count() > 1))
            ClassifyGroup(group.ToList(), "trip_id", group.Key, sourceIndexByAgency, findings);
    }

    private void ClassifyGroup(
        List<FeedEntity> group, string keyField, string keyValue,
        Dictionary<string, EntityIndex> sourceIndexByAgency, List<FindingDraft> findings)
    {
        var resolutions = group.Select(e =>
        {
            var tripId = e.TripUpdate.Trip?.TripId;
            var resolved = resolver.Resolve(tripId) ?? resolver.Resolve(e.Id);
            return resolved;
        }).ToList();

        var distinct = resolutions.Select(r => r is null ? $"unresolved:{keyValue}" : $"{r.AgencyId}:{r.OriginalId}").Distinct().ToList();
        var first = resolutions.FirstOrDefault(r => r is not null);
        if (first is not null && sourceIndexByAgency.TryGetValue(first.AgencyId, out var idx))
        {
            var sourceCount = idx.CountByTripId(FeedTypeKind.TripUpdate, first.OriginalId);
            if (sourceCount >= group.Count) return;
        }

        var mergedJson = JsonFormatter.Default.Format(group[0]);
        var category = distinct.Count == 1 ? MismatchCategory.DuplicatedInMerged : MismatchCategory.PrefixCollision;
        var explanation = distinct.Count == 1
            ? $"Merged feed contains {group.Count} TripUpdate entities sharing {keyField} '{keyValue}', tracing to the same " +
              "source entity - the merge step emitted it more than once. Explains ENTITY_MORE_THAN_ONCE / VEHICLE_POSITION_DUPLICATE_TRIP."
            : $"Merged feed contains {group.Count} TripUpdate entities sharing {keyField} '{keyValue}', but they trace to " +
              "different source entities/agencies - the prefixing scheme failed to keep them unique. Explains ENTITY_MORE_THAN_ONCE.";

        findings.Add(new FindingDraft(FeedType.TripUpdates, category, Severity.Error, first?.AgencyId, keyValue, keyField,
            null, keyValue, explanation, "ENTITY_MORE_THAN_ONCE", null, mergedJson));
    }

    private void DetectMissingInMerged(
        Dictionary<string, FeedMessage> sourcesByAgency, HashSet<(string Agency, string TripId)> covered, List<FindingDraft> findings)
    {
        foreach (var (agencyId, sourceFeed) in sourcesByAgency)
        {
            foreach (var entity in sourceFeed.Entity)
            {
                if (entity.TripUpdate is null) continue;
                var tripId = entity.TripUpdate.Trip?.TripId;
                if (string.IsNullOrEmpty(tripId) || covered.Contains((agencyId, tripId))) continue;

                findings.Add(new FindingDraft(FeedType.TripUpdates, MismatchCategory.MissingInMerged, Severity.Error,
                    agencyId, entity.Id, null, JsonFormatter.Default.Format(entity), null,
                    $"Source TripUpdate '{entity.Id}' (trip '{tripId}', agency '{agencyId}') has no corresponding entity " +
                    "in the merged feed - the merge step dropped it.",
                    null, JsonFormatter.Default.Format(entity), null));
            }
        }
    }
}
