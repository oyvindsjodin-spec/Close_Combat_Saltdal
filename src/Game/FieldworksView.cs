using System.Collections.Generic;
using System.Linq;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;
using Vec = System.Numerics.Vector2;

namespace Saltdal.Game;

/// <summary>Small drawing helpers shared by the fieldwork views.</summary>
static class Shapes
{
    public static Vector2[] Ellipse(Vector2 c, float rx, float ry, int points = 14)
    {
        var p = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float a = Mathf.Tau * i / points;
            p[i] = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        return p;
    }

    /// <summary>A steady number from 0 to 1 for a cell, so rubble and flames keep their places.</summary>
    public static float Hash(int n, int salt)
    {
        uint h = (uint)(n * 374761393 + salt * 668265263);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) / (float)uint.MaxValue;
    }
}

/// <summary>
/// What lies on the ground under the men: trenches and half-dug scrapes,
/// burnt-out houses, and the rooms of a house the player's men are in, drawn
/// with its roof taken off so they can be seen at the windows.
/// </summary>
public partial class GroundworksView : Node2D
{
    public Simulation? Sim { get; set; }
    public Side PlayerSide { get; set; }
    public bool RevealAll { get; set; }

    // Enemy trenches the player's men have seen; they stay marked once found
    readonly HashSet<int> _seen = new();
    Simulation? _seenIn;
    double _nextLook;

    static readonly Color Spoil = new(0.47f, 0.38f, 0.27f);
    static readonly Color SpoilLight = new(0.58f, 0.49f, 0.36f);
    static readonly Color Slot = new(0.13f, 0.11f, 0.09f);
    static readonly Color Floor = new(0.56f, 0.46f, 0.34f);
    static readonly Color Wall = new(0.24f, 0.19f, 0.16f);
    static readonly Color Window = new(0.72f, 0.78f, 0.80f);
    static readonly Color Char = new(0.15f, 0.13f, 0.12f);

    public override void _Draw()
    {
        if (Sim == null) return;
        if (_seenIn != Sim)
        {
            _seen.Clear();
            _seenIn = Sim;
        }
        foreach (var house in Sim.Houses)
        {
            if (house.State != HouseState.Standing) DrawRuin(house);
            else if (Sim.Teams.Any(t => t.Side == PlayerSide && t.Active.Any(s => Sim.HouseAt(s.Position) == house)))
                DrawRooms(house);
        }

        LookForTrenches();
        foreach (var t in Sim.Trenches)
            if (t.Side == PlayerSide || RevealAll || _seen.Contains(t.Cell))
                DrawTrench(View.ToScreen(t.At), t.Facing, 1f);
        foreach (var team in Sim.Teams)
        {
            if (team.Order != TeamOrder.DigIn || (team.Side != PlayerSide && !RevealAll)) continue;
            foreach (var s in team.Active)
                if (s.DigProgress > 0.02f)
                    DrawTrench(View.ToScreen(s.Position), s.Facing, s.DigProgress);
        }
    }

    /// <summary>
    /// A one-man trench seen from above: a dark slot across the way he faces,
    /// with the dug-out earth heaped in front of it. A part-dug one is shorter and shallower.
    /// </summary>
    void DrawTrench(Vector2 at, float facing, float done)
    {
        float m = View.Scale; // screen units per metre
        DrawSetTransform(at, facing, Vector2.One * m);
        // Drawn larger than life, like the men, so a trench shows round the man in it
        float len = 2.0f + 3.0f * done;
        DrawColoredPolygon(Shapes.Ellipse(new Vector2(1.9f, 0), 0.5f + 0.8f * done, len * 0.6f), Spoil);
        DrawColoredPolygon(Shapes.Ellipse(new Vector2(1.7f, -0.2f), 0.3f + 0.45f * done, len * 0.42f), SpoilLight);
        var slot = new Color(Slot, 0.35f + 0.65f * done);
        DrawRect(new Rect2(-0.9f, -len / 2, 1.6f, len), slot);
        DrawRect(new Rect2(-0.9f, -len / 2, 1.6f, len), new Color(0.32f, 0.26f, 0.2f, 0.9f * done), false, 0.25f);
        DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    /// <summary>Once a second, mark the enemy trenches the player's men can see.</summary>
    void LookForTrenches()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (now < _nextLook) return;
        _nextLook = now + 1;
        var lookers = Sim!.Teams.Where(t => t.Side == PlayerSide).SelectMany(t => t.Active).ToList();
        foreach (var t in Sim.Trenches)
        {
            if (t.Side == PlayerSide || _seen.Contains(t.Cell)) continue;
            foreach (var s in lookers)
                if (Vec.DistanceSquared(s.Position, t.At) < 250 * 250 && Sim.LineOfSight(s.Position, 1.7f, t.At, 0.4f))
                {
                    _seen.Add(t.Cell);
                    break;
                }
        }
    }

