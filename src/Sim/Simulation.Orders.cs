using System;
using System.Numerics;

namespace Saltdal.Sim;

public sealed partial class Simulation
{
    /// <summary>
    /// Order a team to move. Returns false if no route exists. The AI can ask for
    /// a covered route, and stop after maxDistance metres along it.
    /// </summary>
    public bool OrderMove(Team team, Vector2 target, MoveMode mode, bool preferCover = false, float maxDistance = float.PositiveInfinity)
    {
        var route = Pathfinder.FindPath(team.Center, target, preferCover);
        if (route == null)
            return false;
        if (!float.IsPositiveInfinity(maxDistance))
            route = Truncate(team.Center, route, maxDistance);
        team.Order = mode switch
        {
            MoveMode.MoveFast => TeamOrder.MoveFast,
            MoveMode.Sneak => TeamOrder.Sneak,
            _ => TeamOrder.Move,
        };
        team.OrderPoint = target;
        int n = team.Soldiers.Count;
        for (int i = 0; i < n; i++)
        {
            var s = team.Soldiers[i];
            if (s.Fleeing) continue;
            s.Path.Clear();
            // Each man follows the team's route, spread a little to the side,
            // and fans out around the goal.
            var offset = FormationOffset(i, n);
            for (int w = 0; w < route.Count; w++)
            {
                bool last = w == route.Count - 1;
                var p = route[w] + (last ? offset : offset * 0.3f);
                var (cx, cy) = Map.WorldToCell(p);
                s.Path.Enqueue(Map.IsPassable(cx, cy) ? p : route[w]);
            }
        }
        return true;
    }

    /// <summary>Shoot at a point: at enemies seen near it, or at the area if none are.</summary>
    public void OrderFire(Team team, Vector2 target) => SetStationaryOrder(team, TeamOrder.Fire, target);

    /// <summary>Stay put, go prone and fire at anything in range, facing a point.</summary>
    public void OrderDefend(Team team, Vector2 facing) => SetStationaryOrder(team, TeamOrder.Defend, facing);

    /// <summary>Lie low and hold fire unless the enemy comes very close.</summary>
    public void OrderHide(Team team) => SetStationaryOrder(team, TeamOrder.Hide, team.Center);

    public void Stop(Team team) => SetStationaryOrder(team, TeamOrder.None, team.Center);

    void SetStationaryOrder(Team team, TeamOrder order, Vector2 point)
    {
        team.Order = order;
        team.OrderPoint = point;
        foreach (var s in team.Soldiers)
        {
            if (s.Fleeing) continue;
            s.Path.Clear();
            s.Target = null;
            s.AreaTarget = null;
            s.BurstLeft = 0;
            if (order != TeamOrder.None && order != TeamOrder.Hide)
            {
                var d = point - s.Position;
                if (d.LengthSquared() > 1) s.Facing = MathF.Atan2(d.Y, d.X);
            }
        }
    }

    static System.Collections.Generic.List<Vector2> Truncate(Vector2 start, System.Collections.Generic.List<Vector2> route, float maxDistance)
    {
        var result = new System.Collections.Generic.List<Vector2>();
        var from = start;
        float left = maxDistance;
        foreach (var p in route)
        {
            float d = Vector2.Distance(from, p);
            if (d >= left)
            {
                result.Add(from + (p - from) * (left / d));
                return result;
            }
            result.Add(p);
            left -= d;
            from = p;
        }
        return result;
    }

    public static Vector2 FormationOffset(int index, int count)
    {
        // A loose two-row line, about 4 m between men
        const float spacing = 4f;
        int perRow = (count + 1) / 2;
        int row = index / perRow, col = index % perRow;
        float x = (col - (perRow - 1) / 2f) * spacing;
        float y = row * spacing - (count > perRow ? spacing / 2 : 0);
        return new Vector2(x, y);
    }
}
