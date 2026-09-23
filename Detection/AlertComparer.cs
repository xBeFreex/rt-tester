using Google.Protobuf;
using rt_tester.Models;
using TransitRealtime;

namespace rt_tester.Detection;

/// <summary>
/// Alerts have no natural business key besides entity.Id, so matching relies on the
/// merged entity id carrying the agency prefix over the source entity id.
/// </summary>
public sealed class AlertComparer(PrefixResolver resolver)
{
    public List<FindingDraft> Compare(FeedMessage merged, Dictionary<string, FeedMessage> sourcesByAgency)
    {
        var findings = new List<FindingDraft>();
        var sourceIndexByAgency = sourcesByAgency.ToDictionary(kv => kv.Key, kv => EntityIndex.BuildAlerts(kv.Value));
        var mergedEntities = merged.Entity.Where(e => e.Alert is not null).ToList();
        var covered = new HashSet<(string Agency, string EntityId)>();

        foreach (var group in mergedEntities.GroupBy(e => e.Id).Where(g => g.Count() > 1))
        {
            var resolved = resolver.Resolve(group.Key);
            if (resolved is not null && sourceIndexByAgency.TryGetValue(resolved.AgencyId, out var idx0))
            {
                var sourceCount = idx0.CountByEntityId(FeedTypeKind.Alert, resolved.OriginalId);
                if (sourceCount >= group.Count()) continue;
            }

            findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.DuplicatedInMerged, Severity.Error,
                resolved?.AgencyId, group.Key, "entity_id", null, group.Key,
                $"Merged feed contains {group.Count()} Alert entities with id '{group.Key}' - the merge step emitted " +
                "it more than once. Explains ENTITY_MORE_THAN_ONCE.",
                "ENTITY_MORE_THAN_ONCE", null, JsonFormatter.Default.Format(group.First())));
        }

        foreach (var entity in mergedEntities)
        {
            var resolved = resolver.Resolve(entity.Id);
            if (resolved is null) continue;
            if (!sourceIndexByAgency.TryGetValue(resolved.AgencyId, out var sourceIndex)) continue;

            var sourceEntity = sourceIndex.FindAlert(resolved.OriginalId);
            if (sourceEntity is null) continue;

            covered.Add((resolved.AgencyId, resolved.OriginalId));
            CompareFields(resolved.AgencyId, entity, sourceEntity, findings);
        }

        foreach (var (agencyId, sourceFeed) in sourcesByAgency)
        {
            foreach (var entity in sourceFeed.Entity)
            {
                if (entity.Alert is null) continue;
                if (covered.Contains((agencyId, entity.Id))) continue;

                findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.MissingInMerged, Severity.Warning,
                    agencyId, entity.Id, null, JsonFormatter.Default.Format(entity), null,
                    $"Source Alert '{entity.Id}' (agency '{agencyId}') has no corresponding entity in the merged feed.",
                    null, JsonFormatter.Default.Format(entity), null));
            }
        }

        return findings;
    }

    private static void CompareFields(string agencyId, FeedEntity mergedEntity, FeedEntity sourceEntity, List<FindingDraft> findings)
    {
        var merged = mergedEntity.Alert;
        var source = sourceEntity.Alert;
        var mergedJson = JsonFormatter.Default.Format(mergedEntity);
        var sourceJson = JsonFormatter.Default.Format(sourceEntity);

        if (source.Cause != merged.Cause)
            findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.FieldMutated, Severity.Warning,
                agencyId, mergedEntity.Id, "cause", source.Cause.ToString(), merged.Cause.ToString(),
                $"Merge altered alert cause for '{mergedEntity.Id}' (agency '{agencyId}').", null, sourceJson, mergedJson));

        if (source.Effect != merged.Effect)
            findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.FieldMutated, Severity.Warning,
                agencyId, mergedEntity.Id, "effect", source.Effect.ToString(), merged.Effect.ToString(),
                $"Merge altered alert effect for '{mergedEntity.Id}' (agency '{agencyId}').", null, sourceJson, mergedJson));

        if (source.ActivePeriod.Count != merged.ActivePeriod.Count)
            findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.FieldMutated, Severity.Warning,
                agencyId, mergedEntity.Id, "active_period.count", source.ActivePeriod.Count.ToString(), merged.ActivePeriod.Count.ToString(),
                $"Merge changed the number of active_period entries for alert '{mergedEntity.Id}' (agency '{agencyId}').",
                null, sourceJson, mergedJson));

        if (source.InformedEntity.Count != merged.InformedEntity.Count)
            findings.Add(new FindingDraft(FeedType.Alerts, MismatchCategory.FieldMutated, Severity.Warning,
                agencyId, mergedEntity.Id, "informed_entity.count", source.InformedEntity.Count.ToString(), merged.InformedEntity.Count.ToString(),
                $"Merge changed the number of informed_entity entries for alert '{mergedEntity.Id}' (agency '{agencyId}').",
                null, sourceJson, mergedJson));
    }
}
