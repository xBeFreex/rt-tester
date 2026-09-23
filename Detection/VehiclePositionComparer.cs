using Google.Protobuf;
using rt_tester.Configuration;
using rt_tester.Models;
using TransitRealtime;

namespace rt_tester.Detection;

/// <summary>
/// Compares the merged VehiclePositions feed against each agency's original source
/// feed, reporting only discrepancies introduced by the merge/prefixing step.
/// </summary>
public sealed class VehiclePositionComparer(PrefixResolver resolver, AggregatorConfig config)
{
    public List<FindingDraft> Compare(FeedMessage merged, Dictionary<string, FeedMessage> sourcesByAgency)
    {
        var findings = new List<FindingDraft>();
        var sourceIndexByAgency = sourcesByAgency.ToDictionary(kv => kv.Key, kv => EntityIndex.BuildVehiclePositions(kv.Value));

        var mergedVehicleEntities = merged.Entity.Where(e => e.Vehicle is not null).ToList();

        // Track which (agency, originalTripId) pairs we successfully attribute a merged entity to,
        // so the "missing in merged" pass below only flags genuine drops.
        var coveredSourceTripKeys = new HashSet<(string Agency, string TripId)>();

        DetectDuplicatesAndCollisions(mergedVehicleEntities, sourceIndexByAgency, findings);

        foreach (var entity in mergedVehicleEntities)
        {
            var vp = entity.Vehicle;
            var tripId = vp.Trip?.TripId;
            var stopId = vp.StopId;

            var (expectedAgency, originalTripId, prefixFinding) = ResolveAgencyAndCheckPrefixConsistency(entity, tripId, stopId);
            if (prefixFinding is not null) findings.Add(prefixFinding);
            if (expectedAgency is null) continue;
            if (!sourceIndexByAgency.TryGetValue(expectedAgency, out var sourceIndex)) continue;

            var vehicleId = vp.Vehicle?.Id;
            var sourceEntity = sourceIndex.FindVehiclePosition(originalTripId, vehicleId, entity.Id);
            if (sourceEntity is null) continue;

            if (!string.IsNullOrEmpty(originalTripId))
                coveredSourceTripKeys.Add((expectedAgency, originalTripId));

            CompareFields(expectedAgency, entity, sourceEntity, findings);
        }

        DetectMissingInMerged(sourcesByAgency, coveredSourceTripKeys, findings);

        return findings;
    }

    private (string? Agency, string? OriginalTripId, FindingDraft? Finding) ResolveAgencyAndCheckPrefixConsistency(
        FeedEntity entity, string? tripId, string? stopId)
    {
        var tripResolved = resolver.Resolve(tripId);
        var stopResolved = resolver.Resolve(stopId);

        if (tripResolved is not null)
        {
            // trip_id carries a known agency prefix; stop_id (if present) must carry the same one.
            if (!string.IsNullOrEmpty(stopId) && stopResolved is null)
            {
                var finding = new FindingDraft(
                    FeedType.VehiclePositions, MismatchCategory.InconsistentIdPrefix, Severity.Error,
                    tripResolved.AgencyId, entity.Id, "stop_id", stopId, stopId,
                    $"Merged VehiclePosition '{entity.Id}' has trip_id prefixed with '{tripResolved.AgencyId}{config.PrefixSeparator}' " +
                    $"but stop_id '{stopId}' was left unprefixed. The stop reference no longer resolves in the merged " +
                    "namespace, which the validator surfaces as UNKNOWN_TRIP_ID / VEHICLE_POSITION_CONVERTED_TO_ADDED.",
                    "UNKNOWN_TRIP_ID", null, JsonFormatter.Default.Format(entity));
                return (tripResolved.AgencyId, tripResolved.OriginalId, finding);
            }

            if (stopResolved is not null && stopResolved.AgencyId != tripResolved.AgencyId)
            {
                var finding = new FindingDraft(
                    FeedType.VehiclePositions, MismatchCategory.InconsistentIdPrefix, Severity.Error,
                    tripResolved.AgencyId, entity.Id, "stop_id", stopId, stopId,
                    $"Merged VehiclePosition '{entity.Id}' has trip_id prefixed for agency '{tripResolved.AgencyId}' " +
                    $"but stop_id is prefixed for a different agency '{stopResolved.AgencyId}'.",
                    "UNKNOWN_TRIP_ID", null, JsonFormatter.Default.Format(entity));
                return (tripResolved.AgencyId, tripResolved.OriginalId, finding);
            }

            return (tripResolved.AgencyId, tripResolved.OriginalId, null);
        }

        if (stopResolved is not null && !string.IsNullOrEmpty(tripId))
        {
            var finding = new FindingDraft(
                FeedType.VehiclePositions, MismatchCategory.InconsistentIdPrefix, Severity.Error,
                stopResolved.AgencyId, entity.Id, "trip_id", tripId, tripId,
                $"Merged VehiclePosition '{entity.Id}' has stop_id prefixed with '{stopResolved.AgencyId}{config.PrefixSeparator}' " +
                $"but trip_id '{tripId}' was left unprefixed.",
                "UNKNOWN_TRIP_ID", null, JsonFormatter.Default.Format(entity));
            return (stopResolved.AgencyId, tripId, finding);
        }

        // Neither field carries a recognizable prefix - either merge left this trip alone (single-agency
        // deployment) or it belongs to an agency we don't know about. Fall back to treating trip_id as-is.
        return (null, tripId, null);
    }

