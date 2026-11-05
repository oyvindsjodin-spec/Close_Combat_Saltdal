using System.Collections.Generic;
using Godot;
using Saltdal.Sim;
using Side = Saltdal.Sim.Side;

namespace Saltdal.Game;

/// <summary>
/// Explosions, burning houses, smoke and gunfire, drawn with the painted
/// pictures from tools/render_effects.py. Drawn over the men, since smoke
/// and fire hide what is under them. Light (flashes, flames, glowing
/// embers, tracer) is drawn a second time on a layer that adds its colour,
/// so it brightens the ground under it the way a real flash does.
/// </summary>
public partial class EffectsView : Node2D
{
    public Simulation? Sim { get; set; }
    public float Zoom { get; set; } = 1f;
    public Side PlayerSide { get; set; } = Side.Allied;
    public bool RevealAll { get; set; }
    /// <summary>How far between the last tick and the next we are drawing (0 to 1).</summary>
    public float Alpha { get; set; }

    /// <summary>Metres a second a bullet covers on screen; slowed from life so the eye can follow it.</summary>
    const float BulletSpeed = 450f;
    /// <summary>How long a blast's smoke and dust stay, at most.</summary>
    const float BlastMemory = 22f;
    const float ShotMemory = 2f;

    readonly List<Blast> _blasts = new();
    readonly List<Shot> _shots = new();
    Simulation? _seen;
    float _lastTime;
    EffectsLight _light = null!;
    EffectSprites? _fx;

    /// <summary>Battle time as drawn: between the last two ticks, like the men.</summary>
    float Now => Sim!.Time + (Alpha - 1) * Simulation.TickSeconds;
    static float M => View.Scale;

