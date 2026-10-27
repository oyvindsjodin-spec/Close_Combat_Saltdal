using System;
using System.Linq;
using System.Numerics;

namespace Saltdal.Sim;

public sealed partial class Simulation
{
    public const float MaxStress = 130f;

    void UpdateMorale()
    {
        foreach (var s in Soldiers)
        {
            if (!s.CanAct) continue;

            bool underFire = Time - s.LastUnderFire < 3f;
            float recovery = underFire ? 1.0f : 4f; // stress points per second
            if (LeaderNearby(s)) recovery *= 1.6f;
            recovery *= 0.7f + 0.6f * s.Experience;
            s.Stress = MathF.Max(0, s.Stress - recovery * TickSeconds);

            var before = s.Morale;
            s.Morale = MoraleFromStress(s.Stress, before);

            if (s.Morale >= MoraleState.Cowering && TryToSurrender(s))
                continue;
            if (s.Morale >= MoraleState.Panicked && !s.Fleeing)
                StartFleeing(s);
            else if (s.Morale < MoraleState.Panicked && s.Fleeing)
            {
                // He has pulled himself together; he stops and waits for orders
                s.Fleeing = false;
                s.Path.Clear();
            }
        }
    }

    public static MoraleState MoraleFromStress(float stress, MoraleState current)
    {
        // A routed man stays routed until he has calmed down well below the threshold
        if (stress >= 100 || (current == MoraleState.Routed && stress >= 50)) return MoraleState.Routed;
        if (stress >= 75) return MoraleState.Panicked;
        if (stress >= 50) return MoraleState.Cowering;
        if (stress >= 25) return MoraleState.Pinned;
        return MoraleState.Ready;
    }

    bool LeaderNearby(Soldier s) =>
        s.Team.Soldiers.Any(l => l.IsLeader && l != s && l.CanAct && l.Morale <= MoraleState.Pinned
                                 && Vector2.DistanceSquared(l.Position, s.Position) < 40 * 40);

    internal void AddStress(Soldier s, float amount)
    {
        if (!s.CanAct) return;
        s.Stress = MathF.Min(MaxStress, s.Stress + amount * (1.2f - 0.5f * s.Experience));
    }

    void OnCasualty(Soldier victim)
    {
        // Each loss hits harder as the team bleeds
        var team = victim.Team;
        float lost = team.Soldiers.Count(s => !s.CanAct) / (float)team.Soldiers.Count;
        float shock = 12f * (1 + 2 * lost);
        foreach (var mate in team.Soldiers)
        {
            if (mate == victim) continue;
            if (Vector2.DistanceSquared(mate.Position, victim.Position) < 50 * 50)
                AddStress(mate, shock);
            if (victim.IsLeader)
                AddStress(mate, 12f);
        }
    }

    bool TryToSurrender(Soldier s)
    {
        bool cornered = Soldiers.Any(e => e.Side != s.Side && e.IsEffective
                                          && Vector2.DistanceSquared(e.Position, s.Position) < 15 * 15);
        if (!cornered || !Rng.Chance(0.02f)) return false;
        s.Morale = MoraleState.Surrendered;
        s.Path.Clear();
        s.Fleeing = false;
        s.Target = null;
        return true;
    }

    void StartFleeing(Soldier s)
    {
        Vector2 goal;
        if (s.Morale == MoraleState.Routed)
        {
            // Run for his own side's map edge
            float edgeY = s.Side == Side.Allied ? AlliedRetreatY : GermanRetreatY;
            goal = new Vector2(s.Position.X, Math.Clamp(edgeY, 1f, Map.WorldHeight - 1f));
        }
        else
        {
            // Panicked: dash for the nearest cover away from the enemy
            var threat = VisibleEnemies(s.Side).Where(e => e.CanAct)
                .OrderBy(e => Vector2.DistanceSquared(e.Position, s.Position)).FirstOrDefault();
            if (threat == null) return;
            var away = Vector2.Normalize(s.Position - threat.Position + new Vector2(0.01f, 0));
            var found = FindCover(s.Position + away * 20f, 30f);
            if (found == null) return;
            goal = found.Value;
        }
        var route = Pathfinder.FindPath(s.Position, goal);
        if (route == null) return;
        s.Path.Clear();
        foreach (var p in route) s.Path.Enqueue(p);
        s.Fleeing = true;
    }

    Vector2? FindCover(Vector2 around, float radius)
    {
        var (cx, cy) = Map.WorldToCell(around);
        int r = (int)(radius / Map.CellSize);
        Vector2? best = null;
        float bestD = float.MaxValue;
        for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = cx + dx, y = cy + dy;
                if (!Map.IsPassable(x, y)) continue;
                var t = Map.TerrainAt(x, y);
                if (t != TerrainType.Forest && t != TerrainType.Rock) continue;
                float d = dx * dx + dy * dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = Map.CellCenter(x, y);
                }
            }
        return best;
    }

    /// <summary>World y of the map edge each side retreats to.</summary>
    public float AlliedRetreatY { get; set; }
    public float GermanRetreatY { get; set; } = float.MaxValue;
}
