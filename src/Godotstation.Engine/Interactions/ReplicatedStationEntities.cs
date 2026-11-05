using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Godotstation.Engine.Entities;

namespace Godotstation.Engine.Interactions;

/// <summary>
/// The client's last replicated view of interactable station objects. This is
/// presentation and hit-test state; the server still validates every request.
/// </summary>
public sealed class ReplicatedStationEntities
{
    private readonly SortedDictionary<int, StationEntityState> entities = new();

    public ReplicatedStationEntities() : this(SharedStationInventoryWorld.InitialStates())
    {
    }

    public ReplicatedStationEntities(IEnumerable<StationEntityState> initialStates) => Apply(initialStates);

    /// <summary>Every known entity, in id order.</summary>
    public IEnumerable<StationEntityState> All => entities.Values;

    public StationEntityState Get(int id) => entities[id];

    public bool TryGet(int id, [MaybeNullWhen(false)] out StationEntityState state) => entities.TryGetValue(id, out state);

    /// <summary>Replaces the listed entities with the server's newer state; unlisted entities keep theirs.</summary>
    public void Apply(IEnumerable<StationEntityState> states)
    {
        foreach (StationEntityState state in states)
            entities[state.Id] = state;
    }

    /// <summary>Forgets an entity the server no longer has, such as a departed crew member's body.</summary>
    public bool Remove(int id) => entities.Remove(id);

    public int? FindNearestTarget(Vector2 position, float radius)
    {
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(radius) || radius <= 0)
            return null;
        int? nearest = null;
        float nearestSquared = radius * radius;
        foreach (StationEntityState entity in entities.Values)
        {
            if (!entity.IsTargetable) continue;
            float distanceSquared = Vector2.DistanceSquared(position, entity.Position);
            if (distanceSquared < nearestSquared ||
                (distanceSquared == nearestSquared && (nearest is null || entity.Id < nearest)))
            {
                nearest = entity.Id;
                nearestSquared = distanceSquared;
            }
        }
        return nearest;
    }
}