    private void CompareFields(string agencyId, FeedEntity mergedEntity, FeedEntity sourceEntity, List<FindingDraft> findings)
    {
        var merged = mergedEntity.Vehicle;
        var source = sourceEntity.Vehicle;
        var mergedJson = JsonFormatter.Default.Format(mergedEntity);
        var sourceJson = JsonFormatter.Default.Format(sourceEntity);

        void Check(string field, string? sourceVal, string? mergedVal, string explanation, string? validatorCode = null, Severity severity = Severity.Warning, MismatchCategory category = MismatchCategory.FieldMutated)
        {
            if (sourceVal == mergedVal) return;
            findings.Add(new FindingDraft(FeedType.VehiclePositions, category, severity, agencyId, mergedEntity.Id,
                field, sourceVal, mergedVal, explanation, validatorCode, sourceJson, mergedJson));
        }

        if (source.HasCurrentStopSequence != merged.HasCurrentStopSequence || (source.HasCurrentStopSequence && source.CurrentStopSequence != merged.CurrentStopSequence))
            Check("current_stop_sequence", source.CurrentStopSequence.ToString(), merged.CurrentStopSequence.ToString(),
                $"Merge altered current_stop_sequence for vehicle '{mergedEntity.Id}' (agency '{agencyId}') from the source value.");

        if (source.HasCurrentStatus != merged.HasCurrentStatus || (source.HasCurrentStatus && source.CurrentStatus != merged.CurrentStatus))
            Check("current_status", source.CurrentStatus.ToString(), merged.CurrentStatus.ToString(),
                $"Merge altered current_status for vehicle '{mergedEntity.Id}' (agency '{agencyId}').");

        if (source.HasOccupancyStatus != merged.HasOccupancyStatus || (source.HasOccupancyStatus && source.OccupancyStatus != merged.OccupancyStatus))
            Check("occupancy_status", source.OccupancyStatus.ToString(), merged.OccupancyStatus.ToString(),
                $"Merge altered occupancy_status for vehicle '{mergedEntity.Id}' (agency '{agencyId}').");

        if (source.HasTimestamp != merged.HasTimestamp || (source.HasTimestamp && source.Timestamp != merged.Timestamp))
        {
            var sourceAge = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)source.Timestamp;
            var mergedAge = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)merged.Timestamp;
            if (source.HasTimestamp && mergedAge > config.StaleThresholdSeconds && sourceAge <= config.StaleThresholdSeconds)
            {
                findings.Add(new FindingDraft(FeedType.VehiclePositions, MismatchCategory.MergeIntroducedStaleTimestamp, Severity.Warning,
                    agencyId, mergedEntity.Id, "timestamp", source.Timestamp.ToString(), merged.Timestamp.ToString(),
                    $"Source timestamp for vehicle '{mergedEntity.Id}' was fresh ({sourceAge}s old) but the merged feed carries a " +
                    $"timestamp that is {mergedAge}s old - the merge step rewrote/dropped the timestamp, which the validator " +
                    "reports as INVALID_VEHICLE_POSITION_STALE_TIMESTAMP / VEHICLE_POSITION_TIMESTAMP_CONSISTENTLY_IN_THE_PAST.",
                    "INVALID_VEHICLE_POSITION_STALE_TIMESTAMP", sourceJson, mergedJson));
            }
            else
            {
                Check("timestamp", source.Timestamp.ToString(), merged.Timestamp.ToString(),
                    $"Merge altered timestamp for vehicle '{mergedEntity.Id}' (agency '{agencyId}').");
            }
        }

        var sourceStart = $"{source.Trip?.StartDate}T{source.Trip?.StartTime}";
        var mergedStart = $"{merged.Trip?.StartDate}T{merged.Trip?.StartTime}";
        var sourceHasStart = !string.IsNullOrEmpty(source.Trip?.StartDate) || !string.IsNullOrEmpty(source.Trip?.StartTime);
        var mergedHasStart = !string.IsNullOrEmpty(merged.Trip?.StartDate) || !string.IsNullOrEmpty(merged.Trip?.StartTime);
        if (sourceHasStart && !mergedHasStart)
        {
            findings.Add(new FindingDraft(FeedType.VehiclePositions, MismatchCategory.BadStartTimeIntroduced, Severity.Warning,
                agencyId, mergedEntity.Id, "trip.start_date/start_time", sourceStart, mergedStart,
                $"Source trip descriptor for vehicle '{mergedEntity.Id}' had a valid start_date/start_time but the merged " +
                "entity's is empty/malformed - likely explains VEHICLE_POSITION_BAD_START_TIME.",
                "VEHICLE_POSITION_BAD_START_TIME", sourceJson, mergedJson));
        }
        else if (sourceHasStart && mergedHasStart && sourceStart != mergedStart)
        {
            Check("trip.start_date/start_time", sourceStart, mergedStart,
                $"Merge altered trip start_date/start_time for vehicle '{mergedEntity.Id}' (agency '{agencyId}').",
                "VEHICLE_POSITION_BAD_START_TIME");
        }

        if (source.Position is not null && merged.Position is not null)
        {
            if (Math.Abs(source.Position.Latitude - merged.Position.Latitude) > 0.0001 ||
                Math.Abs(source.Position.Longitude - merged.Position.Longitude) > 0.0001)
            {
                Check("position.lat/lon",
                    $"{source.Position.Latitude},{source.Position.Longitude}",
                    $"{merged.Position.Latitude},{merged.Position.Longitude}",
                    $"Merge altered the reported position for vehicle '{mergedEntity.Id}' (agency '{agencyId}').",
                    severity: Severity.Error);
            }
        }
    }

    private void DetectDuplicatesAndCollisions(
        List<FeedEntity> mergedVehicleEntities,
        Dictionary<string, EntityIndex> sourceIndexByAgency,
        List<FindingDraft> findings)
    {
        var byEntityId = mergedVehicleEntities.GroupBy(e => e.Id);
        foreach (var group in byEntityId.Where(g => g.Count() > 1))
        {
            ClassifyGroup(group.ToList(), e => e.Vehicle.Trip?.TripId, "entity_id", group.Key, sourceIndexByAgency, findings);
        }

        var byTripId = mergedVehicleEntities
            .Where(e => !string.IsNullOrEmpty(e.Vehicle.Trip?.TripId))
            .GroupBy(e => e.Vehicle.Trip.TripId);
        foreach (var group in byTripId.Where(g => g.Count() > 1))
        {
            ClassifyGroup(group.ToList(), e => e.Vehicle.Trip?.TripId, "trip_id", group.Key, sourceIndexByAgency, findings);
        }
    }

    private void ClassifyGroup(
        List<FeedEntity> group, Func<FeedEntity, string?> tripIdSelector, string keyField, string keyValue,
        Dictionary<string, EntityIndex> sourceIndexByAgency, List<FindingDraft> findings)
    {
        var resolutions = group.Select(e =>
        {
            var tripId = tripIdSelector(e);
            var resolved = resolver.Resolve(tripId) ?? resolver.Resolve(e.Id);
            return (Entity: e, Resolved: resolved, TripId: tripId);
        }).ToList();

        var distinctOriginals = resolutions
            .Select(r => r.Resolved is null ? (null as string, r.TripId) : (r.Resolved.AgencyId, r.Resolved.OriginalId))
            .Distinct()
            .ToList();

        // If the source feed already contained this many occurrences under the original id, it's not our bug.
        var firstResolved = resolutions.FirstOrDefault(r => r.Resolved is not null).Resolved;
        if (firstResolved is not null && sourceIndexByAgency.TryGetValue(firstResolved.AgencyId, out var idx))
        {
            var sourceCount = idx.CountByTripId(FeedTypeKind.VehiclePosition, firstResolved.OriginalId);
            if (sourceCount >= group.Count) return;
        }

        var mergedJson = JsonFormatter.Default.Format(group[0]);
        if (distinctOriginals.Count == 1)
        {
            findings.Add(new FindingDraft(FeedType.VehiclePositions, MismatchCategory.DuplicatedInMerged, Severity.Error,
                firstResolved?.AgencyId, keyValue, keyField, null, keyValue,
                $"Merged feed contains {group.Count} VehiclePosition entities sharing {keyField} '{keyValue}', all tracing back " +
                "to the same source entity - the merge step emitted it more than once. Explains ENTITY_MORE_THAN_ONCE / " +
                "VEHICLE_POSITION_DUPLICATE_ID / VEHICLE_POSITION_DUPLICATE_TRIP.",
                "ENTITY_MORE_THAN_ONCE", null, mergedJson));
        }
        else
        {
            findings.Add(new FindingDraft(FeedType.VehiclePositions, MismatchCategory.PrefixCollision, Severity.Error,
                null, keyValue, keyField, null, keyValue,
                $"Merged feed contains {group.Count} VehiclePosition entities sharing {keyField} '{keyValue}', but they trace back " +
                "to different source entities/agencies. The agency-id prefixing scheme failed to keep them unique after merging. " +
                "Explains ENTITY_MORE_THAN_ONCE / VEHICLE_POSITION_DUPLICATE_ID / VEHICLE_POSITION_DUPLICATE_TRIP.",
                "ENTITY_MORE_THAN_ONCE", null, mergedJson));
        }
    }

    private void DetectMissingInMerged(
        Dictionary<string, FeedMessage> sourcesByAgency,
        HashSet<(string Agency, string TripId)> covered,
        List<FindingDraft> findings)
    {
        foreach (var (agencyId, sourceFeed) in sourcesByAgency)
        {
            foreach (var entity in sourceFeed.Entity)
            {
                if (entity.Vehicle is null) continue;
                var tripId = entity.Vehicle.Trip?.TripId;
                if (string.IsNullOrEmpty(tripId)) continue;
                if (covered.Contains((agencyId, tripId))) continue;

                findings.Add(new FindingDraft(FeedType.VehiclePositions, MismatchCategory.MissingInMerged, Severity.Error,
                    agencyId, entity.Id, null, JsonFormatter.Default.Format(entity), null,
                    $"Source VehiclePosition '{entity.Id}' (trip '{tripId}', agency '{agencyId}') has no corresponding " +
                    "entity in the merged feed - the merge step dropped it.",
                    null, JsonFormatter.Default.Format(entity), null));
            }
        }
    }
}