    /// <summary>The floor, walls and windows of a house, as if the roof were lifted off.</summary>
    void DrawRooms(House house)
    {
        var map = Sim!.Map;
        float cs = map.CellSize * View.Scale;
        var cells = house.Cells.ToHashSet();
        foreach (var c in house.Cells)
            DrawRect(new Rect2(CellCorner(c), cs, cs), Floor);
        // Floorboards
        foreach (var c in house.Cells)
        {
            var o = CellCorner(c);
            for (int i = 1; i < 4; i++)
                DrawLine(o + new Vector2(0, cs * i / 4), o + new Vector2(cs, cs * i / 4), new Color(Floor.Darkened(0.12f)), 0.6f);
        }
        float wall = 0.45f * View.Scale;
        foreach (var c in house.Cells)
        {
            int x = c % map.Width, y = c / map.Width;
            var o = CellCorner(c);
            // Each side of the cell with no house beyond it is an outside wall, with a window in it
            (int dx, int dy, Vector2 a, Vector2 b)[] sides =
            {
                (0, -1, o, o + new Vector2(cs, 0)),
                (0, 1, o + new Vector2(0, cs), o + new Vector2(cs, cs)),
                (-1, 0, o, o + new Vector2(0, cs)),
                (1, 0, o + new Vector2(cs, 0), o + new Vector2(cs, cs)),
            };
            foreach (var (dx, dy, a, b) in sides)
            {
                int nx = x + dx, ny = y + dy;
                if (map.InBounds(nx, ny) && cells.Contains(ny * map.Width + nx)) continue;
                DrawLine(a, b, Wall, wall);
                var mid = (a + b) / 2;
                var half = (b - a).Normalized() * cs * 0.18f;
                DrawLine(mid - half, mid + half, Window, wall * 0.6f);
            }
        }
    }

    /// <summary>A burnt or burning house: blackened ground and fallen beams.</summary>
    void DrawRuin(House house)
    {
        float cs = Sim!.Map.CellSize * View.Scale;
        foreach (var c in house.Cells)
        {
            var o = CellCorner(c);
            DrawRect(new Rect2(o, cs, cs), Char);
            for (int i = 0; i < 4; i++)
            {
                var p = o + new Vector2(Shapes.Hash(c, i * 2), Shapes.Hash(c, i * 2 + 1)) * cs;
                float angle = Shapes.Hash(c, 20 + i) * Mathf.Pi;
                var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cs * (0.15f + 0.2f * Shapes.Hash(c, 30 + i));
                float g = 0.22f + 0.2f * Shapes.Hash(c, 40 + i);
                DrawLine(p - d, p + d, new Color(g, g * 0.9f, g * 0.85f), 1.4f);
            }
            if (house.State == HouseState.Ruined)
                // Ash and stones
                for (int i = 0; i < 3; i++)
                    DrawCircle(o + new Vector2(Shapes.Hash(c, 50 + i), Shapes.Hash(c, 60 + i)) * cs, 1.2f,
                        new Color(0.45f, 0.43f, 0.40f));
        }
    }

    Vector2 CellCorner(int c)
    {
        var map = Sim!.Map;
        return new Vector2(c % map.Width, c / map.Width) * map.CellSize * View.Scale;
    }
}

/// <summary>
/// Flames on burning houses and the smoke drifting off them with the wind.
/// Drawn over the men, since smoke hides what is under it.
/// </summary>
public partial class FireView : Node2D
{
    public Simulation? Sim { get; set; }
    public float Zoom { get; set; } = 1f;