    public override void _Ready()
    {
        _light = new EffectsLight { Host = this, Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
        AddChild(_light);
    }

    public override void _Draw()
    {
        _light.QueueRedraw();
        if (Sim == null) return;
        _fx ??= EffectSprites.Get();
        Remember();
        if (_fx == null) return;
        float now = Now;
        var wind = Wind;

        // Earth thrown out by shells lies under their smoke
        foreach (var b in _blasts) DrawBurst(b, now);
        foreach (var s in _shots) DrawImpact(s, now, wind);
        foreach (var b in _blasts) DrawDebris(b, now);
        foreach (var b in _blasts) DrawFireball(b, now, this, 1f);
        // Shadows first so no cloud darkens another
        foreach (var b in _blasts) DrawBlastSmoke(b, now, wind, shadow: true);
        DrawHouseSmoke(shadow: true);
        foreach (var house in Sim.Houses)
            if (house.State == HouseState.Burning)
                DrawFlames(house, this, 1f);
        foreach (var b in _blasts) DrawBlastSmoke(b, now, wind, shadow: false);
        DrawHouseSmoke(shadow: false);
        foreach (var s in _shots) DrawMuzzleSmoke(s, now, wind);
        DrawTracers(now, this, light: false);
    }

    internal void DrawLight(CanvasItem on)
    {
        if (Sim == null || _fx == null) return;
        float now = Now;
        foreach (var house in Sim.Houses)
            if (house.State == HouseState.Burning)
            {
                DrawFireGlow(house, on);
                DrawFlames(house, on, 0.45f);
                DrawEmbers(house, on);
            }
        foreach (var b in _blasts) DrawFlash(b, now, on);
        foreach (var b in _blasts) DrawFireball(b, now, on, 0.25f);
        foreach (var s in _shots) DrawMuzzleGlow(s, now, on);
        DrawTracers(now, on, light: true);
    }

    Vector2 Wind => new(Sim!.Wind.X, Sim.Wind.Y);

    /// <summary>
    /// The simulation forgets blasts after a few seconds and shots after a
    /// moment; keep them a while longer so their smoke and dust can linger.
    /// </summary>
    void Remember()
    {
        if (_seen != Sim || Sim!.Time < _lastTime)
        {
            _blasts.Clear();
            _shots.Clear();
            _seen = Sim;
        }
        _lastTime = Sim!.Time;
        foreach (var b in Sim.RecentBlasts)
            if (!_blasts.Contains(b)) _blasts.Add(b);
        foreach (var s in Sim.RecentShots)
            if (!_shots.Contains(s)) _shots.Add(s);
        _blasts.RemoveAll(b => Sim.Time - b.Time > BlastMemory);
        _shots.RemoveAll(s => Sim.Time - s.Time > ShotMemory);
    }

    static int Seed(Blast b) => (int)(b.At.X * 13.7f) * 7919 + (int)(b.At.Y * 7.3f) + (int)(b.Time * 100);
    static float H(int seed, int i) => Shapes.Hash(seed, i);
    static Vector2 Dir(float a) => new(Mathf.Cos(a), Mathf.Sin(a));
    static float EaseOut(float t) => 1 - (1 - t) * (1 - t);

    bool OnWater(System.Numerics.Vector2 p)
    {
        var t = Sim!.Map.TerrainAt(p);
        return t is TerrainType.Water or TerrainType.Ford;
    }

    // ---------------------------------------------------------------- explosions

    /// <summary>
    /// A blast's size on screen in metres: drawn larger than life, like the men,
    /// so a mortar bomb is about 9 and a 250 kg bomb about 24.
    /// </summary>
    static float Size(Blast b) => 3f + b.Explosive.KillRadius * 1.5f;

    /// <summary>A white-hot flash lighting up the ground for a split second.</summary>
    void DrawFlash(Blast b, float now, CanvasItem on)
    {
        float t = now - b.Time;
        if (t < 0 || t > 0.45f) return;
        float u = t / 0.45f;
        var p = View.ToScreen(b.At);
        float r = Size(b) * M;
        _fx!.Draw(on, "glow", p, r * (5f + 2f * u), 0, new Color(1f, 0.75f, 0.45f, 0.9f * (1 - u) * (1 - u)));
        _fx.Draw(on, "glow", p, r * 2.2f, 0, new Color(1f, 0.95f, 0.8f, 1 - u));
        // Sparks and burning bits thrown out
        int seed = Seed(b);
        for (int i = 0; i < 10; i++)
        {
            var d = Dir(H(seed, i + 300) * Mathf.Tau);
            float dist = r * (0.6f + 2.4f * H(seed, i + 320)) * EaseOut(u);
            _fx.Draw(on, "glow", p + d * dist, r * 0.35f, 0, new Color(1f, 0.6f, 0.2f, 0.9f * (1 - u)));
        }
    }

    /// <summary>The ball of fire and hot gas, rolling outward and going dark.</summary>
    void DrawFireball(Blast b, float now, CanvasItem on, float strength)
    {
        float t = now - b.Time;
        const float life = 0.7f;
        if (t < 0 || t > life) return;
        float u = t / life;
        var p = View.ToScreen(b.At);
        float r = Size(b) * M;
        int seed = Seed(b);
        if (OnWater(b.At))
        {
            // A column of water instead of fire
            if (on == this)
                _fx!.Draw(on, "smoke" + (seed & 3), p, r * (1.6f + 1.6f * Mathf.Sqrt(u)), H(seed, 1) * Mathf.Tau,
                    new Color(0.85f, 0.9f, 0.95f, 0.9f * (1 - u)));
            return;
        }
        // Several rolls of fire bursting out from the middle, reddening and going sooty
        for (int i = 0; i < 4; i++)
        {
            float grow = (0.7f + 0.9f * Mathf.Sqrt(u)) * (0.8f + 0.4f * H(seed, 30 + i));
            var off = Dir(H(seed, 40 + i) * Mathf.Tau) * r * (i == 0 ? 0.05f : 0.3f + 0.35f * EaseOut(u));
            float dark = 1 - 0.55f * u;
            var c = new Color(dark, dark * (1f - 0.4f * u), dark * (1f - 0.7f * u), strength * Mathf.Pow(1 - u, 1.2f));
            _fx!.Draw(on, "fireball" + ((seed + i) & 3), p + off, r * grow, H(seed, 50 + i) * Mathf.Tau + u * (i % 2 == 0 ? 0.8f : -0.8f), c);
        }
    }

    /// <summary>The spray of earth around the hole, flung out at once and then settling.</summary>
    void DrawBurst(Blast b, float now)
    {
        float t = now - b.Time;
        if (t < 0 || t > 6f) return;
        var p = View.ToScreen(b.At);
        float r = Size(b) * M;
        int seed = Seed(b);
        bool water = OnWater(b.At);
        float grow = 1.4f + 1.9f * EaseOut(Mathf.Min(t / 0.25f, 1));
        float alpha = t < 1.5f ? 1f : Mathf.Clamp(1 - (t - 1.5f) / 4.5f, 0, 1);
        var tint = water ? new Color(0.85f, 0.92f, 1f, 0.55f * alpha) : new Color(0.46f, 0.37f, 0.27f, 0.95f * alpha);
        _fx!.Draw(this, "burst" + Mathf.PosMod(seed, 3), p, r * grow, H(seed, 2) * Mathf.Tau, tint);
        // A ring of dust kicked up along the ground by the blast wave
        if (t < 2.2f && !water)
        {
            float u = t / 2.2f;
            for (int i = 0; i < 12; i++)
            {
                var d = Dir((i + H(seed, i + 60)) / 12f * Mathf.Tau);
                float dist = r * (0.8f + 2.4f * EaseOut(Mathf.Min(t / 0.8f, 1)) * (0.8f + 0.4f * H(seed, i + 70)));
                _fx.Draw(this, "smoke" + (i % 6), p + d * dist, r * (0.9f + 1.1f * u), H(seed, i + 80) * Mathf.Tau,
                    new Color(0.6f, 0.53f, 0.42f, 0.5f * (1 - u)));
            }
        }
    }

    /// <summary>Clods of earth and stones thrown up, each with its shadow, falling back.</summary>
    void DrawDebris(Blast b, float now)
    {
        float t = now - b.Time;
        float size = Size(b);
        float flight = 0.7f + size * 0.06f;
        if (t < 0 || t > flight || OnWater(b.At)) return;
        var p = View.ToScreen(b.At);
        int seed = Seed(b);
        int n = 14 + (int)(size * 2);
        for (int i = 0; i < n; i++)
        {
            float f = flight * (0.6f + 0.4f * H(seed, i + 100));
            if (t > f) continue;
            float u = t / f;
            var d = Dir(H(seed, i + 110) * Mathf.Tau);
            float dist = size * (0.8f + 2.8f * H(seed, i + 120)) * u;
            float height = size * (0.8f + 2.5f * H(seed, i + 130)) * 4f * u * (1 - u);
            var ground = p + d * dist * M;
            float rad = (0.1f + 0.22f * H(seed, i + 140)) * (0.6f + size * 0.04f) * M;
            // The shadow falls to the south-east, away from the light
            DrawClod(ground + new Vector2(0.5f, 0.5f) * height * M, rad, seed + i, u, new Color(0, 0, 0, 0.28f));
            var at = ground + new Vector2(0, -0.6f) * height * M;
            DrawClod(at, rad, seed + i, u, new Color(0.16f, 0.12f, 0.08f));
            DrawClod(at - new Vector2(0.25f, 0.25f) * rad, rad * 0.55f, seed + i, u, new Color(0.4f, 0.33f, 0.24f));
        }
    }

    /// <summary>A lump of earth or stone: a rough, tumbling shape.</summary>
    void DrawClod(Vector2 at, float rad, int seed, float spin, Color c)
    {
        // Two lumps stuck together, turning as they fly
        var d = Dir(H(seed, 7) * Mathf.Tau + spin * 9f) * rad * 0.45f;
        DrawCircle(at + d, rad * 0.75f, c);
        DrawCircle(at - d, rad * (0.5f + 0.3f * H(seed, 8)), c);
    }

    /// <summary>
    /// A column of smoke and dust: dark and brown at first, rising, spreading
    /// and turning pale grey as the wind takes it away.
    /// </summary>
    void DrawBlastSmoke(Blast b, float now, Vector2 wind, bool shadow)
    {
        float t = now - b.Time;
        if (t < 0) return;
        float size = Size(b);
        float life = 8f + size * 0.7f;
        int seed = Seed(b);
        int n = 6 + (int)(size * 0.5f);
        bool water = OnWater(b.At);
        var p = View.ToScreen(b.At);
        for (int i = 0; i < n; i++)
        {
            float born = 0.05f + 0.1f * i * H(seed, i + 200);
            float age = t - born;
            float l = life * (0.6f + 0.4f * H(seed, i + 210));
            if (age < 0 || age > l) continue;
            float u = age / l;
            var start = Dir(H(seed, i + 220) * Mathf.Tau) * size * 0.45f * H(seed, i + 230);
            // Rises (drifts up the screen) and is carried off by the wind
            var drift = wind * age * (0.7f + 0.6f * H(seed, i + 240)) + new Vector2(0, -0.5f) * age;
            var at = p + (start * (1 + 0.5f * EaseOut(Mathf.Min(age / 1.5f, 1))) + drift) * M;
            float r = size * (0.9f + 1.4f * Mathf.Sqrt(u)) * M;
            float alpha = 0.8f * Mathf.Pow(1 - u, 1.3f) * Mathf.Clamp(age / 0.2f, 0, 1);
            float rot = H(seed, i + 250) * Mathf.Tau + age * 0.08f * (i % 2 == 0 ? 1 : -1);
            string pic = "smoke" + Mathf.PosMod(seed + i, 6);
            if (shadow)
            {
                _fx!.Draw(this, pic, at + new Vector2(0.45f, 0.45f) * size * M * (1 + u), r, rot, new Color(0, 0, 0, alpha * 0.18f));
                continue;
            }
            // Brown dust and black fumes at first, pale grey smoke later
            float fresh = Mathf.Clamp(1 - age / 3f, 0, 1);
            Color c = water ? new Color(0.88f, 0.9f, 0.93f)
                : new Color(0.66f, 0.65f, 0.62f).Lerp(new Color(0.3f, 0.27f, 0.24f), fresh);
            c.A = alpha * (water ? 0.6f : 1f);
            _fx!.Draw(this, pic, at, r, rot, c);
        }
    }

    // ---------------------------------------------------------------- burning houses

    float Heat(House house)
    {
        // The fire takes hold over half a minute and dies down at the end
        float grow = Mathf.Clamp((Sim!.Time - house.BurningSince) / 30f, 0.25f, 1f);
        float fade = Mathf.Clamp((house.BurnsOutAt - Sim.Time) / 40f, 0f, 1f);
        return Mathf.Max(grow, 0.55f) * fade;
    }

    /// <summary>Flames lean with the wind and up the screen, since we look a little from the south.</summary>
    Vector2 Lean => (Wind.Normalized() * 0.6f + new Vector2(0, -1f)).Normalized();

    static float Clock => Time.GetTicksMsec() / 1000f; // flicker goes on even while paused

    void DrawFireGlow(House house, CanvasItem on)
    {
        float heat = Heat(house);
        float cs = Sim!.Map.CellSize * M;
        float now = Clock;
        float flicker = 0.8f + 0.12f * Mathf.Sin(now * 7f + house.Id) + 0.08f * Mathf.Sin(now * 13.3f + house.Id * 2);
        float r = cs * (2f + Mathf.Sqrt(house.Cells.Count) * 1.6f);
        _fx!.Draw(on, "glow", View.ToScreen(house.Center), r * 2f, 0, new Color(1f, 0.45f, 0.12f, 0.55f * heat * flicker));
    }

    void DrawFlames(House house, CanvasItem on, float strength)
    {
        var map = Sim!.Map;
        float cs = map.CellSize * M;
        float heat = Heat(house);
        var lean = Lean;
        float angle = Mathf.Atan2(lean.X, -lean.Y);
        float now = Clock;
        foreach (var c in house.Cells)
        {
            var o = new Vector2(c % map.Width, c / map.Width) * cs;
            // Not every part of the house burns as hard: flames bunch and gap
            int count = Shapes.Hash(c, 3) < 0.3f ? 0 : Shapes.Hash(c, 4) < 0.5f ? 1 : 2;
            for (int i = 0; i < count; i++)
            {
                var at = o + new Vector2(-0.2f + 1.4f * Shapes.Hash(c, i), -0.2f + 1.4f * Shapes.Hash(c, i + 7)) * cs;
                float phase = Shapes.Hash(c, i + 13);
                int frame = Mathf.PosMod((int)(now * (10f + 4f * phase) + phase * 8), 8);
                // Each tongue swells and dies back on its own rhythm
                float flicker = 0.75f + 0.25f * Mathf.Sin(now * (2.3f + 1.7f * phase) + phase * Mathf.Tau);
                float size = cs * (2.4f + 1.8f * Shapes.Hash(c, i + 21)) * heat * flicker;
                if (size < 1f) continue;
                string pic = (Shapes.Hash(c, i + 31) < 0.5f ? "flame" : "flameb") + frame;
                _fx!.Draw(on, pic, at, size, angle + (phase - 0.5f) * 0.5f, new Color(1, 1, 1, strength));
            }
        }
    }

    /// <summary>Glowing embers rising from the fire and carried off downwind.</summary>
    void DrawEmbers(House house, CanvasItem on)
    {
        float heat = Heat(house);
        float cs = Sim!.Map.CellSize * M;
        var lean = Lean;
        float now = Clock;
        var centre = View.ToScreen(house.Center);
        int n = 4 + house.Cells.Count / 2;
        for (int i = 0; i < n; i++)
        {
            float period = 1.6f + Shapes.Hash(house.Id, i + 400);
            float u = Mathf.PosMod(now / period + Shapes.Hash(house.Id, i + 410), 1f);
            var start = new Vector2(Shapes.Hash(house.Id, i + 420) - 0.5f, Shapes.Hash(house.Id, i + 430) - 0.5f) * cs * Mathf.Sqrt(house.Cells.Count);
            var wobble = new Vector2(Mathf.Sin(now * 3f + i), 0) * cs * 0.3f;
            var at = centre + start + lean * u * cs * 6f + wobble * u;
            _fx!.Draw(on, "glow", at, cs * 0.35f, 0, new Color(1f, 0.55f, 0.15f, heat * (1 - u) * 0.9f));
        }
    }

    /// <summary>The smoke from burning houses, puffs the simulation tracks because they block sight.</summary>
    void DrawHouseSmoke(bool shadow)
    {
        foreach (var p in Sim!.Smoke)
        {
            float age = p.Age(Sim.Time);
            float life = Mathf.Clamp(age / p.Life, 0, 1);
            float alpha = 0.75f * Mathf.Pow(1f - life, 1.3f) * Mathf.Clamp(age / 2f, 0, 1);
            if (alpha < 0.01f) continue;
            var c = View.ToScreen(p.PositionAt(Sim.Time, Sim.Wind));
            float r = p.RadiusAt(Sim.Time) * M * 2.1f;
            int seed = (int)(p.Born * 10);
            // Dark and oily near the fire, paler and thinner as it spreads
            float g = Mathf.Lerp(0.16f, 0.55f, Mathf.Clamp(life * 1.6f, 0, 1));
            for (int i = 0; i < 2; i++)
            {
                var off = Dir(H(seed, i) * Mathf.Tau) * r * 0.18f * i;
                float rot = H(seed, i + 5) * Mathf.Tau + age * 0.04f * (i == 0 ? 1 : -1);
                string pic = "smoke" + Mathf.PosMod(seed + i, 6);
                if (shadow)
                    _fx!.Draw(this, pic, c + off + new Vector2(0.35f, 0.35f) * r, r * (1 - 0.15f * i), rot, new Color(0, 0, 0, alpha * 0.15f));
                else
                    _fx!.Draw(this, pic, c + off, r * (1 - 0.15f * i), rot, new Color(g, g * 0.98f, g * 0.95f, alpha));
            }
        }
    }

    // ---------------------------------------------------------------- gunfire

    Soldier? Shooter(Shot s)
    {
        foreach (var team in Sim!.Teams)
            foreach (var m in team.Soldiers)
                if (m.Id == s.ShooterId) return m;
        return null;
    }

    static bool IsMachineGun(Soldier? s) => s != null && s.Weapon.Burst >= 5 && s.Weapon.Range >= 800;

    /// <summary>Whether the player may see where this shot came from.</summary>
    bool ShooterSeen(Shot s, Soldier? shooter) =>
        s.Side == PlayerSide || RevealAll || shooter == null || shooter.IsVisibleToEnemy(Sim!.Time);

    /// <summary>
    /// Each round as a short streak flying from the gun to where it lands,
    /// over a faint line that shows for a moment who is shooting at whom.
    /// Machine guns fire tracer: bright and orange. Rifles show as pale streaks.
    /// </summary>
    void DrawTracers(float now, CanvasItem on, bool light)
    {
        float k = 1f / Zoom;
        foreach (var s in _shots)
        {
            float t = now - s.Time;
            if (t < 0) continue;
            var shooter = Shooter(s);
            var from = View.ToScreen(s.From);
            var to = View.ToScreen(s.To);
            float dist = (to - from).Length();
            if (dist < 1f) continue;
            var dir = (to - from) / dist;
            // Hide the shooter's end of an unseen enemy's fire; the end near the target still shows
            if (!ShooterSeen(s, shooter))
                from = to - dir * Mathf.Min(dist, 40f * M);
            float travelled = BulletSpeed * M * t;
            float total = (to - View.ToScreen(s.From)).Length();
            bool mg = IsMachineGun(shooter);
            if (!light && t < 0.35f)
            {
                float a = 0.22f * (1 - t / 0.35f);
                on.DrawLine(from, to, s.Side == Side.Allied ? new Color(1f, 0.92f, 0.65f, a) : new Color(1f, 0.75f, 0.6f, a), 1f * k, true);
            }
            if (travelled > total) continue;
            var head = View.ToScreen(s.From) + dir * travelled;
            if ((head - to).Dot(dir) > 0) continue;
            float len = (mg ? 9f : 5f) * M;
            var tail = head - dir * len;
            if ((tail - from).Dot(dir) < 0) tail = from;
            if ((head - from).Dot(dir) < 0) continue;
            if (light)
            {
                if (mg) on.DrawLine(tail, head, new Color(1f, 0.45f, 0.15f, 0.45f), 4f * k, true);
                continue;
            }
            var core = mg ? new Color(1f, 0.82f, 0.45f, 1f) : new Color(1f, 0.97f, 0.85f, 0.9f);
            on.DrawLine(tail, head, core, (mg ? 1.8f : 1.1f) * k, true);
        }
    }

    /// <summary>Where a missed round lands: a little spurt of dust (or a splash on water).</summary>
    void DrawImpact(Shot s, float now, Vector2 wind)
    {
        float arrive = System.Numerics.Vector2.Distance(s.From, s.To) / BulletSpeed;
        float t = now - s.Time - arrive;
        if (t < 0 || t > 0.9f || s.Hit) return;
        float u = t / 0.9f;
        int seed = (int)(s.Time * 1000) + s.ShooterId * 31;
        var p = View.ToScreen(s.To) + wind * t * M;
        bool water = OnWater(s.To);
        var c = water ? new Color(0.9f, 0.94f, 1f, 0.75f * (1 - u)) : new Color(0.55f, 0.47f, 0.36f, 0.85f * (1 - u));
        _fx!.Draw(this, "smoke" + Mathf.PosMod(seed, 6), p + new Vector2(0, -0.6f) * u * M, (0.8f + 1.6f * EaseOut(u)) * M, H(seed, 1) * Mathf.Tau, c);
        // A few grains of earth flicked up
        if (!water && t < 0.35f)
            for (int i = 0; i < 3; i++)
            {
                var d = Dir(H(seed, i + 5) * Mathf.Tau);
                float v = t / 0.35f;
                DrawCircle(p + (d * 1.2f * v + new Vector2(0, -1.5f * v * (1 - v))) * M, 0.12f * M, new Color(0.2f, 0.16f, 0.11f, 1 - v));
            }
    }

    /// <summary>Where the gun's muzzle is on screen, allowing for the figures being drawn larger than life.</summary>
    Vector2 Muzzle(Shot s)
    {
        var dir = (View.ToScreen(s.To) - View.ToScreen(s.From)).Normalized();
        return View.ToScreen(s.From) + dir * 1.2f * UnitsView.FigureSize(Zoom) * M;
    }

    /// <summary>A wisp of powder smoke at the muzzle after each round, carried off by the wind.</summary>
    void DrawMuzzleSmoke(Shot s, float now, Vector2 wind)
    {
        float t = now - s.Time;
        if (t < 0 || t > 1.6f) return;
        var shooter = Shooter(s);
        if (!ShooterSeen(s, shooter)) return;
        float u = t / 1.6f;
        int seed = (int)(s.Time * 1000) + s.ShooterId * 31;
        var dir = (View.ToScreen(s.To) - View.ToScreen(s.From)).Normalized();
        var p = Muzzle(s) + dir * 1.2f * EaseOut(u) * M + wind * t * M;
        float size = (IsMachineGun(shooter) ? 1.6f : 1.1f) * (0.6f + 1.4f * Mathf.Sqrt(u)) * M;
        _fx!.Draw(this, "smoke" + Mathf.PosMod(seed + 3, 6), p, size, H(seed, 9) * Mathf.Tau, new Color(0.85f, 0.84f, 0.8f, 0.5f * (1 - u)));
    }

    /// <summary>The ground lit for an instant around a firing gun.</summary>
    void DrawMuzzleGlow(Shot s, float now, CanvasItem on)
    {
        float t = now - s.Time;
        if (t < 0 || t > 0.14f) return;
        var shooter = Shooter(s);
        if (!ShooterSeen(s, shooter)) return;
        var dir = (View.ToScreen(s.To) - View.ToScreen(s.From)).Normalized();
        var p = Muzzle(s);
        float r = (IsMachineGun(shooter) ? 4.5f : 3f) * M;
        _fx!.Draw(on, "glow", p, r, 0, new Color(1f, 0.7f, 0.35f, 0.8f * (1 - t / 0.14f)));
    }
}

/// <summary>The layer for light: everything drawn on it adds to the colours under it.</summary>
public partial class EffectsLight : Node2D
{
    public EffectsView? Host { get; set; }
    public override void _Draw() => Host?.DrawLight(this);
}
