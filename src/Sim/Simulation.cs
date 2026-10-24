using System;
using System.Collections.Generic;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// The whole battle state, advanced in fixed ticks. Nothing here knows about
/// Godot or drawing, so the same orders always give the same result.
/// </summary>
public sealed class Simulation
{
    public const float TickSeconds = 0.1f;

    public MapData Map { get; }
    public Pathfinder Pathfinder { get; }
    public float Time { get; private set; }
    public long TickCount { get; private set; }

    readonly List<Team> _teams = new();
    public IReadOnlyList<Team> Teams => _teams;

    int _nextSoldierId = 1;

    public Simulation(MapData map)
    {
        Map = map;
        Pathfinder = new Pathfinder(map);
    }

    /// <summary>Create a team with soldiers spread around a point.</summary>
    public Team SpawnTeam(string name, Side side, Vector2 at, string[] soldierNames)
    {
        var team = new Team(_teams.Count + 1, name, side);
        for (int i = 0; i < soldierNames.Length; i++)
        {
            var pos = at + FormationOffset(i, soldierNames.Length);
            team.Add(new Soldier(_nextSoldierId++, soldierNames[i], pos));
        }
        _teams.Add(team);
        return team;
    }

    /// <summary>Order a team to move. Returns false if no route exists.</summary>
    public bool OrderMove(Team team, Vector2 target, MoveMode mode)
    {
        var route = Pathfinder.FindPath(team.Center, target);
        if (route == null)
            return false;
        team.Mode = mode;
        int n = team.Soldiers.Count;
        for (int i = 0; i < n; i++)
        {
            var s = team.Soldiers[i];
            s.Path.Clear();
            // Each man takes his own short route to the team's path, then follows it
            // and spreads out around the goal.
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

    public void Stop(Team team)
    {
        foreach (var s in team.Soldiers)
            s.Path.Clear();
    }

    public void Tick()
    {
        foreach (var team in _teams)
            foreach (var s in team.Soldiers)
                MoveSoldier(s, team.Mode);
        Time += TickSeconds;
        TickCount++;
    }

    void MoveSoldier(Soldier s, MoveMode mode)
    {
        s.PreviousPosition = s.Position;
        float budget = TickSeconds;
        while (budget > 0 && s.Path.Count > 0)
        {
            var target = s.Path.Peek();
            var delta = target - s.Position;
            float dist = delta.Length();
            if (dist < 0.05f)
            {
                s.Path.Dequeue();
                continue;
            }
            var dir = delta / dist;
            s.Facing = MathF.Atan2(dir.Y, dir.X);
            float speed = Speed(s.Position, dir, mode);
            float step = speed * budget;
            if (step >= dist)
            {
                s.Position = target;
                s.Path.Dequeue();
                budget -= dist / speed;
            }
            else
            {
                s.Position += dir * step;
                budget = 0;
            }
        }
    }

    /// <summary>Walking speed in metres per second at a point, heading in a direction.</summary>
    public float Speed(Vector2 at, Vector2 dir, MoveMode mode)
    {
        float baseSpeed = mode switch
        {
            MoveMode.Sneak => 0.6f,
            MoveMode.MoveFast => 3.0f,
            _ => 1.4f,
        };
        float terrain = TerrainInfo.MoveCost(Map.TerrainAt(at));
        if (float.IsInfinity(terrain))
            terrain = 3f; // wading out of a cell he should not be in
        float ahead = Map.HeightAt(at + dir * 2f) - Map.HeightAt(at);
        float grade = ahead / 2f;
        float slope = grade > 0 ? 1 + grade * 6f : 1 - grade * 1.5f;
        return baseSpeed / (terrain * slope);
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