    public override void _Draw()
    {
        if (Sim == null) return;
        float now = Time.GetTicksMsec() / 1000f; // flicker goes on even while paused
        foreach (var house in Sim.Houses)
            if (house.State == HouseState.Burning)
                DrawFlames(house, now);
        DrawSmoke();
    }

    void DrawFlames(House house, float now)
    {
        var map = Sim!.Map;
        float cs = map.CellSize * View.Scale;
        // The fire takes hold over half a minute and dies down at the end
        float grow = Mathf.Clamp((Sim.Time - house.BurningSince) / 30f, 0.25f, 1f);
        float fade = Mathf.Clamp((house.BurnsOutAt - Sim.Time) / 40f, 0f, 1f);
        float heat = grow * fade;
        var lean = new Vector2(Sim.Wind.X, Sim.Wind.Y).Normalized() * 0.5f + new Vector2(0, -1f);
        lean = lean.Normalized();

        var center = View.ToScreen(house.Center);
        float glow = 0.18f + 0.06f * Mathf.Sin(now * 7f + house.Id);
        DrawCircle(center, cs * (1f + Mathf.Sqrt(house.Cells.Count) * 0.5f), new Color(1f, 0.45f, 0.1f, glow * heat));

        foreach (var c in house.Cells)
        {
            var o = new Vector2(c % map.Width, c / map.Width) * cs;
            for (int i = 0; i < 3; i++)
            {
                var at = o + new Vector2(0.15f + 0.7f * Shapes.Hash(c, i), 0.15f + 0.7f * Shapes.Hash(c, i + 7)) * cs;
                float phase = Shapes.Hash(c, i + 13) * Mathf.Tau;
                float flicker = 0.65f + 0.35f * Mathf.Sin(now * (8f + 3f * i) + phase);
                float size = cs * 0.32f * heat * flicker;
                if (size < 1f) continue;
                DrawTongue(at, lean, size, new Color(0.95f, 0.38f, 0.06f, 0.85f));
                DrawTongue(at + lean * size * 0.15f, lean, size * 0.55f, new Color(1f, 0.82f, 0.3f, 0.95f));
            }
        }
    }

    /// <summary>A tongue of flame: round at the base, drawn out to a point downwind and upward.</summary>
    void DrawTongue(Vector2 at, Vector2 lean, float size, Color color)
    {
        var side = new Vector2(-lean.Y, lean.X);
        // A teardrop, kept convex so it always draws
        var points = new Vector2[8];
        for (int i = 0; i < 7; i++)
        {
            float a = Mathf.Pi * i / 6f; // half circle at the base
            points[i] = at + side * Mathf.Cos(a) * size - lean * Mathf.Sin(a) * size * 0.8f;
        }
        points[7] = at + lean * size * 2.6f;
        DrawColoredPolygon(points, color);
    }

    void DrawSmoke()
    {
        foreach (var p in Sim!.Smoke)
        {
            float age = p.Age(Sim.Time);
            float life = age / p.Life;
            float alpha = 0.5f * Mathf.Pow(1f - Mathf.Clamp(life, 0, 1), 1.3f) * Mathf.Clamp(age / 2f, 0, 1);
            if (alpha < 0.01f) continue;
            var c = View.ToScreen(p.PositionAt(Sim.Time, Sim.Wind));
            float r = p.RadiusAt(Sim.Time) * View.Scale;
            // Dark and dense near the fire, paler and thinner as it spreads
            float g = Mathf.Lerp(0.22f, 0.55f, Mathf.Clamp(life * 1.5f, 0, 1));
            // A soft billow: a faint outer haze and a few lumps that roll slowly
            int seed = (int)(p.Born * 10);
            DrawCircle(c, r, new Color(g, g, g * 0.97f, alpha * 0.22f));
            for (int i = 0; i < 5; i++)
            {
                float a = Shapes.Hash(seed, i) * Mathf.Tau + age * 0.15f;
                float d = r * (0.2f + 0.35f * Shapes.Hash(seed, i + 9));
                float br = r * (0.35f + 0.25f * Shapes.Hash(seed, i + 17));
                float tone = g + 0.08f * (Shapes.Hash(seed, i + 23) - 0.5f);
                DrawCircle(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, br, new Color(tone, tone, tone * 0.97f, alpha * 0.3f));
            }
        }
    }
}
