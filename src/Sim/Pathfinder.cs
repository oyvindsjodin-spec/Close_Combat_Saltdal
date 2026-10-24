using System;
using System.Collections.Generic;
using System.Numerics;

namespace Saltdal.Sim;

/// <summary>
/// A* over the map grid. Cost is walking time: distance, times the terrain
/// cost, times a penalty for climbing. Slopes steeper than MaxSlopeDegrees
/// cannot be climbed or descended.
/// </summary>
public sealed class Pathfinder
{
    public const float MaxSlopeDegrees = 40f;
    const float UphillPenalty = 6f;   // extra cost per unit of rise over run
    const float DownhillPenalty = 1.5f;

    static readonly (int Dx, int Dy, float Dist)[] Neighbours =
    {
        (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
        (1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (-1, -1, 1.41421356f),
    };

    readonly MapData _map;
    readonly float _maxGrade;

    public Pathfinder(MapData map)
    {
        _map = map;
        _maxGrade = MathF.Tan(MaxSlopeDegrees * MathF.PI / 180f);
    }

    /// <summary>Cost of stepping from one cell to a neighbour, or infinity if blocked.</summary>
    public float StepCost(int x0, int y0, int x1, int y1, float dist)
    {
        if (!_map.IsPassable(x1, y1))
            return float.PositiveInfinity;
        // Diagonal steps may not cut between two blocked cells
        if (x0 != x1 && y0 != y1 && (!_map.IsPassable(x1, y0) || !_map.IsPassable(x0, y1)))
            return float.PositiveInfinity;
        float run = dist * _map.CellSize;
        float grade = (_map.HeightAt(x1, y1) - _map.HeightAt(x0, y0)) / run;
        if (MathF.Abs(grade) > _maxGrade)
            return float.PositiveInfinity;
        float slope = grade > 0 ? 1 + grade * UphillPenalty : 1 - grade * DownhillPenalty;
        float terrain = (TerrainInfo.MoveCost(_map.TerrainAt(x0, y0)) + TerrainInfo.MoveCost(_map.TerrainAt(x1, y1))) / 2;
        return run * terrain * slope;
    }

    /// <summary>
    /// Find a path between two world points. Returns waypoints (excluding the start),
    /// or null if the goal cannot be reached. If the goal cell is blocked, the nearest
    /// open cell is used instead.
    /// </summary>
    public List<Vector2>? FindPath(Vector2 from, Vector2 to)
    {
        var start = _map.WorldToCell(from);
        var goal = _map.WorldToCell(to);
        if (!_map.InBounds(start.X, start.Y))
            return null;
        goal = (Math.Clamp(goal.X, 0, _map.Width - 1), Math.Clamp(goal.Y, 0, _map.Height - 1));
        if (!_map.IsPassable(goal.X, goal.Y))
        {
            var near = NearestPassable(goal.X, goal.Y);
            if (near == null) return null;
            goal = near.Value;
            to = _map.CellCenter(goal.X, goal.Y);
        }

        int w = _map.Width;
        int n = w * _map.Height;
        var g = new float[n];
        Array.Fill(g, float.PositiveInfinity);
        var came = new int[n];
        Array.Fill(came, -1);
        var closed = new bool[n];
        var open = new PriorityQueue<int, float>();

        // Cheapest possible cost per metre, keeps the heuristic admissible
        float minCost = TerrainInfo.MoveCost(TerrainType.Road) * _map.CellSize;
        float H(int x, int y)
        {
            int dx = Math.Abs(x - goal.X), dy = Math.Abs(y - goal.Y);
            return (Math.Max(dx, dy) + 0.41421356f * Math.Min(dx, dy)) * minCost;
        }

        int s = start.Y * w + start.X, gi = goal.Y * w + goal.X;
        g[s] = 0;
        open.Enqueue(s, H(start.X, start.Y));
        while (open.Count > 0)
        {
            int cur = open.Dequeue();
            if (closed[cur]) continue;
            if (cur == gi) break;
            closed[cur] = true;
            int cx = cur % w, cy = cur / w;
            foreach (var (dx, dy, dist) in Neighbours)
            {
                int nx = cx + dx, ny = cy + dy;
                if (!_map.InBounds(nx, ny)) continue;
                int ni = ny * w + nx;
                if (closed[ni]) continue;
                float cost = StepCost(cx, cy, nx, ny, dist);
                if (float.IsInfinity(cost)) continue;
                float ng = g[cur] + cost;
                if (ng < g[ni])
                {
                    g[ni] = ng;
                    came[ni] = cur;
                    open.Enqueue(ni, ng + H(nx, ny));
                }
            }
        }
        if (float.IsInfinity(g[gi]))
            return null;

        var cells = new List<int>();
        for (int c = gi; c != -1; c = came[c])
            cells.Add(c);
        cells.Reverse();
        return Simplify(cells, to);
    }

    (int X, int Y)? NearestPassable(int x, int y)
    {
        for (int r = 1; r < 64; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                    if (_map.IsPassable(x + dx, y + dy)) return (x + dx, y + dy);
                }
        return null;
    }

    /// <summary>Drop waypoints in straight runs of the same direction, keep the turns.</summary>
    List<Vector2> Simplify(List<int> cells, Vector2 exactGoal)
    {
        int w = _map.Width;
        var result = new List<Vector2>();
        for (int i = 1; i < cells.Count - 1; i++)
        {
            int ax = cells[i - 1] % w, ay = cells[i - 1] / w;
            int bx = cells[i] % w, by = cells[i] / w;
            int cx = cells[i + 1] % w, cy = cells[i + 1] / w;
            if (bx - ax != cx - bx || by - ay != cy - by)
                result.Add(_map.CellCenter(bx, by));
        }
        result.Add(exactGoal);
        return result;
    }
}
