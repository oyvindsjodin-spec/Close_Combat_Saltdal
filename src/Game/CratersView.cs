using System.Collections.Generic;
using Godot;
using Saltdal.Sim;

namespace Saltdal.Game;

/// <summary>
/// Shell holes that stay on the ground for the rest of the battle: a dark
/// scorch, a ring of thrown-up earth and the hole itself, lit from the
/// north-west like the painted ground. Drawn under the trenches and the men.
/// </summary>
public partial class CratersView : Node2D
{
    Simulation? _sim;
    readonly List<Blast> _craters = new();
    readonly HashSet<(float, float, float)> _seen = new();

    public Simulation? Sim
    {
        get => _sim;
        set
        {
            if (_sim == value) return;
            _sim = value;
            _craters.Clear();
            _seen.Clear();
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        if (_sim == null) return;
        bool added = false;
        foreach (var b in _sim.RecentBlasts)
        {
            if (!_seen.Add((b.At.X, b.At.Y, b.Time))) continue;
            // Nothing stays where a shell lands in water or on a house
            var t = _sim.Map.TerrainAt(b.At);
            if (t is TerrainType.Water or TerrainType.Ford or TerrainType.Building or TerrainType.BurningHouse) continue;
            _craters.Add(b);
            added = true;
        }
        if (added) QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var b in _craters)
            DrawCrater(b);
    }

    void DrawCrater(Blast b)
    {
        var p = View.ToScreen(b.At);
        // The hole is roughly a third of the radius that kills: about 1 m
        // for a small mortar bomb, 3 to 4 m for a heavy bomb
        float r = Mathf.Clamp(b.Explosive.KillRadius * 0.35f, 0.9f, 4.5f) * View.Scale;
        int seed = (int)(b.At.X * 13 + b.At.Y * 7);
        var light = new Vector2(-1, -1).Normalized();

        // Scorched, blackened ground around it
        for (int i = 0; i < 3; i++)
            DrawColoredPolygon(Blob(p, r * (2.5f - i * 0.45f), seed, i), new Color(0.10f, 0.08f, 0.06f, 0.10f));
        // Earth and stones thrown out in clods, more of them close in
        for (int i = 0; i < 34; i++)
        {
            float a = Shapes.Hash(seed, i) * Mathf.Tau;
            float h = Shapes.Hash(seed, i + 40);
            float d = r * (1.0f + 1.4f * h * h);
            float sz = r * (0.05f + 0.09f * Shapes.Hash(seed, i + 80)) + 0.4f;
            var at = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
            DrawCircle(at + new Vector2(sz * 0.5f, sz * 0.5f), sz, new Color(0.05f, 0.04f, 0.03f, 0.30f));
            DrawCircle(at, sz, new Color(0.36f, 0.30f, 0.22f, 0.9f).Lightened(0.15f * Shapes.Hash(seed, i + 120)));
        }
        // The raised lip of earth: lit on the north-west, in shade on the south-east
        DrawColoredPolygon(Blob(p, r * 1.3f, seed, 5), new Color(0.26f, 0.21f, 0.15f, 0.95f));
        DrawColoredPolygon(Blob(p + light * r * 0.15f, r * 1.15f, seed, 6), new Color(0.40f, 0.34f, 0.25f, 0.95f));
        // The hole: its far (south-east) wall catches the light, the near wall is in shadow
        DrawColoredPolygon(Blob(p, r * 0.95f, seed, 7), new Color(0.18f, 0.15f, 0.11f));
        DrawColoredPolygon(Blob(p - light * r * 0.22f, r * 0.62f, seed, 8), new Color(0.27f, 0.22f, 0.16f));
        DrawColoredPolygon(Blob(p - light * r * 0.05f, r * 0.38f, seed, 9), new Color(0.14f, 0.12f, 0.09f));
    }

    /// <summary>A ragged round shape, the same each time for the same crater.</summary>
    static Vector2[] Blob(Vector2 c, float r, int seed, int salt)
    {
        const int n = 18;
        var pts = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = Mathf.Tau * i / n;
            float k = 0.82f + 0.3f * Shapes.Hash(seed * 31 + salt, i);
            pts[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * k;
        }
        return pts;
    }
}
