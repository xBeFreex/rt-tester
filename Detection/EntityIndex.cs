using TransitRealtime;

namespace rt_tester.Detection;

/// <summary>
/// Per-agency lookup of source entities by their original (unprefixed) id, so a
/// merged entity can be matched back to the source entity it should have come from.
/// Lists (not single values) are kept per key so we can tell whether a duplicate
/// already existed in the source feed (out of scope) vs. was introduced by the merge.
/// </summary>
public sealed class EntityIndex
{
    private readonly Dictionary<string, List<FeedEntity>> _vehiclePositionsByTripId = new();
    private readonly Dictionary<string, List<FeedEntity>> _vehiclePositionsByVehicleId = new();
    private readonly Dictionary<string, List<FeedEntity>> _vehiclePositionsByEntityId = new();

    private readonly Dictionary<string, List<FeedEntity>> _tripUpdatesByTripId = new();
    private readonly Dictionary<string, List<FeedEntity>> _tripUpdatesByEntityId = new();

    private readonly Dictionary<string, List<FeedEntity>> _alertsByEntityId = new();

    public static EntityIndex BuildVehiclePositions(FeedMessage feed)
    {
        var index = new EntityIndex();
        foreach (var entity in feed.Entity)
        {
            if (entity.Vehicle is null) continue;
            Add(index._vehiclePositionsByEntityId, entity.Id, entity);

            var tripId = entity.Vehicle.Trip?.TripId;
            if (!string.IsNullOrEmpty(tripId))
                Add(index._vehiclePositionsByTripId, tripId, entity);

            var vehicleId = entity.Vehicle.Vehicle?.Id;
            if (!string.IsNullOrEmpty(vehicleId))
                Add(index._vehiclePositionsByVehicleId, vehicleId, entity);
        }
        return index;
    }

    public static EntityIndex BuildTripUpdates(FeedMessage feed)
    {
        var index = new EntityIndex();
        foreach (var entity in feed.Entity)
        {
            if (entity.TripUpdate is null) continue;
            Add(index._tripUpdatesByEntityId, entity.Id, entity);

            var tripId = entity.TripUpdate.Trip?.TripId;
            if (!string.IsNullOrEmpty(tripId))
                Add(index._tripUpdatesByTripId, tripId, entity);
        }
        return index;
    }

    public static EntityIndex BuildAlerts(FeedMessage feed)
    {
        var index = new EntityIndex();
        foreach (var entity in feed.Entity)
        {
            if (entity.Alert is null) continue;
            Add(index._alertsByEntityId, entity.Id, entity);
        }
        return index;
    }

    private static void Add(Dictionary<string, List<FeedEntity>> dict, string key, FeedEntity entity)
    {
        if (!dict.TryGetValue(key, out var list))
        {
            list = new List<FeedEntity>();
            dict[key] = list;
        }
        list.Add(entity);
    }

    public FeedEntity? FindVehiclePosition(string? tripId, string? vehicleId, string? entityId)
    {
        if (!string.IsNullOrEmpty(tripId) && _vehiclePositionsByTripId.TryGetValue(tripId, out var byTrip)) return byTrip[0];
        if (!string.IsNullOrEmpty(vehicleId) && _vehiclePositionsByVehicleId.TryGetValue(vehicleId, out var byVehicle)) return byVehicle[0];
        if (!string.IsNullOrEmpty(entityId) && _vehiclePositionsByEntityId.TryGetValue(entityId, out var byEntity)) return byEntity[0];
        return null;
    }

    public FeedEntity? FindTripUpdate(string? tripId, string? entityId)
    {
        if (!string.IsNullOrEmpty(tripId) && _tripUpdatesByTripId.TryGetValue(tripId, out var byTrip)) return byTrip[0];
        if (!string.IsNullOrEmpty(entityId) && _tripUpdatesByEntityId.TryGetValue(entityId, out var byEntity)) return byEntity[0];
        return null;
    }

    public FeedEntity? FindAlert(string? entityId)
    {
        if (!string.IsNullOrEmpty(entityId) && _alertsByEntityId.TryGetValue(entityId, out var byEntity)) return byEntity[0];
        return null;
    }

    public int CountByTripId(FeedTypeKind kind, string tripId)
    {
        var dict = kind switch
        {
            FeedTypeKind.VehiclePosition => _vehiclePositionsByTripId,
            FeedTypeKind.TripUpdate => _tripUpdatesByTripId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return dict.TryGetValue(tripId, out var list) ? list.Count : 0;
    }

    public int CountByEntityId(FeedTypeKind kind, string entityId)
    {
        var dict = kind switch
        {
            FeedTypeKind.VehiclePosition => _vehiclePositionsByEntityId,
            FeedTypeKind.TripUpdate => _tripUpdatesByEntityId,
            FeedTypeKind.Alert => _alertsByEntityId,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return dict.TryGetValue(entityId, out var list) ? list.Count : 0;
    }
}

public enum FeedTypeKind
{
    VehiclePosition,
    TripUpdate,
    Alert
}
