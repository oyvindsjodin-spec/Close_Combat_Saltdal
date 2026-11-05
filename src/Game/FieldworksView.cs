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

    EffectSprites? _fx;

    // Enemy trenches the player's men have seen; they stay marked once found
    readonly HashSet<int> _seen = new();
    Simulation? _seenIn;
    double _nextLook;

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
        _fx ??= EffectSprites.Get();
        foreach (var house in Sim.Houses)
        {
            if (house.State == HouseState.Ruined) DrawRuin(house);
            else if (Sim.Teams.Any(t => t.Side == PlayerSide && t.Active.Any(s => Sim.HouseAt(s.Position) == house)))
            {
                DrawRooms(house);
                DrawDamage(house, roofOff: true);
            }
            else
            {
                DrawDamage(house, roofOff: false);
                if (house.State == HouseState.Burning) DrawBurning(house);
            }
        }

        LookForTrenches();
        foreach (var t in Sim.Trenches)
            if (t.Side == PlayerSide || RevealAll || _seen.Contains(t.Cell))
                DrawTrench(View.ToScreen(t.At), t.Facing, 1f, t.Cell);
        foreach (var team in Sim.Teams)
        {
            if (team.Order != TeamOrder.DigIn || (team.Side != PlayerSide && !RevealAll)) continue;
            foreach (var s in team.Active)
                if (s.DigProgress > 0.02f)
                    {
                    var (cx, cy) = Sim.Map.WorldToCell(s.Position);
                    DrawTrench(View.ToScreen(s.Position), s.Facing, s.DigProgress, cy * Sim.Map.Width + cx);
                }
        }
    }

    // Fresh-dug earth in the painted look, lit from the north-west (top left)
    static readonly Color EarthShade = new(0.25f, 0.20f, 0.14f);
    static readonly Color Earth = new(0.39f, 0.32f, 0.23f);
    static readonly Color EarthLit = new(0.58f, 0.50f, 0.37f);
    static readonly Color PitWall = new(0.31f, 0.24f, 0.17f);
    static readonly Color PitFloor = new(0.12f, 0.10f, 0.08f);
    static readonly Vector2 ToLight = new Vector2(-1f, -1.15f).Normalized();

    /// <summary>
    /// A one-man foxhole seen from above, painted like the ground: a round,
    /// dark pit with the dug earth thrown up into a low parapet round it,
    /// heaped highest in front. The sun from the north-west lights the near
    /// slopes, casts a soft shadow to the south-east and leaves the north-west
    /// inner wall of the pit in shade. A few sods are laid on the front for
    /// cover. A part-dug hole is smaller, with less earth thrown up.
    /// </summary>
    void DrawTrench(Vector2 at, float facing, float done, int seed)
    {
        float m = View.Scale; // screen units per metre
        float grow = 0.45f + 0.55f * done;
        var fwd = new Vector2(Mathf.Cos(facing), Mathf.Sin(facing));
        var across = new Vector2(-fwd.Y, fwd.X);
        // Drawn larger than life, like the men, so the hole shows round the man in it
        Vector2 P(float f, float a) => at + (fwd * f + across * a) * m;

        // The earth is thrown out forward into a crescent-shaped parapet, with a
        // lower rim of it round the sides and back of the pit
        var pit = P(-0.2f * grow, 0);
        float pf = 0.95f * grow, pa = 1.2f * grow;
        var mound = P(0.95f * grow, 0);
        float mf = 1.25f * grow, ma = 2.45f * grow;
        // Soft shadow cast to the south-east, two layers for a blurred edge
        var shadow = -ToLight * 0.6f * m * grow;
        DrawColoredPolygon(Blob(mound + shadow * 1.4f, fwd, mf * 1.1f, ma * 1.06f, seed, 0), new Color(0.05f, 0.05f, 0.02f, 0.10f * done + 0.03f));
        DrawColoredPolygon(Blob(mound + shadow, fwd, mf, ma, seed, 0), new Color(0.05f, 0.05f, 0.02f, 0.14f * done + 0.04f));
        // Loose earth scattered thinly onto the grass
        DrawColoredPolygon(Blob(P(0.4f * grow, 0), fwd, 2.1f * grow, 2.7f * grow, seed, 9), new Color(Earth, 0.12f * done));
        // The rim round the pit
        DrawColoredPolygon(Blob(pit, fwd, pf * 1.6f, pa * 1.5f, seed, 6), new Color(EarthShade, 0.85f));
        DrawColoredPolygon(Blob(pit + ToLight * 0.15f * m, fwd, pf * 1.45f, pa * 1.38f, seed, 7), new Color(Earth, 0.85f));
        // The parapet: dark base, then lighter towards the sun in soft steps
        DrawColoredPolygon(Blob(mound, fwd, mf, ma, seed, 0), EarthShade);
        DrawColoredPolygon(Blob(mound + ToLight * 0.2f * m, fwd, mf * 0.88f, ma * 0.9f, seed, 1), Earth);
        DrawColoredPolygon(Blob(mound + ToLight * 0.4f * m, fwd, mf * 0.62f, ma * 0.72f, seed, 2), new Color(EarthLit, 0.45f));
        DrawColoredPolygon(Blob(mound + ToLight * 0.55f * m, fwd, mf * 0.38f, ma * 0.5f, seed, 3), new Color(EarthLit, 0.35f));
        // Clods and stones, faint, each with a speck of shadow
        for (int i = 0; i < 6; i++)
        {
            float t = Shapes.Hash(seed, 100 + i) * Mathf.Tau;
            float r = 0.3f + 0.55f * Shapes.Hash(seed, 120 + i);
            var c = mound + (fwd * Mathf.Cos(t) * mf + across * Mathf.Sin(t) * ma) * r * m;
            float size = (0.1f + 0.08f * Shapes.Hash(seed, 140 + i)) * m;
            DrawCircle(c - ToLight * size * 0.7f, size, new Color(EarthShade, 0.45f * done));
            DrawCircle(c, size * 0.8f, new Color(EarthLit, 0.5f * done));
        }

        // The pit: its lit south-east inner wall, then the shaded floor set towards the north-west
        DrawColoredPolygon(Blob(pit, fwd, pf, pa, seed, 4), new Color(PitWall, 0.4f + 0.6f * done));
        DrawColoredPolygon(Blob(pit + ToLight * 0.2f * m, fwd, pf * 0.8f, pa * 0.82f, seed, 5), new Color(PitFloor, 0.3f + 0.7f * done));
    }

    /// <summary>
    /// A lumpy oval in screen space: f metres along a direction, a metres
    /// across it, its outline wobbling steadily for a given seed.
    /// </summary>
    static Vector2[] Blob(Vector2 c, Vector2 fwd, float f, float a, int seed, int salt, int points = 18)
    {
        var across = new Vector2(-fwd.Y, fwd.X);
        float m = View.Scale;
        var p = new Vector2[points];
        for (int i = 0; i < points; i++)
        {
            float t = Mathf.Tau * i / points;
            float wob = 1f + 0.16f * (Shapes.Hash(seed * 31 + salt, i) - 0.5f) + 0.06f * Mathf.Sin(t * 3 + salt);
            p[i] = c + (fwd * Mathf.Cos(t) * f + across * Mathf.Sin(t) * a) * wob * m;
        }
        return p;
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

    Vector2 CellMiddle(int c) => CellCorner(c) + Vector2.One * Sim!.Map.CellSize * View.Scale * 0.5f;

    /// <summary>
    /// What shells have done to a house still standing: broken tiles strewn
    /// round each hit, a hole torn through the roof where it struck, and once
    /// the house is badly knocked about, more holes where near misses shook
    /// the roof loose. With the roof lifted off to show the men inside, only
    /// the fallen tiles and plaster on the floors show.
    /// </summary>
    void DrawDamage(House house, bool roofOff)
    {
        if (_fx == null) return;
        float m = View.Scale;
        float cs = Sim!.Map.CellSize * m;
        for (int i = 0; i < house.Hits.Count; i++)
        {
            var hit = house.Hits[i];
            int seed = house.Id * 97 + i;
            var at = View.ToScreen(hit.At);
            _fx.Draw(this, "debris" + (seed % 3), at, (hit.Size * 1.6f + 3.5f) * m, Shapes.Hash(seed, 1) * Mathf.Tau, new Color(0.9f, 0.86f, 0.8f, 0.8f));
            if (roofOff) continue;
            // Bigger shells tear bigger holes, but never so big the roof seems gone
            float size = Mathf.Min(Mathf.Clamp(hit.Size * 0.8f + 1f, 2.5f, 5f), Mathf.Sqrt(house.Cells.Count) * Sim.Map.CellSize * 0.45f) * m;
            _fx.Draw(this, "roofhole" + (seed % 4), at, size, Shapes.Hash(seed, 2) * Mathf.Tau, Colors.White);
        }
        if (roofOff || house.Wear < 0.25f) return;
        // Tiles shaken off and rafters showing where near misses hit, more of them as the house is battered
        int holes = Mathf.Min(house.Cells.Count, (int)((house.Wear - 0.15f) * house.Cells.Count * 0.5f) + 1);
        for (int i = 0; i < holes; i++)
        {
            int c = house.Cells[(int)(Shapes.Hash(house.Id, 300 + i) * house.Cells.Count) % house.Cells.Count];
            var at = CellCorner(c) + new Vector2(0.25f + 0.5f * Shapes.Hash(c, 310 + i), 0.25f + 0.5f * Shapes.Hash(c, 320 + i)) * cs;
            float size = cs * (0.35f + 0.25f * Shapes.Hash(c, 330 + i) + 0.2f * house.Wear);
            _fx.Draw(this, "roofhole" + (int)(Shapes.Hash(c, 340 + i) * 4), at, size, Shapes.Hash(c, 350 + i) * Mathf.Tau, Colors.White);
        }
    }

    /// <summary>
    /// A house on fire: the roofing chars and blackens where the fire has
    /// reached, spreading from where it started, and later burns through so
    /// the blackened rooms and fallen beams show.
    /// </summary>
    void DrawBurning(House house)
    {
        if (_fx == null) { DrawRuin(house); return; }
        float cs = Sim!.Map.CellSize * View.Scale;
        float now = Sim.Time;
        foreach (var c in house.Cells)
        {
            var mid = CellMiddle(c);
            float f = house.FireAt(View.ToWorld(mid), now);
            if (f <= 0.05f) continue;
            _fx.Draw(this, "char" + (c % 2), mid, cs * (1.1f + 0.7f * f), Shapes.Hash(c, 70) * Mathf.Tau, new Color(1, 1, 1, Mathf.Min(1, f * 1.1f)));
        }
        // Burnt through: the roof falls into the house
        float through = (now - house.BurningSince) / Mathf.Max(1f, house.BurnsOutAt - house.BurningSince);
        float a = Mathf.Clamp((through - 0.4f) / 0.3f, 0, 1);
        if (a > 0) DrawCharredRooms(house, a);
    }

    /// <summary>A burnt or shot-down house: blackened ground, fallen beams and a heap of rubble.</summary>
    void DrawRuin(House house)
    {
        float cs = Sim!.Map.CellSize * View.Scale;
        if (house.Burnt) DrawCharredRooms(house, 1f);
        else
            // Shot to pieces: walls and roof down in a dusty heap, no fire
            foreach (var c in house.Cells)
                DrawRect(new Rect2(CellCorner(c), cs, cs), new Color(0.36f, 0.32f, 0.27f, 0.85f));
        if (_fx == null) return;
        var tint = house.Burnt ? new Color(0.42f, 0.39f, 0.37f, 0.9f) : new Color(0.95f, 0.92f, 0.88f, 1f);
        foreach (var c in house.Cells)
        {
            var at = CellMiddle(c) + new Vector2(Shapes.Hash(c, 80) - 0.5f, Shapes.Hash(c, 81) - 0.5f) * cs * 0.4f;
            _fx.Draw(this, "rubble" + (int)(Shapes.Hash(c, 82) * 3), at, cs * (1.6f + 0.5f * Shapes.Hash(c, 83)), Shapes.Hash(c, 84) * Mathf.Tau, tint);
        }
        if (!house.Burnt)
            for (int i = 0; i < house.Hits.Count; i++)
                _fx.Draw(this, "debris" + (i % 3), View.ToScreen(house.Hits[i].At), (house.Hits[i].Size * 1.6f + 3.5f) * View.Scale,
                    Shapes.Hash(house.Id * 97 + i, 1) * Mathf.Tau, new Color(0.9f, 0.86f, 0.8f, 0.8f));
    }

    /// <summary>Blackened floors and fallen, charred beams where the roof has burnt away.</summary>
    void DrawCharredRooms(House house, float alpha)
    {
        float cs = Sim!.Map.CellSize * View.Scale;
        foreach (var c in house.Cells)
        {
            var o = CellCorner(c);
            DrawRect(new Rect2(o, cs, cs), new Color(Char, alpha));
            for (int i = 0; i < 4; i++)
            {
                var p = o + new Vector2(Shapes.Hash(c, i * 2), Shapes.Hash(c, i * 2 + 1)) * cs;
                float angle = Shapes.Hash(c, 20 + i) * Mathf.Pi;
                var d = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * cs * (0.15f + 0.2f * Shapes.Hash(c, 30 + i));
                float g = 0.22f + 0.2f * Shapes.Hash(c, 40 + i);
                DrawLine(p - d, p + d, new Color(g, g * 0.9f, g * 0.85f, alpha), 1.4f);
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
