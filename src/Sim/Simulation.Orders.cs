using System;
using System.Linq;
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
        DelayOrder(team);
        int n = team.Soldiers.Count;
        // The men end up in a line across the way they were going, facing on
        var travel = target - (route.Count > 1 ? route[^2] : team.Center);
        float heading = travel.LengthSquared() > 1 ? MathF.Atan2(travel.Y, travel.X) : MathF.PI / 2;
        // Into a house: the men spread through its rooms instead of fanning out around the door
        var house = Map.TerrainAt(target) == TerrainType.Building ? HouseAt(target) : null;
        var places = house != null ? PlacesInHouse(house, n) : null;
        // Onto empty foxholes of our own: each man drops into the nearest one
        // to where he would have stood, facing the way it was dug.
        var holeOf = new int[n];
        Array.Fill(holeOf, -1);
        var holes = house == null && Vector2.Distance(route[^1], target) < 2f
            ? FreeFoxholesAround(team.Side, target, n, team) : new System.Collections.Generic.List<Trench>();
        if (holes.Count > 0)
        {
            var movers = Enumerable.Range(0, n).Where(i => !team.Soldiers[i].Fleeing && !team.Soldiers[i].EscapingFire).ToList();
            var match = MatchNearest(movers.Select(i => target + FormationOffset(i, n, heading)).ToList(), holes.Select(h => h.At).ToList());
            for (int k = 0; k < movers.Count; k++) holeOf[movers[k]] = match[k];
        }
        for (int i = 0; i < n; i++)
        {
            var s = team.Soldiers[i];
            if (s.Fleeing || s.EscapingFire) continue;
            s.Path.Clear();
            s.FacingOnArrival = places != null ? null : heading;
            if (places != null)
            {
                for (int w = 0; w < route.Count - 1; w++)
                    s.Path.Enqueue(route[w] + FormationOffset(i, n) * 0.3f);
                s.Path.Enqueue(places[i]);
                continue;
            }
            if (holeOf[i] >= 0)
            {
                var hole = holes[holeOf[i]];
                for (int w = 0; w < route.Count - 1; w++)
                    s.Path.Enqueue(route[w] + FormationOffset(i, n, heading) * 0.3f);
                s.Path.Enqueue(hole.At);
                s.FacingOnArrival = hole.Facing;
                continue;
            }
            // Each man follows the team's route, spread a little to the side,
            // and fans out around the goal.
            var offset = FormationOffset(i, n, heading);
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
    public void OrderFire(Team team, Vector2 target)
    {
        SetStationaryOrder(team, TeamOrder.Fire, target);
        foreach (var s in team.Soldiers.Where(s => s.Weapon.IsIndirect && s.CanAct))
        {
            float d = Vector2.Distance(s.Position, target);
            if (s.Ammo == 0) Log(team.Side, $"{s.Name}: no bombs left.");
            else if (d < s.Weapon.MinRange) Log(team.Side, $"{s.Name}: too close for the mortar, at least {s.Weapon.MinRange:0} m.");
            else if (d > s.Weapon.Range) Log(team.Side, $"{s.Name}: out of mortar range, at most {s.Weapon.Range:0} m.");
            else
            {
                s.BarrageLeft = Math.Min(MortarBarrage, s.Ammo);
                Log(team.Side, $"{s.Name}: firing {s.BarrageLeft} bombs, {s.Ammo} left.");
            }
        }
    }

    /// <summary>Bombs a mortar fires at a point for one Fire order, so it does not empty itself on empty ground.</summary>
    public const int MortarBarrage = 6;

    /// <summary>Stay put, go prone and fire at anything in range, facing a point.</summary>
    public void OrderDefend(Team team, Vector2 facing)
    {
        SetStationaryOrder(team, TeamOrder.Defend, facing);
        TakeUpLine(team, facing);
    }

    /// <summary>
    /// The men spread out into a line across the way they face, each going to
    /// the nearest place in it, then turn to face the enemy. Men already in a
    /// house or in trenches stay where they are: they have their places. If
    /// there are empty foxholes of their own side close by, the men get into
    /// those instead.
    /// </summary>
    internal void TakeUpLine(Team team, Vector2 facingPoint, bool mustDig = false)
    {
        var men = team.Soldiers.Where(s => s.CanAct && !s.Fleeing && !s.EscapingFire).ToList();
        if (men.Count == 0) return;
        if (men.Any(s => Map.TerrainAt(s.Position) == TerrainType.Building)) return;
        var center = men.Aggregate(Vector2.Zero, (a, s) => a + s.Position) / men.Count;
        if (FillFoxholes(men, center, facingPoint)) return;
        var d = facingPoint - center;
        if (d.LengthSquared() < 1) return;
        float facing = MathF.Atan2(d.Y, d.X);
        var across = new Vector2(-MathF.Sin(facing), MathF.Cos(facing));
        bool leaderBehind = men[0].IsLeader && men.Count > 2;
        // Places in the line, left to right; men taken left to right too so nobody crosses another's path
        var places = Enumerable.Range(0, men.Count).Select(i => Place(center + LineOffset(i, men.Count, facing, leaderBehind), mustDig)).ToList();
        var order = Enumerable.Range(0, men.Count).ToList();
        var byAcross = (leaderBehind ? men.Skip(1) : men).OrderBy(s => Vector2.Dot(s.Position - center, across)).ToList();
        if (leaderBehind) byAcross.Insert(0, men[0]);
        for (int i = 0; i < men.Count; i++)
        {
            var s = byAcross[i];
            s.Path.Clear();
            if (Vector2.Distance(s.Position, places[i]) > 0.5f) s.Path.Enqueue(places[i]);
            s.FacingOnArrival = facing;
            if (!s.IsMoving) s.Facing = facing;
        }
    }

    /// <summary>
    /// Men who are not yet in a foxhole get into the empty ones of their side
    /// near the team. Returns false if no man is in one and there are none to
    /// get into, so the team forms an ordinary line instead. Men left over
    /// when the holes run out stay where they are.
    /// </summary>
    bool FillFoxholes(System.Collections.Generic.List<Soldier> men, Vector2 center, Vector2 facingPoint)
    {
        bool InHole(Soldier s) => Map.TerrainAt(s.Position) == TerrainType.Trench;
        var outside = men.Where(s => !InHole(s)).ToList();
        var holes = outside.Count > 0
            ? FreeFoxholesAround(men[0].Side, center, outside.Count, null)
            : new System.Collections.Generic.List<Trench>();
        if (outside.Count == men.Count && holes.Count == 0) return false;
        var match = MatchNearest(outside.Select(s => s.Position).ToList(), holes.Select(h => h.At).ToList());
        for (int k = 0; k < outside.Count; k++)
        {
            if (match[k] < 0) continue;
            var s = outside[k];
            var hole = holes[match[k]];
            s.Path.Clear();
            s.Path.Enqueue(hole.At);
            var d = facingPoint - hole.At;
            s.FacingOnArrival = d.LengthSquared() > 1 ? MathF.Atan2(d.Y, d.X) : hole.Facing;
        }
        return true;
    }

    /// <summary>The nearest spot to a point a man can stand on (and dig in, if he must).</summary>
    Vector2 Place(Vector2 p, bool mustDig)
    {
        bool Good(Vector2 q)
        {
            var (x, y) = Map.WorldToCell(q);
            if (!Map.InBounds(x, y) || !Map.IsPassable(x, y)) return false;
            var t = Map.TerrainAt(x, y);
            return !mustDig || CanDig(t);
        }
        if (Good(p)) return p;
        for (float r = 2; r <= 10; r += 2)
            for (int a = 0; a < 12; a++)
            {
                var q = p + new Vector2(MathF.Cos(a * MathF.PI / 6), MathF.Sin(a * MathF.PI / 6)) * r;
                if (Good(q)) return q;
            }
        return p;
    }

    /// <summary>Lie low and hold fire unless the enemy comes very close.</summary>
    public void OrderHide(Team team) => SetStationaryOrder(team, TeamOrder.Hide, team.Center);

    public void Stop(Team team) => SetStationaryOrder(team, TeamOrder.None, team.Center);

    /// <summary>
    /// A team with a leader moves off at once; without one the men take a
    /// while to agree what to do, longer the more frightened they are.
    /// </summary>
    void DelayOrder(Team team)
    {
        if (team.HasLeader) { team.OrderDelayUntil = Time; return; }
        float stress = team.Active.Select(s => s.Stress).DefaultIfEmpty(0).Average();
        team.OrderDelayUntil = Time + 4f + stress / 10f;
    }

    /// <summary>Whether any steady man of the team can see a point on the ground.</summary>
    public bool CanObserve(Team team, Vector2 point) =>
        team.Soldiers.Any(s => s.CanAct && s.Morale <= MoraleState.Pinned
                               && LineOfSight(s.Position, EyeHeight(Posture.Crouched), point, 0.5f));

    /// <summary>
    /// The player's way to call support: a headquarters team must be able to
    /// see the target. Returns a message to show if it could not be called.
    /// </summary>
    public string? OrderSupport(Team team, SupportAsset asset, Vector2 target)
    {
        if (!team.IsHeadquarters) return $"{team.Name} has no way to reach the guns.";
        if (InPrep) return "The guns will not fire before the attack begins.";
        if (asset.Side != team.Side) return "That is not ours to call.";
        if (!asset.IsAvailable(Time)) return asset.MissionsLeft <= 0 ? $"{asset.Name} has nothing left." : $"{asset.Name} is still busy.";
        if (!CanObserve(team, target)) return $"{team.Name} cannot see that spot.";
        CallSupport(asset, target);
        return null;
    }

    void SetStationaryOrder(Team team, TeamOrder order, Vector2 point)
    {
        team.Order = order;
        team.OrderPoint = point;
        foreach (var s in team.Soldiers)
        {
            if (s.Fleeing || s.EscapingFire) continue;
            s.Path.Clear();
            s.Target = null;
            s.AreaTarget = null;
            s.BurstLeft = 0;
            s.BarrageLeft = 0;
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

    /// <summary>
    /// Where a man stands in his team's formation, turned to face a direction
    /// (radians): the line runs across the way the team faces, the second row behind.
    /// </summary>
    public static Vector2 FormationOffset(int index, int count, float facing)
    {
        var o = FormationOffset(index, count);
        var forward = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
        var across = new Vector2(-forward.Y, forward.X);
        return across * o.X - forward * o.Y;
    }

    /// <summary>
    /// A defensive line: one man every <see cref="FoxholeSpacing"/> metres across
    /// the way the team faces, staggered a little forward and back so the holes
    /// do not make a straight row an enemy could rake along. The leader stays a
    /// few metres behind the middle.
    /// </summary>
    public static Vector2 LineOffset(int index, int count, float facing, bool leaderBehind)
    {
        var forward = new Vector2(MathF.Cos(facing), MathF.Sin(facing));
        var across = new Vector2(-forward.Y, forward.X);
        if (leaderBehind && index == 0 && count > 2) return -forward * 6f;
        int k = leaderBehind && count > 2 ? index - 1 : index;
        int m = leaderBehind && count > 2 ? count - 1 : count;
        float x = (k - (m - 1) / 2f) * FoxholeSpacing;
        float y = k % 2 == 0 ? 1f : -1f;
        return across * x + forward * y;
    }

    /// <summary>Metres between foxholes in a team's line.</summary>
    public const float FoxholeSpacing = 5f;

    public static Vector2 FormationOffset(int index, int count)
    {
        // A loose two-row line, about 6 m between men
        const float spacing = 6f;
        int perRow = (count + 1) / 2;
        int row = index / perRow, col = index % perRow;
        float x = (col - (perRow - 1) / 2f) * spacing;
        float y = row * spacing - (count > perRow ? spacing / 2 : 0);
        return new Vector2(x, y);
    }
}
